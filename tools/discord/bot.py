"""The Do Damage Discord bot. On the VPS it runs as bsk-discord (deploy/systemd/bsk-discord.service),
with its settings in /opt/bsk/discord/discord.env (from deploy/discord.local.env; see SETUP-LOCAL.md,
"Discord"). It needs discord.py (requirements.txt).

    python tools/discord/bot.py --env deploy/discord.local.env       # run it
    python tools/discord/bot.py --dry-run                            # print what it would post, no Discord
    python tools/discord/bot.py --dry-run --release 2.2.0            # ...for that release (from the changelog)
    python tools/discord/bot.py --env F --post-release 2.2.1 --channel ID   # post one release there (a test)
    python tools/discord/bot.py --env F --as-member 123 --try "link ABC234" # a linked-account command, no Discord

What it does:
- Release posts: every minute it reads the published desktop client's latest.json
  (BSK_RELEASES, /opt/bsk/dist/client/windows/latest.json on the VPS, which only deploy/deploy.py
  changes, after the release is merged and published). A version newer than any it has posted goes
  to #updates with that version's changelog, pinging the Updates role. Each version is posted
  once: the posted versions are kept in the state file, and before posting it also looks for the
  post among #updates' recent messages. On its first start (no state file) it only notes the
  current version, or DISCORD_POSTED_UP_TO when that's set. Test publishes (--publish-to, the isolated stacks) write elsewhere and never
  reach that file, and without BSK_RELEASES it posts nothing.
- A message in #server-status that it keeps up to date (players, 24-hour peak, uptime).
- Slash commands: /status, /online, /wiki <term>, /play, and /updates (the Updates role on or off).
- Linked game accounts: /link <code> (the code from the game's main menu, Link Discord), then
  /account, /resetpassword (a new password, shown to that member only) and /unlink. All answered
  privately. The account server does the work (source/App/Controllers/DiscordController.cs).
- Verified members: linking gives the member the Verified role and their game name as their
  nickname here; unlinking (with /unlink, or in the game: the bot asks the account server every
  minute who did) takes both away. /account puts them back for a member who left and came back.
  Members can't rename themselves: setup_server.py takes Change Nickname from @everyone. The bot
  needs Manage Roles and Manage Nicknames, and can't change the server's owner or anyone ranked
  above it.

Settings (KEY=value lines in the --env file, or the environment):
    DISCORD_TOKEN            the bot's token (secret)
    DISCORD_GUILD_ID         the Do Damage server's id
    DISCORD_UPDATES_CHANNEL  #updates' id (release posts); empty: none
    DISCORD_STATUS_CHANNEL   #server-status' id (the live status message); empty: none
    DISCORD_UPDATES_ROLE     the Updates role's id (pinged on releases, and /updates); empty: none
    DISCORD_VERIFIED_ROLE    the Verified role's id (members with a linked game account); empty: none,
                             and nicknames are left alone
    BSK_SITE                 the website (default https://dodmg.com)
    BSK_RELEASES             the published latest.json to watch; empty: no release posts
    DISCORD_STATE            the state file (default discord-state.json beside the --env file)
    DISCORD_POSTED_UP_TO     on the first start (no state file): the newest version announced
                             before the bot, so that a newer one is posted; empty: what's out then
    BSK_ACCOUNT              the account server for linked accounts (default BSK_SITE + /api)
    BSK_LINK_SECRET          what the bot and the account server share (serverSettings.discordSecret
                             there); empty: made from DISCORD_TOKEN, as deploy/deploy.py does (secret)
"""
import argparse
import asyncio
import json
import os
import sys

import dodmg

RELEASE_CHECK = 60
UNLINK_CHECK = 60
STATUS_REFRESH = 120


def settings(path):
    env = dict(os.environ)
    if path:
        env.update(dodmg.load_env(path))
    state = env.get("DISCORD_STATE") or os.path.join(os.path.dirname(os.path.abspath(path or ".")), "discord-state.json")
    num = lambda k: int(env[k]) if env.get(k, "").strip() else None
    return {
        "token": env.get("DISCORD_TOKEN", ""),
        "guild": num("DISCORD_GUILD_ID"),
        "updates": num("DISCORD_UPDATES_CHANNEL"),
        "status": num("DISCORD_STATUS_CHANNEL"),
        "role": num("DISCORD_UPDATES_ROLE"),
        "verified": num("DISCORD_VERIFIED_ROLE"),
        "site": (env.get("BSK_SITE") or dodmg.SITE).rstrip("/"),
        "releases": env.get("BSK_RELEASES", ""),
        "state": state,
        "posted_up_to": env.get("DISCORD_POSTED_UP_TO", "").strip(),
        "account": (env.get("BSK_ACCOUNT") or (env.get("BSK_SITE") or dodmg.SITE).rstrip("/") + "/api").rstrip("/"),
        "link_secret": dodmg.link_secret(env),
    }


