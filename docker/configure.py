"""Writes a container's settings from its environment into the server's json, at each start
(docker/Dockerfile), as deploy/deploy.py writes them into the VPS bundle:

    python3 configure.py account /opt/bsk/app/server.json
    python3 configure.py world /opt/bsk/worldserver/wServer.json

BSK_REDIS_HOST, BSK_REDIS_PORT and BSK_REDIS_AUTH are the database; BSK_ACCOUNT_PORT the account
server's port; BSK_WORLD_PORT and BSK_PUBLIC_ADDRESS the world port and the address the server
list gives players (rolling_restart.py also reads those two, and the ports of its slots follow).
"""
import json
import os
import sys


def main():
    kind, path = sys.argv[1], sys.argv[2]
    env = os.environ
    with open(path, encoding="utf-8-sig") as f:
        d = json.load(f)
    db = d["dbInfo"]
    db["host"] = env.get("BSK_REDIS_HOST", db["host"])
    db["port"] = int(env.get("BSK_REDIS_PORT", db["port"]))
    db["auth"] = env.get("BSK_REDIS_AUTH", db.get("auth", ""))
    if kind == "account":
        d["serverInfo"]["port"] = int(env.get("BSK_ACCOUNT_PORT", d["serverInfo"]["port"]))
    elif kind == "world":
        d["serverInfo"]["port"] = int(env.get("BSK_WORLD_PORT", d["serverInfo"]["port"]))
        d["serverInfo"]["address"] = env.get("BSK_PUBLIC_ADDRESS", d["serverInfo"]["address"])
    else:
        sys.exit("configure.py: account or world, not %s" % kind)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(d, f, indent=2)
    print("%s: redis %s:%d, port %d" % (os.path.basename(path), db["host"], db["port"], d["serverInfo"]["port"]))


if __name__ == "__main__":
    main()
