"""Builds the betterSkillys servers and the browser client on this PC and installs them on a Linux
server (a VPS with Ubuntu 24.04) over ssh. Nothing is built on the server.

    python deploy/deploy.py                          # build, upload, install (deploy/vps.local.json)
    python deploy/deploy.py --import-data source/Redis/data   # first time: also copy the Redis data
    python deploy/deploy.py --skip-build             # upload the last build again
    python deploy/deploy.py --status                 # the services and world servers on the server
    python deploy/deploy.py --no-publish             # deploy without publishing the desktop client
    python deploy/deploy.py --dry-run --no-publish   # build and pack only: nothing reaches the server
    python deploy/deploy.py --client-only            # publish the client, upload it and the website only

GitHub Actions (.github/workflows/deploy.yml) deploys each merge to main from a fresh checkout,
which has no dist/: --fetch-from-server first copies the server's published client releases
(/opt/bsk/dist/client) into dist/client and its website downloads into dist/, so the client check
below and the website's download links work as they do on the main checkout. --publish-only
publishes the desktop client when it changed and stops there (the workflow does that on Windows,
then deploys from Linux with what it published). When the workflow has no signing key it deploys
everything but the client; --client-only, from the main checkout, then publishes the client and
installs only the releases and the website (deploy/remote/install-client.sh), leaving the servers
as the workflow put them. Upload and install hold a lock on the server (/opt/bsk/deploy.lock),
so deploys from here and from the workflow never overlap.

Before building, it publishes the desktop client (tools/build_client_rs.py --package --publish,
with --account https://<site>/api, or http://<public_address>:<account port> without a site) when the
client changed since the newest release in dist/client: anything under client-rs/ or client/src/
(its assets) since that release's commit. That needs the main checkout on main with those folders
committed (the release is signed with build/client-update.key, which only the main checkout has),
and a version in client-rs/Cargo.toml above the release's, with its changelog section named.
Otherwise it stops before building anything; --no-publish deploys the client releases as they are.

The settings are in deploy/vps.local.json (copy deploy/vps.example.json; --config picks another):

    target          "ssh" (a VPS) or "wsl" (a WSL distro on this PC, for testing: "wsl": its name)
    ssh             user@host; the user needs sudo without a password (OVH's "ubuntu" has it)
    ssh_options     extra ssh arguments, such as ["-i", "C:/Users/me/.ssh/vps", "-p", "22"]
    site            Caddy's site address: the domain players open in a browser (https), or ""
                    for no Caddy
    site_name       the game's name on the website (default Do Damage)
    site_redirects  other names that redirect to the site, such as ["www.example.com"]
    public_address  the host players' clients connect to: the world server address in the server
                    list, and the account server is http://<public_address>:<account port>
    ports           redis, account, world, gateway (the gateway only listens on 127.0.0.1; its
                    three slots take that port and the next two)
    forward_address true: the gateway tells the world servers each player's address (players in
                    a browser, and desktop clients on https). Needs the world proxy from
                    2026-10-16 or later running (bsk-world restarted since): an older one sends
                    those connections to the active world server whatever their Hello asks for
    build_wsl       on Windows: the WSL distro (with Rust) that builds the Linux gateway
    backup_repo     the private git repository the database is backed up to every 6 hours
                    (git@github.com:<owner>/<repo>.git); see deploy/backup.py

What it builds (in build/vps/bundle), then unpacks into /opt/bsk/incoming and installs with
deploy/remote/install.sh as root:
- app/          dotnet publish of source/App for linux-x64 (needs the .NET runtime, which
                install.sh puts on the server), with server.json set to the ports
- worldserver/  the same for source/WorldServer, with wServer.json set to the ports and address
- web/          tools/build_client_rs.py --web
- site/         tools/build_site.py: the website at the site's root (the browser client is /play/)
- bin/          bsk-gateway, built for Linux (in WSL when run on Windows); install.sh copies it and
                web/ into the gateway slot it starts
- tools/        world_proxy.py, rolling_restart.py and the Discord bot (tools/discord); deploy/: the
                units, redis.conf, install.sh
- discord.env   the Discord bot's settings, from deploy/discord.local.env (when there is one)
- client/       dist/client, the published desktop client releases (unless --no-releases)
- redis-data/   with --import-data: dump.rdb and appendonly.aof from that folder
"""
import argparse
import importlib.util
import io
import json
import os
import shutil
import subprocess
import sys
import tarfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT = os.path.join(ROOT, "build", "vps")
STAGE = os.path.join(OUT, "bundle")
BUNDLE = os.path.join(OUT, "bundle.tar.gz")
REMOTE = "/opt/bsk"
WINDOWS = os.name == "nt"


