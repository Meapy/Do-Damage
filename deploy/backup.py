"""Backs up the game's database (Redis) to a private git repository, so it survives the VPS.

On the VPS, bsk-backup.timer runs deploy/remote/backup.sh every 6 hours: a fresh snapshot
(dump.rdb, a few hundred KB) committed and pushed to `backup_repo` from deploy/vps.local.json
with a deploy key that lives on the VPS and can write to that repository only. Keep the
repository private: it holds players' emails and password hashes.

    python deploy/backup.py --setup               # once per VPS: install, make and register the key, back up
    python deploy/backup.py --now                 # back up now
    python deploy/backup.py --status              # the timer and the last runs
    python deploy/backup.py --pull build/db-backup [--at <commit>]   # the latest (or an older) snapshot here

Moving to a new VPS: set it up with deploy.py as usual, then restore the latest snapshot onto it:

    python deploy/backup.py --pull build/db-backup
    python deploy/deploy.py --import-data build/db-backup --force-data
    python deploy/backup.py --setup               # the new VPS's own key, and its backups

--setup needs the GitHub CLI (gh) signed in as an account that can add deploy keys to the
repository. deploy.py keeps the script, the timer and backup_repo up to date afterwards.
"""
import argparse
import io
import os
import re
import subprocess
import sys
import tarfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import deploy  # noqa: E402  (load_config, remote)

B = deploy.REMOTE + "/backup"


def github_name(url):
    m = re.match(r"(?:git@github\.com:|https://github\.com/)([^/]+/[^/]+?)(?:\.git)?/?$", url)
    if not m:
        sys.exit("backup_repo %r isn't a GitHub repository (git@github.com:<owner>/<repo>.git)" % url)
    return m.group(1)


def upload(cfg, repo):
    """The script, the units and backup.env, as a deploy would install them."""
    buf = io.BytesIO()
    with tarfile.open(fileobj=buf, mode="w:gz") as tar:
        def add(name, text):
            data = text.replace("\r\n", "\n").encode()
            info = tarfile.TarInfo(name)
            info.size = len(data)
            info.mode = 0o644
            tar.addfile(info, io.BytesIO(data))
        def read(*p):
            with open(os.path.join(HERE, *p), encoding="utf-8") as f:
                return f.read()
        add("backup.sh", read("remote", "backup.sh"))
        add("bsk-backup.service", read("systemd", "bsk-backup.service"))
        add("bsk-backup.timer", read("systemd", "bsk-backup.timer"))
        add("backup.env", "# Written by deploy/backup.py; deploy.py rewrites it from the config.\nBSK_BACKUP_REPO=%s\n" % repo)
    tmp = os.path.join(deploy.ROOT, "build", "backup-setup.tar.gz")
    os.makedirs(os.path.dirname(tmp), exist_ok=True)
    with open(tmp, "wb") as f:
        f.write(buf.getvalue())
    script = ("set -e; t=$(mktemp -d); tar xzf - -C $t; mkdir -p %(r)s/deploy %(b)s;"
              " install -m 644 $t/backup.sh %(r)s/deploy/backup.sh;"
              " install -m 644 $t/backup.env %(b)s/backup.env;"
              " install -m 644 $t/bsk-backup.service $t/bsk-backup.timer /etc/systemd/system/;"
              " rm -rf $t; chown -R bsk:bsk %(b)s; systemctl daemon-reload"
              % {"r": deploy.REMOTE, "b": B})
    rc = deploy.remote(cfg, script, tmp)
    os.remove(tmp)
    if rc:
        sys.exit("upload failed")


def setup(cfg, repo):
    name = github_name(repo)
    upload(cfg, repo)
    # the key is made on the VPS and never leaves it; only its public half comes back
    out = subprocess.run(
        ["ssh"] + cfg["ssh_options"] + [cfg["ssh"], "sudo -u bsk bash -c '"
         "[ -f %(b)s/deploy_key ] || ssh-keygen -q -t ed25519 -N \"\" -C \"bsk-backup@$(hostname)\" -f %(b)s/deploy_key;"
         " cat %(b)s/deploy_key.pub'" % {"b": B}],
        check=True, capture_output=True, text=True).stdout.strip()
    if not out.startswith("ssh-ed25519 "):
        sys.exit("no public key came back: %r" % out)
    have = subprocess.run(["gh", "api", "repos/%s/keys" % name, "--jq", ".[].key"], capture_output=True, text=True)
    if have.returncode:
        sys.exit("can't read %s's deploy keys (does it exist, and is gh signed in?): %s" % (name, have.stderr.strip()))
    if out.split()[1] in have.stdout:
        print("the VPS's key is already a deploy key of %s" % name)
    else:
        pub = os.path.join(deploy.ROOT, "build", "backup-key.pub")
        with open(pub, "w", encoding="utf-8") as f:
            f.write(out + "\n")
        deploy.run(["gh", "repo", "deploy-key", "add", pub, "-R", name, "--allow-write",
                    "--title", "VPS backups (%s)" % out.split()[-1]])
        os.remove(pub)
    if deploy.remote(cfg, "systemctl start bsk-backup.service && systemctl enable --now bsk-backup.timer;"
                          " journalctl -u bsk-backup.service -n 3 --no-pager -o cat"):
        sys.exit("the first backup failed: see journalctl -u bsk-backup on the VPS")


def pull(repo, folder, at):
    name = github_name(repo)
    if os.path.isdir(os.path.join(folder, ".git")):
        deploy.run(["git", "-C", folder, "fetch", "-q", "origin"])
    else:
        deploy.run(["gh", "repo", "clone", name, folder, "--", "-q"])
    deploy.run(["git", "-C", folder, "-c", "advice.detachedHead=false", "checkout", "-q", at or "origin/main"])
    subprocess.run(["git", "-C", folder, "log", "-1", "--format=snapshot: %s (%h)"])
    print("restore it with: python deploy/deploy.py --import-data %s --force-data" % folder)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--config", default=os.path.join(HERE, "vps.local.json"))
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument("--setup", action="store_true", help="install on the VPS, register its key, back up once")
    g.add_argument("--now", action="store_true", help="back up now")
    g.add_argument("--status", action="store_true", help="the timer and the last runs")
    g.add_argument("--pull", metavar="DIR", help="fetch the backup repository into DIR")
    ap.add_argument("--at", metavar="COMMIT", help="with --pull: an older snapshot")
    args = ap.parse_args()
    if not os.path.exists(args.config):
        sys.exit("no %s: copy deploy/vps.example.json there and fill it in" % args.config)
    cfg = deploy.load_config(args.config)
    repo = cfg.get("backup_repo")
    if not repo:
        sys.exit("%s has no backup_repo (git@github.com:<owner>/<repo>.git, a private repository)" % args.config)
    if args.setup:
        setup(cfg, repo)
    elif args.now:
        sys.exit(deploy.remote(cfg, "systemctl start bsk-backup.service; journalctl -u bsk-backup.service -n 3 --no-pager -o cat"))
    elif args.status:
        sys.exit(deploy.remote(cfg, "systemctl list-timers bsk-backup.timer --no-pager;"
                                    " journalctl -u bsk-backup.service -n 12 --no-pager -o short-iso"))
    else:
        pull(repo, args.pull, args.at)


if __name__ == "__main__":
    main()
