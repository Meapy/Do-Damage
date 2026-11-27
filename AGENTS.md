# betterSkillys: working notes

A Realm of the Mad God private server (C#, .NET 8) and its clients, for a small group of players.
Players know it as **Do Damage** (the client, the website, anything players read);
`betterSkillys` is the repository's and the code's name. Read the guide for the part you change:

- [source/AGENTS.md](source/AGENTS.md): the servers (account, world, behaviours, worlds, Redis).
- [client-rs/AGENTS.md](client-rs/AGENTS.md): the Rust client players use (desktop and browser)
  and the gateway for browser play.
- [SETUP-LOCAL.md](SETUP-LOCAL.md): running, deploying ("Running on a VPS") and sharing the game,
  and every change made here. Add to its "Changes from upstream" when you change behaviour or add
  content; "Classes, minigames and the economy" says where those live.
- [client/AGENTS.md](client/AGENTS.md): the old AIR client. **Deprecated**: don't build, test or
  change it unless the owner asks; its `src/` still holds the game's art and XML.
- `website/` is dodmg.com (built by `tools/build_site.py`); `deploy/` installs everything on the
  VPSes.

## What runs where

Two Ubuntu 24.04 VPSes with systemd services, installed together by one deploy (SETUP-LOCAL.md,
"Two machines").

**Game, play.dodmg.com** (91.90.80.8, KVM):

| Part | Port | Notes |
|---|---|---|
| Caddy | 80, 443 | browser play at `/play/`, `/api` and `/ws` to the gateway; the rest redirects to dodmg.com |
| Gateway (`bsk-gateway@a`/`b`/`c`) | 8095-8097, local | web client, account requests, the game over WebSocket; one slot takes new players, another may be handing over |
| Account server (`bsk-account`) | 8080 | open for older desktop clients |
| World proxy and servers (`bsk-world`) | 2051 (slots 12051 / 22051, local) | **Europe 2**, the main server; rolling deploys |
| Redis (`bsk-redis`) | 6379, local | data in `/opt/bsk/redis/data`; backups (`bsk-backup.timer`) |

**Website, dodmg.com** (169.58.244.146, VMware; its host pauses, so nothing latency-sensitive):

| Part | Port | Notes |
|---|---|---|
| Caddy | 80, 443, 8080 | the website; `/play` redirects to play.dodmg.com; `/api`, `/ws` and 8080 forwarded there for older clients |
| Discord bot (`bsk-discord`) | none | release posts, server status, Marketplace listings, slash commands, linked accounts (SETUP-LOCAL.md, "Discord") |
| Testing (`bsk-world`, `bsk-redis-tunnel`) | 2051 | admin-only world server on the game's Redis (SSH tunnel), where each pull request's world server goes; everyone else goes to Europe 2 |

Its old Redis, account server and gateways are masked: their data is from 2026-11-07, not live.

**This PC:** the old live servers are stopped since 2026-10-16 (`source/Redis/data` is that day's
data: the test copy, not live). `tools/update_relay.py` may run on 8080, sending desktop clients
built for this PC to dodmg.com. Isolated test servers use 6380 / 8081 / 2052, slot N adding N
(`tools/testclient/iso_stack.py`, under `build/iso*/`). Another server on this PC uses 8888, 2050
and 843: don't take those ports. `world_proxy.py` and `rolling_restart.py` must keep working on
Windows and Linux. `Run Server.bat` starts a copy on the old data, so it asks first; never run test
copies under the live process names (`redis-server.exe`, `App.exe`, `WorldServer.exe`).

## Ground rules

- **Real people play on the live servers.** A deploy, publish or restart is the owner's call: ask
  first, every time, unless they just asked for it. Test on an isolated stack. What disconnects
  whom:
  - a deploy (each merge to `main`: `.github/workflows/deploy.yml` runs `deploy/deploy.py`) kicks
    nobody (SETUP-LOCAL.md, "Deploys that kick nobody");
  - restarting `bsk-world` itself kicks everyone (saved first);
  - restarting a gateway slot (`bsk-gateway@<slot>`) or Caddy drops nearly everyone on it; the
    Rust client takes them back to the Nexus by itself.
- **Stopping a world server** saves everyone on Ctrl+C (Windows) or SIGTERM (Linux).
  `taskkill /F` (what `rolling_restart.py stop` uses on Windows) skips the save; from a script,
  attach to its console and send Ctrl+C (`AttachConsole`, `GenerateConsoleCtrlEvent`).
- **Never drive a player's window** ("Do Damage", or "betterSkillys" for the AIR client). Test
  with the Rust client's scripted flags, in its own window, on an isolated stack. In game, test
  only what the tests and builds can't show, in one run.
- **Write down testing pitfalls.** When a test trips on something not obvious, add it with what
  works instead to the testing notes of the part you tested (client-rs/AGENTS.md, "Testing
  against the servers"; source/AGENTS.md, "Build and test") in the same commit. Fold it into the
  note it belongs with rather than adding a dated line, and delete notes that stop being true.
- **Client and server ship together** when a message changes. Keep old clients working with a new
  server where possible, and say in the commit which side needs redeploying.
- **Client releases** are published by the deploy on merge to `main`, signed with the
  `CLIENT_UPDATE_KEY` secret (checked against `CLIENT_UPDATE_PUBLIC_KEY`). The only other copy is
  the main checkout's `build/client-update.key`. Publishing from a worktree or a fresh clone makes
  a new key and strands every player: never publish anywhere else.
- **Releases post to Discord** (#updates, pinging the Updates role, once per version), so name the
  changelog section as players should read it. Tests never post: publish test releases with
  `--publish-to`, try the bot with `bot.py --dry-run` or in a private test channel deleted after.
- **Secrets** stay in git-ignored files: `*.local.txt`, `deploy/*.local.json`,
  `deploy/discord.local.env`, `build/` (signing key), `~/.ssh/dodmg_ed25519`. GitHub has its own
  copies as secrets (`VPS_SSH_KEY`, `VPS_KNOWN_HOSTS`, `VPS_CONFIG`, `DISCORD_ENV`,
  `CLIENT_UPDATE_KEY`); never print them in a workflow (the logs are public). Never sign in with a
  password pasted into a chat: ask the owner for a key. `build/`, `dist/`, `client/bin/`,
  `tools/airsdk/` and `source/Redis/data/` aren't tracked.
- **Changelog:** every change players can notice gets a line in
  [client-rs/CHANGELOG.md](client-rs/CHANGELOG.md) under `## Unreleased`, in the same commit, in
  plain words for players, in its `### ` group, summed up with what's there rather than a line per
  detail. Publishing renames the section to the version, so hold back lines that aren't live yet.
- **Wiki:** a finished change players can notice also updates `website/wiki/` in the same commit.
  Much is generated from the game's data (`tools/build_wiki.py`, `tools/wiki_*.py`): build it
  (`python tools/build_wiki.py --out <scratch folder>`), read the pages the change touches, fix the
  generator when it misses the change, and edit the hand-written guides (`website/wiki/*.md`).
  Measured tables (Gear statistics) stay as measured; add a note when one goes out of date.
- **Languages:** the Rust client's text goes through `tr` / `tf!` / `tn!` with a translation in
  every `client-rs/app/lang/*.json`, in the same commit (client-rs/AGENTS.md, "Languages").
- **Game content is edited in place.** Items, monsters, worlds and behaviours are in the
  repository's XML, maps and C#; change them directly, keeping the server's and the client's copy
  of an XML in step. A file whose first line says "Generated by tools/<script>" is the exception:
  change that script and rerun it. Content first made for Doomed Realms is the game's own now: in
  docs and player text call it what it is ("the newer classes", "the minigames"), and leave the
  `Doomed` names in code, files and Redis keys as they are.
- **DNS** for dodmg.com is at Register365 with a one-day TTL; check several resolvers (1.1.1.1,
  8.8.8.8, 9.9.9.9) before relying on a change.

## Git

- **`main` gets one squashed commit per released version**, through a pull request; nothing
  else lands on it.
- **Collecting work:** finished work goes into the next version's branch, `release/<version>`,
  named for the version after `main`'s newest "Release x.y.z" commit (`git branch --list
  'release/*'`; older ones may linger after their squash and take nothing). With none, create it
  from `main` and reset its PR-BODY.md to the empty template: the title line, `## For players`,
  `## Details`, `## Checks`.
  - **[PR-BODY.md](PR-BODY.md)** is the release's pull request body, written as work goes in, in
    the same commit as the work. For players: the piece summed up in its group (fuller than the
    changelog). Details: a `### ` part per piece: what and why, files touched (and what was
    regenerated), which side redeploys and whether players are disconnected, Redis data kept or
    migrated, anything the owner must do, what's not done. Checks: how it was tested. Keep both
    sides' parts when rebasing.
  - Sessions work in worktrees under `.claude/worktrees/`, rebase onto the release branch, then
    fast-forward it from the main checkout: `git -C <main checkout> merge --ff-only <branch>`
    when the main checkout has the release branch checked out, else
    `git -C <main checkout> fetch . <branch>:release/<version>` (both refuse anything but a
    fast-forward; `fetch` into the checked-out branch leaves its files stale). A hook stops edits
    into the main checkout from a worktree.
  - **A release squashed mid-session:** `git rebase --onto release/<new> <old release tip>`, then
    redo the PR-BODY.md and CHANGELOG.md parts in the new sections.
  - **Clean up once it's in:** check `git status --short` is clean (`__pycache__` and `TODO.MD`
    don't count) and `git merge-base --is-ancestor <branch> release/<version>`, then
    `git -C <main checkout> worktree remove <worktree>` and `branch -D <branch>`. Don't reach for
    `--force` on a worktree with changes: look at them. On Windows the folder stays until its
    session closes.
- **Releasing** (when the owner asks):
  1. `python tools\community_sprites.py check` on the release branch. While it lists sprites
     approved in the Sprite Editor but not taken in, take them with
     `python tools\community_sprites.py apply --commit` (in a worktree rebased onto the branch;
     `rework` fixes the XML), look at them in a `--shot`, and check again until it passes. One it
     calls an unchanged copy isn't taken: ask the owner to turn it down in the editor's Review tab.
     Do this before naming the changelog section (`apply` writes under `## Unreleased`).
  2. Name the changelog section and raise the version (client-rs/AGENTS.md, "Releasing to
     players"); check the client tests, `python tools\build_site.py` and the server builds.
  3. `git push -u origin release/<version>`, check PR-BODY.md against
     `git log main..release/<version>` (version and deploying sides in its first line), then
     `gh pr create --base main --head release/<version> --body-file PR-BODY.md`.
  4. `gh pr merge --squash --delete-branch` (title naming the version), then in the main checkout
     `switch main`, `pull --ff-only`, `branch -D release/<version>`, and remove the worktrees and
     branches it carried.
  5. The merge deploys (SETUP-LOCAL.md, "Deploying from GitHub"). Watch it:
     `gh run watch --repo Meapy/Do-Damage $(gh run list --repo Meapy/Do-Damage --workflow Deploy --limit 1 --json databaseId --jq '.[0].databaseId')`,
     then check `https://dodmg.com/api/client/windows/latest.json` names the new version. Don't
     also deploy from the PC. If it fails, fix it on the next release branch; only if players need
     it now, run `python deploy\deploy.py` from the main checkout.
- **No merge commits** on `main` or a release branch: rebase instead. Don't rewrite commits
  pushed to `origin` unless the owner asks.
- Another session may be editing the same tree: stage and commit explicit paths only (never
  `git add -A` or `commit -a`), and check `git status` for changes that aren't yours.
- `core.autocrlf` is on: the repository stores LF, many working files are CRLF. Scripts that patch
  text should keep each file's line endings (`sed -i` in Git Bash can turn CRLF into LF).
- `origin` is [Meapy/Do-Damage](https://github.com/Meapy/Do-Damage), standalone. Its history was
  condensed on 2026-10-18; the full history is the local branch `full-history` (never push it) and
  Meapy/betterSkillys. Move a branch cut before then with
  `git rebase --onto main full-history <branch>`. The remotes `aurusenth` and `upstream` aren't in
  `main`'s history: pick up their changes by hand (cherry-pick or copy), never `git merge`.
- Commit messages and authors name the change, not the tool that made it.

## Tools

- Python 3 (`tools/`, `deploy/`), the .NET 8 SDK (servers), Rust stable with
  `wasm32-unknown-unknown` and `wasm-bindgen-cli` (`client-rs/`), and WSL Ubuntu 24.04 with Rust
  (the Linux gateway). AIR SDK 32 in `tools/airsdk/` is only for the deprecated AIR client.
- Git Bash rewrites arguments starting with `/` into Windows paths: prefix `MSYS_NO_PATHCONV=1`
  for slash commands, and give Windows programs `C:/Users/...` paths, not `/c/Users/...`.
- Write long scripts and text full of backticks or quotes to a file and run that, rather than
  passing them through a shell heredoc.