class State:
    """The versions posted and the live status message, in a small JSON file (written whole, then
    renamed into place, so a crash never leaves half of it)."""

    def __init__(self, path):
        self.path = path
        self.fresh = not os.path.exists(path)
        self.data = {"posted": [], "status_message": None}
        if not self.fresh:
            with open(path, encoding="utf-8") as f:
                self.data.update(json.load(f))

    def save(self):
        tmp = self.path + ".tmp"
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(self.data, f, indent=1)
        os.replace(tmp, self.path)

    def newest_posted(self):
        return max(self.data["posted"], key=dodmg.version_key, default=None)


def to_post(state, version, posted_up_to):
    """Whether the release out now (version) is to be posted: newer than any posted. On the first
    start, everything up to posted_up_to (or else this version) counts as posted already."""
    if state.fresh:
        state.fresh = False
        state.data["posted"].append(posted_up_to or version)
        state.save()
        print("releases: first start, %s counts as posted" % state.newest_posted(), flush=True)
    newest = state.newest_posted()
    return not newest or dodmg.version_key(version) > dodmg.version_key(newest)


def dry_run(cfg, version):
    """Prints the release post and the status message without connecting to Discord."""
    sys.stdout.reconfigure(encoding="utf-8")
    if version:
        release = next((r for r in dodmg.releases_from_changelog() if r[0] == version), None)
        if release is None:
            sys.exit("no %s in client-rs/CHANGELOG.md" % version)
    elif cfg["releases"]:
        release = dodmg.release_from_latest(cfg["releases"])
    else:
        release = dodmg.releases_from_changelog()[0]
    print("=== release post for %s (to channel %s, ping role %s) ===" % (release[0], cfg["updates"], cfg["role"]))
    for i, msg in enumerate(dodmg.release_messages(release, cfg["site"], cfg["role"]), 1):
        e = msg["embeds"][0]
        print("--- message %d (%d characters) ---" % (i, len(e["description"]) + len(e.get("title", ""))))
        if msg.get("content"):
            print("content:", msg["content"])
        if e.get("title"):
            print("# %s  <%s>" % (e["title"], e.get("url")))
        print(e["description"])
        if e.get("footer"):
            print("(footer: %s)" % e["footer"]["text"])
    try:
        status = dodmg.fetch_status(cfg["site"])
    except Exception as e:
        print("status: unavailable (%s)" % e)
        status = None
    print("=== /status ===")
    print(json.dumps(dodmg.status_embed(status, cfg["site"]), indent=1, ensure_ascii=False))
    print("=== /online ===")
    print(json.dumps(dodmg.online_embed(status, cfg["site"]), indent=1, ensure_ascii=False))
    print("=== /link (no code) ===")
    print(dodmg.LINK_HOW)
    print("=== /link, /account, /resetpassword (made-up answers) ===")
    print(dodmg.link_reply("link", {"Name": "Tester", "Reward": "Potion of Maxy"}, None))
    print(dodmg.link_reply("account", {"Name": "Tester", "Fame": "120", "TotalFame": "4500", "Characters": "3", "Guild": "Testers"}, None))
    print(dodmg.link_reply("resetPassword", {"Name": "Tester", "Email": "tester@example.com", "Password": "(the new password)"}, None))
    print(dodmg.link_reply("link", None, "Bad code"))
    print("=== /wiki oryx ===")
    print(dodmg.wiki_embed("oryx", dodmg.wiki_search(dodmg.wiki_index(cfg["site"]), "oryx"), cfg["site"])["description"])