def run(cmd, **kw):
    print(">", " ".join(cmd), flush=True)
    subprocess.run(cmd, check=True, **kw)


def load_config(path):
    with open(path, encoding="utf-8") as f:
        cfg = json.load(f)
    cfg.setdefault("target", "ssh")
    cfg.setdefault("ssh_options", [])
    cfg.setdefault("site", "")
    ports = {"redis": 6379, "account": 8080, "world": 2051, "gateway": 8095}
    ports.update(cfg.get("ports", {}))
    cfg["ports"] = ports
    if not cfg.get("public_address"):
        sys.exit("%s: public_address is missing" % path)
    if cfg["target"] == "ssh" and not cfg.get("ssh"):
        sys.exit("%s: ssh (user@host) is missing" % path)
    if cfg["target"] == "wsl" and not cfg.get("wsl"):
        sys.exit("%s: wsl (the distro's name) is missing" % path)
    return cfg


def set_json(path, fn):
    with open(path, encoding="utf-8-sig") as f:
        data = json.load(f)
    fn(data)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=2)


def write_lf(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text.replace("\r\n", "\n"))


def copy_lf(src, dst):
    with open(src, encoding="utf-8") as f:
        write_lf(dst, f.read())


def wsl_path(distro, path):
    out = subprocess.run(["wsl", "-d", distro, "--", "wslpath", "-a", path.replace("\\", "/")],
                         check=True, capture_output=True, text=True)
    return out.stdout.strip()


def build_gateway(cfg):
    dest = os.path.join(STAGE, "bin")
    os.makedirs(dest, exist_ok=True)
    manifest = os.path.join(ROOT, "client-rs", "Cargo.toml")
    if not WINDOWS:
        run(["cargo", "build", "--release", "-p", "bsk-gateway", "--manifest-path", manifest])
        shutil.copy2(os.path.join(ROOT, "client-rs", "target", "release", "bsk-gateway"), dest)
        return
    distro = cfg.get("build_wsl") or "Ubuntu-24.04"
    # the target folder stays in WSL's own disk: building on /mnt/c is slow
    script = ('set -e; . "$HOME/.cargo/env" 2>/dev/null || true; '
              'cargo build --release -p bsk-gateway --manifest-path "%s" --target-dir "$HOME/.cache/bsk-gateway"; '
              'cp "$HOME/.cache/bsk-gateway/release/bsk-gateway" "%s/"'
              % (wsl_path(distro, manifest), wsl_path(distro, dest)))
    run(["wsl", "-d", distro, "--", "bash", "-c", script])


def git(*args):
    r = subprocess.run(["git"] + list(args), cwd=ROOT, capture_output=True, text=True)
    return r.returncode, r.stdout.strip()


def version_key(v):
    return tuple(int(p) for p in v.split("."))


