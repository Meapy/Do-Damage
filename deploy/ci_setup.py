"""Writes what deploy/deploy.py needs from the GitHub Actions secrets (.github/workflows/deploy.yml),
on a fresh runner. It never prints a secret, and every file it writes is git-ignored or outside the
checkout.

    python deploy/ci_setup.py            # ssh key, known hosts, deploy/vps.local.json
    python deploy/ci_setup.py --signing  # also build/client-update.key (the Windows publish job)

From the environment (SETUP-LOCAL.md, "Deploying from GitHub"):
    VPS_SSH_KEY          the private key that signs in to the VPS (OpenSSH format)
    VPS_KNOWN_HOSTS      the VPS's line(s) from known_hosts: ssh refuses any other host key
    VPS_CONFIG           deploy/vps.local.json's contents; its ssh_options lose any -i (the key
                         above is used) and keep the rest, such as -p
    DISCORD_ENV          optional: deploy/discord.local.env's contents. Without it the bot keeps
                         the settings it has on the VPS (install.sh)
    CLIENT_UPDATE_KEY    with --signing: build/client-update.key's contents (the hex line)
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)


def need(name):
    value = os.environ.get(name, "").strip()
    if not value:
        sys.exit("the %s secret is missing or empty (SETUP-LOCAL.md, \"Deploying from GitHub\")" % name)
    return value


def write_private(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as f:
        f.write(text.replace("\r\n", "\n").strip() + "\n")
    if os.name != "nt":
        os.chmod(path, 0o600)


def main():
    signing = "--signing" in sys.argv[1:]
    ssh_dir = os.path.join(os.path.expanduser("~"), ".ssh")
    os.makedirs(ssh_dir, exist_ok=True)
    if os.name != "nt":
        os.chmod(ssh_dir, 0o700)
    key = os.path.join(ssh_dir, "dodmg_deploy")
    known = os.path.join(ssh_dir, "dodmg_known_hosts")
    write_private(key, need("VPS_SSH_KEY"))
    write_private(known, need("VPS_KNOWN_HOSTS"))

    try:
        cfg = json.loads(need("VPS_CONFIG"))
    except ValueError as e:
        sys.exit("the VPS_CONFIG secret isn't JSON (%s at line %d)" % (e.__class__.__name__, getattr(e, "lineno", 0)))
    kept, skip = [], False
    for opt in cfg.get("ssh_options", []):
        if skip:
            skip = False
        elif opt == "-i":
            skip = True
        elif not opt.startswith("-i"):
            kept.append(opt)
    # forward slashes: Windows' own ssh and Git's both take them
    cfg["ssh_options"] = ["-i", key.replace("\\", "/"), "-o", "IdentitiesOnly=yes", "-o", "BatchMode=yes",
                          "-o", "StrictHostKeyChecking=yes", "-o", "UserKnownHostsFile=" + known.replace("\\", "/"),
                          "-o", "ServerAliveInterval=30"] + kept
    if cfg.get("target", "ssh") != "ssh":
        sys.exit("VPS_CONFIG: target must be ssh on GitHub Actions")
    write_private(os.path.join(HERE, "vps.local.json"), json.dumps(cfg, indent=2))

    discord = os.environ.get("DISCORD_ENV", "").strip()
    if discord:
        write_private(os.path.join(HERE, "discord.local.env"), discord)

    if signing:
        write_private(os.path.join(ROOT, "build", "client-update.key"), need("CLIENT_UPDATE_KEY"))
    print("set up the deploy to %s%s%s" % (cfg.get("ssh", "?").split("@")[-1],
                                           ", with the Discord bot's settings" if discord else "",
                                           ", with the client signing key" if signing else ""))


if __name__ == "__main__":
    main()