def post_release(cfg, version, channel):
    """One release, posted straight away with the REST API (testing in a private channel)."""
    release = next((r for r in dodmg.releases_from_changelog() if r[0] == version), None)
    if release is None:
        sys.exit("no %s in client-rs/CHANGELOG.md" % version)
    rest = dodmg.Rest(cfg["token"])
    for msg in dodmg.release_messages(release, cfg["site"], None):
        sent = rest("POST", "/channels/%d/messages" % channel, msg)
        print("posted message", sent["id"])


def try_command(cfg, member, line):
    """One linked-account command as a member would give it, answered here instead of in Discord
    (for an isolated stack: BSK_ACCOUNT and BSK_LINK_SECRET in the --env file)."""
    sys.stdout.reconfigure(encoding="utf-8")
    words = line.split()
    what = {"link": "link", "unlink": "unlink", "account": "account", "resetpassword": "resetPassword"}.get(words[0].lower() if words else "")
    if what is None:
        sys.exit("--try takes: link <code>, account, resetpassword or unlink")
    member_id, _, name = member.partition(":")
    fields = {"code": " ".join(words[1:]), "discordName": name or "tester"} if what == "link" else {}
    answer, error = dodmg.Accounts(cfg["account"], cfg["link_secret"])(what, member_id, **fields)
    print(dodmg.link_reply(what, answer, error))