def client_version():
    """The client's version as tools/build_client_rs.py publishes it."""
    spec = importlib.util.spec_from_file_location("build_client_rs", os.path.join(ROOT, "tools", "build_client_rs.py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod.version()


def player_account(cfg):
    """The account server players' desktop clients use: the site's gateway (https), else the
    account server itself."""
    if cfg["site"]:
        return "https://%s/api" % cfg["site"]
    return "http://%s:%d" % (cfg["public_address"], cfg["ports"]["account"])


CLIENT_PATHS = ["client-rs", "client/src"]


def client_change():
    """(published version, its commit, why) when the client changed since the newest release in
    dist/client, else None."""
    latest = os.path.join(ROOT, "dist", "client", "windows", "latest.json")
    published, published_commit = None, ""
    if os.path.exists(latest):
        with open(latest, encoding="utf-8") as f:
            data = json.load(f)
        published, published_commit = data["version"], data.get("commit", "")
    if published:
        known = published_commit and git("cat-file", "-e", published_commit + "^{commit}")[0] == 0
        if known and git("diff", "--quiet", published_commit, "HEAD", "--", *CLIENT_PATHS)[0] == 0:
            print("client: unchanged since %s (%s), not publishing" % (published, published_commit))
            return None
    why = "client: changed since %s (%s)" % (published, published_commit) if published else "client: no release yet"
    return published, published_commit, why


def publish_client(cfg):
    """Publishes the desktop client when it changed since the newest release (see the top).
    Returns whether it published."""
    change = client_change()
    if not change:
        return False
    published, published_commit, why = change
    current = client_version()
    stop = " Fix it, or deploy with --no-publish to leave the client releases as they are."
    if not os.path.exists(os.path.join(ROOT, "build", "client-update.key")):
        sys.exit(why + ", but this checkout has no build/client-update.key: publish from the main checkout"
                 " (a new key would strand every player)." + stop)
    if git("branch", "--show-current")[1] != "main":
        sys.exit(why + ", but this checkout isn't on main: releases are published from main, after the"
                 " release's pull request is merged." + stop)
    if git("status", "--porcelain", "--", *CLIENT_PATHS)[1]:
        sys.exit(why + ", but client-rs/ or client/src/ has uncommitted changes." + stop)
    if published and version_key(current) <= version_key(published):
        sys.exit(why + ", but client-rs/Cargo.toml still says %s: raise the version and name its"
                 " changelog section." % current + stop)
    expected = os.environ.get("BSK_UPDATE_PUBLIC_KEY", "").strip().lower()
    if expected:
        # CI: the key came from a secret; a wrong one would sign a release no client takes
        out = subprocess.run(["cargo", "run", "-q", "--release", "-p", "bsk-publish", "--", "key", "--key",
                              os.path.join(ROOT, "build", "client-update.key")],
                             cwd=os.path.join(ROOT, "client-rs"), check=True, capture_output=True, text=True)
        if out.stdout.strip().lower() != expected:
            sys.exit(why + ", but build/client-update.key isn't the key players' clients trust"
                     " (its public key isn't BSK_UPDATE_PUBLIC_KEY)." + stop)
    print(why + ": publishing %s" % current, flush=True)
    # --package too: the website's download (dist/Do-Damage-<os>.zip) is the same release
    run([sys.executable, os.path.join(ROOT, "tools", "build_client_rs.py"),
         "--account", player_account(cfg), "--package", "--publish"])
    return True


def fetch_from_server(cfg):
    """Copies the server's published client releases into dist/client and its website downloads
    (Do-Damage-<os>.zip) into dist/, for a checkout that has none (GitHub Actions). Never replaces
    what's already in dist/."""
    dist = os.path.join(ROOT, "dist")
    if os.path.exists(os.path.join(dist, "client")):
        sys.exit("--fetch-from-server: dist/client is already here; it's for a checkout without one")
    os.makedirs(OUT, exist_ok=True)
    got = os.path.join(OUT, "from-server.tar.gz")
    with open(got, "wb") as f:
        code = remote(cfg, "set -e; cd %s; set --; for p in dist/client site/downloads; do [ -d $p ] && set -- \"$@\" $p; done;"
                           " tar czf - -T /dev/null \"$@\"" % REMOTE, stdout=f)
    if code:
        sys.exit("fetching the releases from the server failed")
    n = 0
    with tarfile.open(got) as tar:
        for m in tar.getmembers():
            if not (m.isfile() or m.isdir()) or m.name.startswith("/") or ".." in m.name.split("/"):
                continue
            if m.name.startswith("dist/client"):
                dest = os.path.join(ROOT, *m.name.split("/"))
            elif m.isfile() and m.name.startswith("site/downloads/") and m.name.endswith(".zip"):
                dest = os.path.join(dist, os.path.basename(m.name))
            else:
                continue
            if m.isdir():
                os.makedirs(dest, exist_ok=True)
                continue
            os.makedirs(os.path.dirname(dest), exist_ok=True)
            with tar.extractfile(m) as src, open(dest, "wb") as out:
                shutil.copyfileobj(src, out)
            n += 1
    os.remove(got)
    latest = os.path.join(dist, "client", "windows", "latest.json")
    if os.path.exists(latest):
        with open(latest, encoding="utf-8") as f:
            print("fetched %d files from the server; its newest client release is %s" % (n, json.load(f)["version"]))
    else:
        print("fetched %d files from the server; it has no client release yet" % n)


def build(cfg, args):
    if os.path.exists(STAGE):
        shutil.rmtree(STAGE)
    os.makedirs(STAGE)
    ports = cfg["ports"]
    source = os.path.join(ROOT, "source")

    # Debug, as Run Server.bat builds: Optimize is on for WorldServer and Shared in Debug too
    for project, folder in (("App", "app"), ("WorldServer", "worldserver")):
        run(["dotnet", "publish", os.path.join(source, project, project + ".csproj"), "-c", "Debug",
             "-r", "linux-x64", "--self-contained", "false", "-o", os.path.join(STAGE, folder),
             "-v", "q", "--nologo"])

    def account(d):
        d["dbInfo"]["port"] = ports["redis"]
        d["serverInfo"]["port"] = ports["account"]
    shutil.copyfile(os.path.join(source, "App", "server.json"), os.path.join(STAGE, "app", "server.json"))
    set_json(os.path.join(STAGE, "app", "server.json"), account)

    def world(d):
        d["dbInfo"]["port"] = ports["redis"]
        d["serverInfo"]["port"] = ports["world"]
        d["serverInfo"]["address"] = cfg["public_address"]
    shutil.copyfile(os.path.join(source, "WorldServer", "wServer.json"),
                    os.path.join(STAGE, "worldserver", "wServer.json"))
    set_json(os.path.join(STAGE, "worldserver", "wServer.json"), world)

    run([sys.executable, os.path.join(ROOT, "tools", "build_client_rs.py"), "--web"])
    shutil.copytree(os.path.join(ROOT, "build", "client-rs", "web"), os.path.join(STAGE, "web"))
    build_gateway(cfg)
    run([sys.executable, os.path.join(ROOT, "tools", "build_site.py"),
         "--name", cfg.get("site_name") or "Do Damage", "--out", os.path.join(STAGE, "site")])

    for name in ("world_proxy.py", "rolling_restart.py"):
        copy_lf(os.path.join(ROOT, "tools", name), os.path.join(STAGE, "tools", name))
    for name in ("bot.py", "dodmg.py", "requirements.txt"):
        copy_lf(os.path.join(ROOT, "tools", "discord", name), os.path.join(STAGE, "tools", "discord", name))
    for name in os.listdir(os.path.join(HERE, "remote")):
        copy_lf(os.path.join(HERE, "remote", name), os.path.join(STAGE, "deploy", name))
    for name in os.listdir(os.path.join(HERE, "systemd")):
        copy_lf(os.path.join(HERE, "systemd", name), os.path.join(STAGE, "deploy", "systemd", name))

    write_lf(os.path.join(STAGE, "bsk.env"),
             "# Written by deploy/deploy.py from %s: change it there.\n"
             "BSK_PUBLIC_ADDRESS=%s\nBSK_REDIS_PORT=%d\nBSK_ACCOUNT_PORT=%d\nBSK_WORLD_PORT=%d\nBSK_GATEWAY_PORT=%d\n"
             "BSK_GATEWAY_FLAGS=%s\n"
             % (os.path.basename(args.config), cfg["public_address"], ports["redis"], ports["account"],
                ports["world"], ports["gateway"], "--forward-address" if cfg.get("forward_address") else ""))
    # kept out of bsk.env, whose every change means a new gateway slot and an account restart
    write_lf(os.path.join(STAGE, "backup.env"),
             "# Written by deploy/deploy.py from %s: change it there.\nBSK_BACKUP_REPO=%s\n"
             % (os.path.basename(args.config), cfg.get("backup_repo", "")))
    discord_env(cfg)
    if cfg["site"]:
        # Caddy gets the certificate for the site by itself (ports 80 and 443 must be open);
        # WebSockets pass through reverse_proxy as they are. The website is the site's root. The
        # browser client is the gateway's page at /play/: it asks for play/api (beside its page)
        # and /ws (from the root), which both reach the gateway as /api and /ws. The website's
        # server status asks /api too.
        #
        # The gateway runs in three slots (bsk-gateway@a/b/c); a deploy starts the new one and the
        # old one hands over, its /healthz turning 503. Caddy checks each slot's every second and
        # sends requests to the first healthy one, retrying another while none answers, so a
        # gateway swap needs no reload. stream_close_delay keeps open WebSockets (the game)
        # through a reload, which would otherwise close them all (Caddy 2.7 or later). The slots
        # that aren't running fail their checks every second: those lines stay out of the log.
        gateways = " ".join("127.0.0.1:%d" % (ports["gateway"] + i) for i in range(3))
        write_lf(os.path.join(STAGE, "Caddyfile"),
                 "# Written by deploy/deploy.py: the website, and the browser client over https (wss for the game).\n"
                 "{\n"
                 "\tlog default {\n\t\texclude http.handlers.reverse_proxy.health_checker.active\n\t}\n"
                 "}\n"
                 "%(site)s {\n"
                 "\tencode zstd gzip\n"
                 "\tredir /play /play/\n"
                 "\t@gateway path /play/* /api/* /ws/*\n"
                 "\thandle @gateway {\n"
                 "\t\turi strip_prefix /play\n"
                 "\t\treverse_proxy %(gateways)s {\n"
                 "\t\t\tlb_policy first\n"
                 "\t\t\tlb_try_duration 10s\n"
                 "\t\t\tlb_try_interval 250ms\n"
                 "\t\t\tfail_duration 5s\n"
                 "\t\t\thealth_uri /healthz\n"
                 "\t\t\thealth_interval 1s\n"
                 "\t\t\thealth_timeout 2s\n"
                 "\t\t\tstream_close_delay 3h\n"
                 "\t\t}\n"
                 "\t}\n"
                 "\thandle {\n\t\troot * %(root)s/site\n\t\tfile_server\n\t}\n"
                 "}\n" % {"site": cfg["site"], "gateways": gateways, "root": REMOTE}
                 # other names for the site (www.) send visitors to it, each with its own certificate
                 + "".join("%s {\n\tredir https://%s{uri} permanent\n}\n" % (alias, cfg["site"])
                           for alias in cfg.get("site_redirects", [])))

    releases = os.path.join(ROOT, "dist", "client")
    if not args.no_releases and os.path.isdir(releases):
        shutil.copytree(releases, os.path.join(STAGE, "client"))


def discord_env(cfg):
    """The Discord bot's settings (SETUP-LOCAL.md, "Discord"): deploy/discord.local.env, with where
    the releases and the site are on the server. Without that file there's no discord.env in the
    bundle, and install.sh leaves the bot as it is."""
    local = os.path.join(HERE, "discord.local.env")
    if not os.path.exists(local):
        print("discord: no deploy/discord.local.env, leaving the bot as it is")
        return
    with open(local, encoding="utf-8-sig") as f:
        lines = [l.rstrip() for l in f if l.split("=", 1)[0].strip() not in ("BSK_RELEASES", "BSK_SITE", "DISCORD_STATE")]
    lines += ["BSK_RELEASES=%s/dist/client/windows/latest.json" % REMOTE,
              "BSK_SITE=%s" % ("https://" + cfg["site"] if cfg["site"] else ""),
              "DISCORD_STATE=%s/discord/state.json" % REMOTE]
    write_lf(os.path.join(STAGE, "discord.env"), "\n".join(lines) + "\n")


def add_data(folder):
    dest = os.path.join(STAGE, "redis-data")
    if os.path.exists(dest):
        shutil.rmtree(dest)
    os.makedirs(dest)
    found = False
    for name in ("dump.rdb", "appendonly.aof", "appendonlydir"):
        src = os.path.join(folder, name)
        if os.path.isdir(src):
            shutil.copytree(src, os.path.join(dest, name))
            found = True
        elif os.path.isfile(src):
            shutil.copy2(src, dest)
            found = True
    if not found:
        sys.exit("no dump.rdb or appendonly.aof in %s" % folder)


def pack():
    def norm(info):
        info.uid = info.gid = 0
        info.uname = info.gname = "root"
        if info.isdir():
            info.mode = 0o755
        else:
            executable = info.name.startswith("bin/") or info.name.endswith(".sh")
            info.mode = 0o755 if executable else 0o600 if info.name == "discord.env" else 0o644
        return info
    with tarfile.open(BUNDLE, "w:gz", compresslevel=6) as tar:
        for name in sorted(os.listdir(STAGE)):
            tar.add(os.path.join(STAGE, name), arcname=name, filter=norm)
    print("bundle: %s (%.1f MB)" % (os.path.relpath(BUNDLE, ROOT), os.path.getsize(BUNDLE) / 1e6))


def remote(cfg, script, stdin=None, stdout=None):
    """Runs a bash script as root on the server."""
    if cfg["target"] == "wsl":
        # -e: run bash directly (with --, wsl hands the line to a shell first, which expands $)
        cmd = ["wsl", "-d", cfg["wsl"], "-u", "root", "-e", "bash", "-c", script]
    else:
        quoted = "'" + script.replace("'", "'\\''") + "'"
        root = cfg["ssh"].split("@")[0] == "root"
        cmd = ["ssh"] + cfg["ssh_options"] + [cfg["ssh"], ("bash -c " if root else "sudo bash -c ") + quoted]
    print(">", script if len(script) < 200 else script[:200] + "...", flush=True)
    if stdin is None:
        return subprocess.run(cmd, stdout=stdout).returncode
    with open(stdin, "rb") as f:
        return subprocess.run(cmd, stdin=f, stdout=stdout).returncode


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--config", default=os.path.join(HERE, "vps.local.json"))
    ap.add_argument("--skip-build", action="store_true", help="upload build/vps/bundle as it is")
    ap.add_argument("--no-releases", action="store_true", help="leave out dist/client (desktop client releases)")
    ap.add_argument("--no-publish", action="store_true", help="don't publish the desktop client, even if it changed")
    ap.add_argument("--import-data", metavar="DIR", help="also copy this Redis data folder (first install)")
    ap.add_argument("--force-data", action="store_true", help="with --import-data: replace data already there")
    ap.add_argument("--status", action="store_true", help="only show the services' state")
    ap.add_argument("--fetch-from-server", action="store_true",
                    help="first copy the server's client releases and website downloads into dist/ (CI)")
    ap.add_argument("--publish-only", action="store_true",
                    help="publish the desktop client if it changed, then stop (no build, no upload)")
    ap.add_argument("--client-only", action="store_true",
                    help="publish the desktop client if it changed and upload only it and the website")
    ap.add_argument("--force-client", action="store_true",
                    help="with --client-only: upload the releases and the website even if nothing was published")
    ap.add_argument("--dry-run", action="store_true", help="build and pack, but don't touch the server")
    args = ap.parse_args()
    if not os.path.exists(args.config):
        sys.exit("no %s: copy deploy/vps.example.json there and fill it in" % args.config)
    cfg = load_config(args.config)

    if args.status:
        sys.exit(remote(cfg, "systemctl --no-pager --lines=0 status bsk-redis bsk-account bsk-world 'bsk-gateway@*' caddy bsk-discord"
                             " | grep -E '[.]service - |^ *Active'; runuser -u bsk -- python3 %(r)s/tools/rolling_restart.py status;"
                             " . %(r)s/bsk.env; echo \"active gateway slot: $(cat %(r)s/gateway/active 2>/dev/null)\";"
                             " p=$BSK_GATEWAY_PORT; for s in a b c; do"
                             " h=$(curl -s --max-time 2 http://127.0.0.1:$p/healthz) && echo \"gateway slot $s (port $p): $h\"; p=$((p + 1)); done"
                        % {"r": REMOTE}))
    if args.fetch_from_server:
        fetch_from_server(cfg)
    if args.publish_only or args.client_only:
        published = publish_client(cfg)
        if os.environ.get("GITHUB_OUTPUT"):
            with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as f:
                f.write("published=%s\n" % ("true" if published else "false"))
        if args.publish_only:
            return
        if not published and not args.force_client:
            print("nothing to upload (--force-client uploads the releases and the website anyway)")
            return
        build_client_bundle(cfg)
        pack()
        if not args.dry_run:
            upload_and_install(cfg, "install-client.sh")
        return
    if not args.skip_build:
        if not (args.no_publish or args.no_releases):
            publish_client(cfg)
        else:
            change = os.path.isdir(os.path.join(ROOT, "dist", "client")) and client_change()
            if change:
                note = change[2] + ", but it isn't being published: players keep that release until it is"
                print(("::warning::" if os.environ.get("GITHUB_ACTIONS") else "") + note, flush=True)
        build(cfg, args)
    elif not os.path.isdir(STAGE):
        sys.exit("nothing built yet: run without --skip-build")
    if args.import_data:
        add_data(args.import_data)
    elif os.path.exists(os.path.join(STAGE, "redis-data")):
        shutil.rmtree(os.path.join(STAGE, "redis-data"))
    pack()
    if args.dry_run:
        print("dry run: nothing sent to the server")
        return

    flags = (" --import-data" if args.import_data else "") + (" --force-data" if args.force_data else "")
    upload_and_install(cfg, "install.sh" + flags)


def upload_and_install(cfg, install):
    """Unpacks the bundle into /opt/bsk/incoming and runs deploy/<install> from it. One deploy at a
    time on the server, whoever runs it (a PC or GitHub Actions): the upload and the install hold
    a lock together, so a second deploy waits for the first to finish."""
    incoming = REMOTE + "/incoming"
    script = ("set -e; mkdir -p %(r)s; exec 9>%(r)s/deploy.lock;"
              " flock -w 1800 9 || { echo 'another deploy is still running' >&2; exit 1; };"
              " rm -rf %(i)s; mkdir -p %(i)s; tar xzf - -C %(i)s; bash %(i)s/deploy/%(s)s </dev/null"
              % {"r": REMOTE, "i": incoming, "s": install})
    code = remote(cfg, script, BUNDLE)
    if code:
        sys.exit("the deploy failed (%d): see the output above" % code)


def build_client_bundle(cfg):
    """--client-only: the published client releases and the website (whose download is the new
    release), for deploy/remote/install-client.sh. The servers and the gateway stay as they are."""
    if os.path.exists(STAGE):
        shutil.rmtree(STAGE)
    os.makedirs(STAGE)
    shutil.copytree(os.path.join(ROOT, "dist", "client"), os.path.join(STAGE, "client"))
    run([sys.executable, os.path.join(ROOT, "tools", "build_site.py"),
         "--name", cfg.get("site_name") or "Do Damage", "--out", os.path.join(STAGE, "site")])
    copy_lf(os.path.join(HERE, "remote", "install-client.sh"), os.path.join(STAGE, "deploy", "install-client.sh"))


if __name__ == "__main__":
    main()