def run(cfg):
    import discord
    from discord import app_commands

    state = State(cfg["state"])
    accounts = dodmg.Accounts(cfg["account"], cfg["link_secret"])
    intents = discord.Intents.none()
    intents.guilds = True
    client = discord.Client(intents=intents)
    tree = app_commands.CommandTree(client)
    guild = discord.Object(id=cfg["guild"]) if cfg["guild"] else None
    site = cfg["site"]
    wiki = {"index": None}

    async def status_now():
        try:
            return await asyncio.to_thread(dodmg.fetch_status, site)
        except Exception as e:
            print("status: %s" % e, flush=True)
            return None

    @tree.command(name="status", description="Is Do Damage up? Players online, 24-hour peak and uptime.", guild=guild)
    async def status_cmd(inter: discord.Interaction):
        await inter.response.defer()
        await inter.followup.send(embed=discord.Embed.from_dict(dodmg.status_embed(await status_now(), site)))

    @tree.command(name="online", description="How many players are in each world server.", guild=guild)
    async def online_cmd(inter: discord.Interaction):
        await inter.response.defer()
        await inter.followup.send(embed=discord.Embed.from_dict(dodmg.online_embed(await status_now(), site)))

    @tree.command(name="wiki", description="Search the Do Damage wiki.", guild=guild)
    @app_commands.describe(term="What to look for: an item, a dungeon, a class, a command...")
    async def wiki_cmd(inter: discord.Interaction, term: str):
        await inter.response.defer()
        try:
            if wiki["index"] is None:
                wiki["index"] = await asyncio.to_thread(dodmg.wiki_index, site)
            results = dodmg.wiki_search(wiki["index"], term)
        except Exception as e:
            print("wiki: %s" % e, flush=True)
            results = []
        await inter.followup.send(embed=discord.Embed.from_dict(dodmg.wiki_embed(term, results, site)))

    @tree.command(name="play", description="Where to play Do Damage: browser, download, wiki.", guild=guild)
    async def play_cmd(inter: discord.Interaction):
        await inter.response.send_message(embed=discord.Embed.from_dict(dodmg.play_embed(site)))

    @tree.command(name="updates", description="Turn pings for new game updates on or off.", guild=guild)
    async def updates_cmd(inter: discord.Interaction):
        role = inter.guild and cfg["role"] and inter.guild.get_role(cfg["role"])
        if not role or not isinstance(inter.user, discord.Member):
            await inter.response.send_message("Update pings aren't set up here.", ephemeral=True)
            return
        if role in inter.user.roles:
            await inter.user.remove_roles(role, reason="/updates")
            await inter.response.send_message("You won't be pinged for updates any more.", ephemeral=True)
        else:
            await inter.user.add_roles(role, reason="/updates")
            await inter.response.send_message("You'll be pinged in <#%s> when an update is out." % cfg["updates"], ephemeral=True)

    async def verify(member, name):
        """The Verified role and the game name as nickname (name), or neither (None), for a member
        of the server. Returns a key of dodmg.VERIFIED, or None when there's nothing to do."""
        if not cfg["verified"] or not isinstance(member, discord.Member):
            return None
        role = discord.Object(id=cfg["verified"])
        try:
            if name:
                if not any(r.id == role.id for r in member.roles):
                    await member.add_roles(role, reason="linked to the game account %s" % name)
                if member.nick != name:
                    await member.edit(nick=name, reason="linked to the game account %s" % name)
                return "marked"
            if any(r.id == role.id for r in member.roles):
                await member.remove_roles(role, reason="game account unlinked")
                await member.edit(nick=None, reason="game account unlinked")
            return "unmarked"
        except discord.Forbidden:
            print("verified: Discord refused to change member %s" % member.id, flush=True)
            return "refused"
        except discord.HTTPException as e:
            print("verified: member %s: %r" % (member.id, e), flush=True)
            return None

    async def account_call(what, user, **fields):
        answer, error = await asyncio.to_thread(accounts, what, user.id, **fields)
        if error not in (None, "Not linked", "Bad code", "Too soon"):
            print("accounts: %s for %s: %s" % (what, user.id, error), flush=True)
        verified = None
        if what in ("link", "account") and answer:
            # /account too: a member who left the server and came back gets both again
            verified = await verify(user, answer.get("Name"))
            verified = None if what == "account" and verified == "marked" else verified
        elif what == "unlink" and answer or error == "Not linked":
            verified = await verify(user, None)
            verified = verified if what == "unlink" and answer else None
        return dodmg.link_reply(what, answer, error, verified)

    async def unlinked_loop():
        """Members who unlinked in the game lose the Verified role and their nickname here."""
        done = []
        while True:
            try:
                answer, error = await asyncio.to_thread(accounts, "unlinked", 0, done=",".join(done))
                done = []
                server = client.get_guild(cfg["guild"])
                for member_id in filter(None, (answer or {}).get("Ids", "").split(",")) if server else []:
                    try:
                        await verify(await server.fetch_member(int(member_id)), None)
                    except discord.NotFound:
                        pass  # left the server: nothing to take away
                    done.append(member_id)
                if error and error != "unreachable":
                    print("unlinked: %s" % error, flush=True)
            except Exception as e:
                print("unlinked: %r" % e, flush=True)
            await asyncio.sleep(UNLINK_CHECK)

    @tree.command(name="link", description="Link your Discord to your game account (a Potion of Maxy the first time).", guild=guild)
    @app_commands.describe(code="The 6-character code from the game: main menu, Link Discord")
    async def link_cmd(inter: discord.Interaction, code: str = ""):
        if not code.strip():
            await inter.response.send_message(dodmg.LINK_HOW, ephemeral=True)
            return
        await inter.response.defer(ephemeral=True)
        await inter.followup.send(await account_call("link", inter.user, code=code, discordName=inter.user.name), ephemeral=True)

    @tree.command(name="account", description="The game account your Discord is linked to.", guild=guild)
    async def account_cmd(inter: discord.Interaction):
        await inter.response.defer(ephemeral=True)
        await inter.followup.send(await account_call("account", inter.user), ephemeral=True)

    @tree.command(name="unlink", description="Unlink your Discord from your game account.", guild=guild)
    async def unlink_cmd(inter: discord.Interaction):
        await inter.response.defer(ephemeral=True)
        await inter.followup.send(await account_call("unlink", inter.user), ephemeral=True)

    class ConfirmReset(discord.ui.View):
        """Asks before a reset: the old password stops working the moment it's done."""

        def __init__(self):
            super().__init__(timeout=120)

        @discord.ui.button(label="Reset my password", style=discord.ButtonStyle.danger)
        async def confirm(self, inter: discord.Interaction, button: discord.ui.Button):
            await inter.response.defer(ephemeral=True)
            text = await account_call("resetPassword", inter.user)
            await inter.edit_original_response(content=text, view=None)

        @discord.ui.button(label="Cancel", style=discord.ButtonStyle.secondary)
        async def cancel(self, inter: discord.Interaction, button: discord.ui.Button):
            await inter.response.edit_message(content="Nothing was changed.", view=None)

    @tree.command(name="resetpassword", description="Lost your password? Get a new one for your linked game account.", guild=guild)
    async def reset_cmd(inter: discord.Interaction):
        await inter.response.defer(ephemeral=True)
        answer, error = await asyncio.to_thread(accounts, "account", inter.user.id)
        if error:
            await inter.followup.send(dodmg.link_error(error), ephemeral=True)
            return
        await inter.followup.send("Give **%s** a new password? The one it has now stops working, and I'll show you the "
                                  "new one here, where only you can see it." % answer.get("Name", "your account"),
                                  view=ConfirmReset(), ephemeral=True)

    async def already_posted(channel, version):
        title = dodmg.release_title(version)
        async for m in channel.history(limit=30):
            if m.author.id == client.user.id and any(e.title == title for e in m.embeds):
                return True
        return False

    async def check_release():
        release = await asyncio.to_thread(dodmg.release_from_latest, cfg["releases"])
        if release is None or not to_post(state, release[0], cfg["posted_up_to"]):
            return
        version = release[0]
        channel = client.get_channel(cfg["updates"]) or await client.fetch_channel(cfg["updates"])
        if not await already_posted(channel, version):
            for msg in dodmg.release_messages(release, site, cfg["role"]):
                mentions = discord.AllowedMentions(everyone=False, users=False,
                                                   roles=[discord.Object(id=int(r)) for r in msg["allowed_mentions"]["roles"]])
                await channel.send(content=msg.get("content"), embed=discord.Embed.from_dict(msg["embeds"][0]),
                                   allowed_mentions=mentions)
            print("releases: posted %s" % version, flush=True)
        state.data["posted"].append(version)
        state.save()

    async def releases_loop():
        while True:
            try:
                await check_release()
            except Exception as e:
                print("releases: %r" % e, flush=True)
            await asyncio.sleep(RELEASE_CHECK)

    async def status_loop():
        channel = client.get_channel(cfg["status"]) or await client.fetch_channel(cfg["status"])
        while True:
            try:
                embed = discord.Embed.from_dict(dodmg.status_embed(await status_now(), site, live=True))
                msg = None
                if state.data.get("status_message"):
                    try:
                        msg = await channel.fetch_message(state.data["status_message"])
                    except discord.NotFound:
                        msg = None
                if msg is None:
                    # a new state file (another machine, or lost): the bot's own message there
                    async for m in channel.history(limit=20):
                        if m.author.id == client.user.id:
                            msg = m
                            state.data["status_message"] = m.id
                            state.save()
                            break
                if msg:
                    await msg.edit(embed=embed)
                else:
                    msg = await channel.send(embed=embed)
                    state.data["status_message"] = msg.id
                    state.save()
            except Exception as e:
                print("status message: %r" % e, flush=True)
            await asyncio.sleep(STATUS_REFRESH)

    started = {"done": False}

    @client.event
    async def on_ready():
        print("signed in as %s" % client.user, flush=True)
        if started["done"]:
            return  # a reconnect
        started["done"] = True
        await tree.sync(guild=guild)
        if cfg["releases"] and cfg["updates"]:
            asyncio.create_task(releases_loop())
        if cfg["status"]:
            asyncio.create_task(status_loop())
        if cfg["verified"] and cfg["guild"] and cfg["link_secret"]:
            asyncio.create_task(unlinked_loop())

    client.run(cfg["token"], log_handler=None)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--env", help="the settings file (KEY=value lines)")
    ap.add_argument("--dry-run", action="store_true", help="print what would be posted; don't connect to Discord")
    ap.add_argument("--release", help="with --dry-run: this release, from client-rs/CHANGELOG.md")
    ap.add_argument("--post-release", metavar="VERSION", help="post this release to --channel now, and stop")
    ap.add_argument("--channel", type=int, help="the channel for --post-release")
    ap.add_argument("--try", dest="try_", metavar="COMMAND", help='a linked-account command, answered here: "link <code>", '
                    "account, resetpassword or unlink, as --as-member")
    ap.add_argument("--as-member", metavar="ID[:NAME]", help="the Discord member for --try (a made-up id on a test stack)")
    args = ap.parse_args()
    cfg = settings(args.env)
    if args.dry_run:
        dry_run(cfg, args.release)
        return
    if args.try_:
        if not args.as_member:
            sys.exit("--try needs --as-member")
        try_command(cfg, args.as_member, args.try_)
        return
    if not cfg["token"]:
        sys.exit("no DISCORD_TOKEN: give --env with the settings file")
    if args.post_release:
        if not args.channel:
            sys.exit("--post-release needs --channel")
        post_release(cfg, args.post_release, args.channel)
        return
    run(cfg)


if __name__ == "__main__":
    main()
