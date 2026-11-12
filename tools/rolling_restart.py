"""Deploys a new world server build without disconnecting anyone, behind the world proxy.

    python tools/rolling_restart.py up              # proxy + a world server, if they aren't running
    python tools/rolling_restart.py update          # up if nothing runs, else deploy (Run Server.bat)
    python tools/rolling_restart.py deploy          # build the current source and switch to it
    python tools/rolling_restart.py deploy --reuse  # same build again (the scheduled restart)
    python tools/rolling_restart.py status
    python tools/rolling_restart.py stop            # stops the proxy and the world servers: kicks everyone
    python tools/rolling_restart.py serve           # Linux service: a world server, then become the proxy
    python tools/rolling_restart.py request update  # ask the running proxy to run `update` (Linux service)

    --iso N (before the command) works on isolated test stack N instead (Redis 6380+N, world port
    2052+N, under build/iso<N>/live; start its Redis and account server with
    `iso_stack.py --slot N start --rolling`).

    --from DIR (before the command) copies a finished build from DIR (`dotnet publish` output with
    its wServer.json) instead of building the source: what a Linux server does, which has no SDK.
    It's kept in the state, so the scheduled restarts use it too.

On Linux (deploy/systemd/bsk-world.service) the service runs `serve`: it starts a world server and
then turns into the world proxy, so every world server it or the proxy starts belongs to the
service. A deploy has to start the new world server inside the service too, so it goes through
`request`, which hands the command to the proxy and shows its output. BSK_WORLD_PORT,
BSK_REDIS_PORT and BSK_PUBLIC_ADDRESS (deploy/bsk.env) override the live ports and the address
the server list gives players.

How it fits together:
- tools/world_proxy.py owns the public world port (2051) and forwards each connection to world
  server slot a (port 12051, world ids from 1000000) or b (22051, from 2000000). Both run from
  build/live/<slot>, built from source/WorldServer with that slot's port and id range in their
  wServer.json (serverInfo.listenPort, serverSettings.worldIdBase).
- deploy builds into the slot that isn't active, starts it, waits until it listens, makes it the
  active one (the proxy sends the Nexus and new logins there) and asks the old one to hand over
  (drain.request in its folder, with the minutes to allow). Players on the old one keep their
  world, dungeons included, and switch at their next trip to the Nexus or a realm; those in its
  Nexus switch at once; when the time is up (--minutes, 60) everyone left is moved to the new
  Nexus. The old server then stops by itself.
- A deploy while the other slot is still handing over waits for it, or with --now moves its
  players to the new Nexus first.
- A new world server is in the state ("starting") from the moment it's started, so it's never
  left running untracked: if it doesn't come up, or the tool is stopped while it waits, the tool
  stops it again (it has no players yet), and a later deploy stops one a dead tool left behind.
"""
import argparse
import json
import os
import shutil
import signal
import socket
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SOURCE = os.path.join(ROOT, "source")
SLOTS = {"a": 1, "b": 2}
WINDOWS = os.name == "nt"
START_MINUTES = 10  # how long a new world server gets to start listening

LIVE = None   # set by configure()
ISO = None
BUILD_FROM = None


def configure(iso, build_from=None):
    global LIVE, ISO, BUILD_FROM
    ISO = iso
    BUILD_FROM = build_from and os.path.abspath(build_from)
    LIVE = os.path.join(ROOT, "build", "live" if iso is None else os.path.join("iso%s" % (iso or ""), "live"))
    os.makedirs(LIVE, exist_ok=True)


def front_port():
    return int(os.environ.get("BSK_WORLD_PORT") or 2051) if ISO is None else 2052 + ISO


def tool_args():
    """The global arguments, for the commands the proxy runs later."""
    return ([] if ISO is None else ["--iso", str(ISO)]) + ([] if BUILD_FROM is None else ["--from", BUILD_FROM])


def slot_port(slot):
    return front_port() + 10000 * SLOTS[slot]


def state_path():
    return os.path.join(LIVE, "state.json")


def read_state():
    try:
        with open(state_path(), encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return {"front_port": front_port(), "bind": "0.0.0.0" if ISO is None else "127.0.0.1",
                "tool_args": tool_args(), "active": None, "backends": {}}


def write_state(st):
    tmp = state_path() + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(st, f, indent=2)
    for _ in range(50):  # the proxy may be reading it
        try:
            os.replace(tmp, state_path())
            return
        except PermissionError:
            time.sleep(0.1)
    os.replace(tmp, state_path())


def alive(pid, names=(b"WorldServer", b"world_proxy")):
    """Whether a world server or world proxy (on Linux: a process with one of these names in its
    command line) runs with this process id."""
    if not pid:
        return False
    if WINDOWS:
        import ctypes
        k = ctypes.windll.kernel32
        h = k.OpenProcess(0x1000, False, pid)  # PROCESS_QUERY_LIMITED_INFORMATION
        if not h:
            return False
        code = ctypes.c_ulong()
        ok = k.GetExitCodeProcess(h, ctypes.byref(code))
        k.CloseHandle(h)
        return bool(ok) and code.value == 259  # STILL_ACTIVE
    try:
        os.waitpid(pid, os.WNOHANG)  # our own child that stopped: collect it
    except ChildProcessError:
        pass
    try:
        with open("/proc/%d/stat" % pid, "rb") as f:
            if f.read().rsplit(b")", 1)[1].split()[0] == b"Z":
                return False  # stopped, not yet collected by its parent
        with open("/proc/%d/cmdline" % pid, "rb") as f:
            cmdline = f.read()
    except (OSError, IndexError):
        return False
    # after a reboot an old id can belong to anything
    return any(n in cmdline for n in names)


def listening(port):
    try:
        with socket.create_connection(("127.0.0.1", port), timeout=1):
            return True
    except OSError:
        return False


def popen(args, cwd, out_name):
    """Live: in its own console window, like the other servers (on Linux: in the service's log).
    Test stacks: hidden, output to a file."""
    if not WINDOWS:
        if ISO is None:
            return subprocess.Popen(args, cwd=cwd, stdin=subprocess.DEVNULL)
        return subprocess.Popen(args, cwd=cwd, stdin=subprocess.DEVNULL, stdout=open(os.path.join(cwd, out_name), "a"),
                                stderr=subprocess.STDOUT, start_new_session=True)
    if ISO is None:
        return subprocess.Popen(args, cwd=cwd, creationflags=subprocess.CREATE_NEW_CONSOLE)
    return subprocess.Popen(args, cwd=cwd, stdout=open(os.path.join(cwd, out_name), "a"), stderr=subprocess.STDOUT,
                            creationflags=subprocess.CREATE_NO_WINDOW | subprocess.CREATE_NEW_PROCESS_GROUP)


def ensure_proxy(st):
    pid_file = os.path.join(LIVE, "proxy.pid")
    pid = int(open(pid_file).read()) if os.path.exists(pid_file) else 0
    if alive(pid) and listening(st["front_port"]):
        return
    if listening(st["front_port"]):
        sys.exit("port %d is taken by something else (an old world server outside the proxy?): stop it first"
                 % st["front_port"])
    p = popen([sys.executable, os.path.join(HERE, "world_proxy.py"), LIVE], LIVE, "proxy.out")
    for _ in range(50):
        if listening(st["front_port"]):
            print("world proxy started on port %d (pid %d)" % (st["front_port"], p.pid))
            return
        time.sleep(0.2)
    sys.exit("the world proxy didn't start: see %s" % os.path.join(LIVE, "proxy.log"))


def set_json(path, fn):
    with open(path, encoding="utf-8-sig") as f:
        data = json.load(f)
    fn(data)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)


def prepare(slot, reuse_from=None):
    """Puts a world server build in build/live/<slot>, configured for that slot."""
    folder = os.path.join(LIVE, slot)
    skip = shutil.ignore_patterns("logs", "GenerateXMLS", "*.request", "*.status", "*.out")
    if reuse_from:
        src = os.path.join(LIVE, reuse_from)
        print("copying the running build from slot %s to slot %s" % (reuse_from, slot))
        shutil.copytree(src, folder, ignore=skip, dirs_exist_ok=True)
    elif BUILD_FROM:
        if not os.path.exists(os.path.join(BUILD_FROM, "WorldServer.dll")):
            sys.exit("no world server build in %s" % BUILD_FROM)
        print("copying the world server build from %s to slot %s" % (BUILD_FROM, slot))
        shutil.copytree(BUILD_FROM, folder, ignore=skip, dirs_exist_ok=True)
    else:
        print("building source/WorldServer into %s" % os.path.relpath(folder, ROOT))
        subprocess.check_call(["dotnet", "build", os.path.join(SOURCE, "WorldServer", "WorldServer.csproj"),
                               "-c", "Debug", "-o", folder, "-v", "q", "--nologo"])
        shutil.copyfile(os.path.join(SOURCE, "WorldServer", "wServer.json"), os.path.join(folder, "wServer.json"))
    return configure_slot(slot)


def configure_slot(slot):
    """Sets a slot's port and world id range in its wServer.json, and clears old requests."""
    folder = os.path.join(LIVE, slot)
    for name in ("drain.request", "drain.status", "restart.request"):
        if os.path.exists(os.path.join(folder, name)):
            os.remove(os.path.join(folder, name))

    def cfg(d):
        d["serverInfo"]["listenPort"] = slot_port(slot)
        d["serverSettings"]["worldIdBase"] = 1000000 * SLOTS[slot]
        if ISO is not None:
            d["dbInfo"]["port"] = 6380 + ISO
            d["serverInfo"]["port"] = front_port()
            d["serverInfo"]["address"] = "127.0.0.1"
            d["serverSettings"]["testStack"] = True   # every character maxed (Player.TestStackStart)
        else:
            if os.environ.get("BSK_WORLD_PORT"):
                d["serverInfo"]["port"] = front_port()
            if os.environ.get("BSK_REDIS_PORT"):
                d["dbInfo"]["port"] = int(os.environ["BSK_REDIS_PORT"])
            if os.environ.get("BSK_PUBLIC_ADDRESS"):
                d["serverInfo"]["address"] = os.environ["BSK_PUBLIC_ADDRESS"]
    set_json(os.path.join(folder, "wServer.json"), cfg)
    return folder


CTRL_C_HELPER = """
import ctypes, sys
k = ctypes.windll.kernel32
k.FreeConsole()
if k.AttachConsole(int(sys.argv[1])):
    k.GenerateConsoleCtrlEvent(1, 0)  # Ctrl+Break: a new process group ignores Ctrl+C
"""


def stop_cleanly(pid, seconds=90):
    """Stops a world server the way Ctrl+C or systemctl stop does, so it saves everyone; by force
    only if it's still running after that long. True once it has stopped."""
    if not alive(pid):
        return True
    try:
        if WINDOWS:
            # Ctrl+Break in its console, from a helper: attaching takes the caller's own console away
            subprocess.run([sys.executable, "-c", CTRL_C_HELPER, str(pid)], capture_output=True, timeout=30,
                           creationflags=subprocess.CREATE_NO_WINDOW)
        else:
            os.kill(pid, signal.SIGTERM)  # SignalListenerLinux
    except (OSError, subprocess.SubprocessError):
        pass
    deadline = time.time() + seconds
    while alive(pid) and time.time() < deadline:
        time.sleep(0.5)
    if alive(pid):
        print("world server pid %d didn't stop in %d seconds: stopping it by force" % (pid, seconds))
        if WINDOWS:
            subprocess.run(["taskkill", "/PID", str(pid), "/T", "/F"], capture_output=True)
        else:
            try:
                os.kill(pid, signal.SIGKILL)
            except OSError:
                pass
        for _ in range(20):
            if not alive(pid):
                break
            time.sleep(0.5)
    return not alive(pid)


class StartFailed(Exception):
    pass


def start_backend(st, slot, folder):
    port = slot_port(slot)
    if listening(port):
        sys.exit("port %d (slot %s) is already taken (a world server the state doesn't know about?): "
                 "stop it with SIGTERM or Ctrl+C, then try again" % (port, slot))
    args = [os.path.join(folder, "WorldServer.exe")] if ISO is None and WINDOWS else ["dotnet", "WorldServer.dll"]
    p = popen(args, folder, "ws.out")
    # in the state at once: if this tool dies while it waits, the next deploy finds the server
    st["backends"][slot] = {"port": port, "pid": p.pid, "started": time.strftime("%Y-%m-%d %H:%M:%S"),
                            "starting": True}
    write_state(st)
    print("started world server %s (pid %d, port %d); waiting until it listens..." % (slot, p.pid, port), flush=True)
    try:
        deadline = time.time() + START_MINUTES * 60
        while not listening(port):
            # our own child: poll() rather than alive(), whose /proc/<pid>/cmdline can read empty
            # while the child is still turning from this Python into dotnet
            if p.poll() is not None:
                raise StartFailed("world server %s stopped while starting: see %s" % (slot, os.path.join(folder, "logs")))
            if time.time() > deadline:
                raise StartFailed("world server %s didn't start listening in %g minutes" % (slot, START_MINUTES))
            time.sleep(1)
    except BaseException as e:  # StartFailed, Ctrl+C, SIGTERM (SystemExit)
        print("%s: stopping it again" % (e if isinstance(e, StartFailed) else "interrupted"), flush=True)
        if stop_cleanly(p.pid):
            del st["backends"][slot]
            print("stopped world server %s (pid %d)" % (slot, p.pid))
        else:
            print("world server %s (pid %d) is still running: it stays in the state (starting), and the "
                  "next deploy stops it" % (slot, p.pid))
        write_state(st)
        if isinstance(e, StartFailed):
            sys.exit(str(e))
        raise
    del st["backends"][slot]["starting"]
    print("world server %s is up" % slot)


def stop_leftover(st, slot):
    """A world server still starting when its deploy died: it never took players, so it's stopped
    rather than waited for."""
    b = st["backends"].get(slot)
    if not b or not b.get("starting") or st["active"] == slot:
        return
    print("world server %s (pid %s) was left starting by an earlier deploy: stopping it" % (slot, b.get("pid")))
    if not stop_cleanly(b.get("pid")):
        sys.exit("world server %s (pid %s) doesn't stop" % (slot, b.get("pid")))
    del st["backends"][slot]
    write_state(st)


def prune(st):
    """Forgets world servers that have stopped."""
    for slot, b in list(st["backends"].items()):
        if not alive(b.get("pid")):
            del st["backends"][slot]
    if st["active"] not in st["backends"]:
        st["active"] = None


def players_left(slot):
    try:
        return int(open(os.path.join(LIVE, slot, "drain.status")).read().strip() or 0)
    except (OSError, ValueError):
        return None


def drain(slot, minutes):
    with open(os.path.join(LIVE, slot, "drain.request"), "w") as f:
        f.write(str(minutes))


def lock():
    path = os.path.join(LIVE, "deploy.lock")
    try:
        with open(path) as f:
            holder = int(f.read().strip() or 0)
        if time.time() - os.stat(path).st_mtime > 1800 or not alive(holder, (b"rolling_restart",)):
            os.remove(path)  # left by a deploy that died
    except (OSError, ValueError):
        pass
    try:
        fd = os.open(path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
    except FileExistsError:
        sys.exit("another deploy is running (%s)" % path)
    os.write(fd, str(os.getpid()).encode())
    os.close(fd)
    return path


def cmd_up(args):
    st = read_state()
    prune(st)
    if not st["active"]:
        slot = "a" if "a" not in st["backends"] else "b"
        if args.no_build and os.path.exists(os.path.join(LIVE, slot, "WorldServer.dll")):
            folder = configure_slot(slot)
        else:
            folder = prepare(slot)
        start_backend(st, slot, folder)
        st["active"] = slot
    write_state(st)
    ensure_proxy(st)
    print("world server %s is active behind the proxy on port %d" % (st["active"], st["front_port"]))


def cmd_deploy(args):
    st = read_state()
    prune(st)
    if not st["active"]:
        sys.exit("no world server is running: use `up`")
    old = st["active"]
    new = "b" if old == "a" else "a"
    path = lock()
    try:
        stop_leftover(st, new)
        if new in st["backends"]:
            left = players_left(new)
            if not args.now:
                print("world server %s is still handing over (%s players left): waiting for it (--now moves them)"
                      % (new, "?" if left is None else left))
            else:
                print("moving the players still on world server %s to the Nexus" % new)
                drain(new, 0)
            while alive(st["backends"][new]["pid"]):
                time.sleep(2)
            del st["backends"][new]
            write_state(st)
        folder = prepare(new, reuse_from=old if args.reuse else None)
        start_backend(st, new, folder)
        st["active"] = new
        write_state(st)
        print("the proxy now sends new connections and the Nexus to world server %s" % new)
        if alive(st["backends"][old]["pid"]):
            drain(old, args.minutes)
            print("world server %s hands its players over (at their next Nexus trip, within %s minutes) and then stops"
                  % (old, args.minutes))
    finally:
        os.remove(path)


def cmd_update(args):
    """What Run Server.bat does: start the proxy and a world server if none is running, otherwise
    deploy the current source to them without kicking anyone. A deploy started while the previous
    one is still handing over moves that one's last players to the Nexus rather than waiting up to
    an hour."""
    st = read_state()
    prune(st)
    if not st["active"]:
        args.no_build = False
        cmd_up(args)
        return
    ensure_proxy(st)
    args.reuse, args.now = False, True
    cmd_deploy(args)


def cmd_status(_):
    st = read_state()
    pid_file = os.path.join(LIVE, "proxy.pid")
    pid = int(open(pid_file).read()) if os.path.exists(pid_file) else 0
    print("proxy: port %d, %s" % (st["front_port"], "running (pid %d)" % pid if alive(pid) else "not running"))
    for slot, b in sorted(st["backends"].items()):
        state = "running" if alive(b.get("pid")) else "stopped"
        left = players_left(slot)
        extra = "" if left is None else ", handing over: %d players left" % left
        print("world server %s: port %d, pid %s, %s%s%s" % (slot, b["port"], b.get("pid"), state,
                                                           " (active)" if slot == st["active"] else "", extra))


def kill(pid):
    if not alive(pid):
        return
    if WINDOWS:
        subprocess.run(["taskkill", "/PID", str(pid), "/T", "/F"], capture_output=True)
        return
    # SIGTERM: a world server saves everyone and stops (SignalListenerLinux)
    os.kill(pid, signal.SIGTERM)
    deadline = time.time() + 90
    while alive(pid) and time.time() < deadline:
        time.sleep(0.5)
    if alive(pid):
        os.kill(pid, signal.SIGKILL)


def cmd_stop(_):
    st = read_state()
    pid_file = os.path.join(LIVE, "proxy.pid")
    if os.path.exists(pid_file):
        kill(int(open(pid_file).read()))
        os.remove(pid_file)
        print("stopped the proxy")
    for slot, b in st["backends"].items():
        kill(b.get("pid"))
        print("stopped world server", slot)
    st["backends"], st["active"] = {}, None
    write_state(st)


def cmd_serve(_):
    """The Linux service (deploy/systemd/bsk-world.service): starts a world server, then becomes
    the world proxy, so the world servers stay in the service: systemd stops them with it, and
    `request` starts the next ones inside it."""
    if WINDOWS:
        sys.exit("serve is for Linux services: use up")
    st = read_state()
    prune(st)
    st["tool_args"] = tool_args()
    if not st["active"]:
        slot = "a" if "a" not in st["backends"] else "b"
        # a cold start takes the newest build: a deploy may have replaced it while stopped
        if BUILD_FROM or not os.path.exists(os.path.join(LIVE, slot, "WorldServer.dll")):
            folder = prepare(slot)
        else:
            folder = configure_slot(slot)
        start_backend(st, slot, folder)
        st["active"] = slot
    write_state(st)
    if listening(st["front_port"]):
        sys.exit("port %d is taken by something else: stop it first" % st["front_port"])
    sys.stdout.flush()
    os.execv(sys.executable, [sys.executable, os.path.join(HERE, "world_proxy.py"), LIVE])


def cmd_request(args):
    """Hands a command to the running world proxy (deploy.request), which runs it inside its
    service, and shows its output until it ends; exits with its exit code."""
    if not args.command:
        sys.exit("request needs a command, such as: request update")
    request = os.path.join(LIVE, "deploy.request")
    result = os.path.join(LIVE, "deploy.result")
    out = os.path.join(LIVE, "deploy.log")
    pid_file = os.path.join(LIVE, "proxy.pid")

    def proxy_alive():
        return os.path.exists(pid_file) and alive(int(open(pid_file).read()))

    if not proxy_alive():
        sys.exit("the world proxy isn't running (start its service: systemctl start bsk-world)")
    if os.path.exists(result):
        os.remove(result)
    pos = os.path.getsize(out) if os.path.exists(out) else 0
    with open(request + ".tmp", "w", encoding="utf-8") as f:
        json.dump(args.command, f)
    os.replace(request + ".tmp", request)
    print("asked the world proxy to run: %s" % " ".join(args.command), flush=True)
    asked = time.time()
    while True:
        done = os.path.exists(result)
        if os.path.exists(out):
            with open(out, "rb") as f:
                f.seek(pos)
                data = f.read()
            pos += len(data)
            sys.stdout.write(data.decode("utf-8", "replace"))
            sys.stdout.flush()
        if done:
            with open(result, encoding="utf-8") as f:
                sys.exit(json.load(f).get("code", 1))
        if os.path.exists(request) and time.time() - asked > 30 and not proxy_alive():
            sys.exit("the world proxy stopped before taking the request")
        if time.time() - asked > 3 * 3600:
            sys.exit("no result after 3 hours")
        time.sleep(1)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--iso", type=int, help="isolated test stack N instead of the live servers")
    ap.add_argument("--from", dest="build_from", help="copy this finished world server build instead of building")
    sub = ap.add_subparsers(dest="cmd", required=True)
    u = sub.add_parser("up")
    u.add_argument("--no-build", action="store_true", help="start the last build of the slot")
    d = sub.add_parser("deploy")
    d.add_argument("--reuse", action="store_true", help="copy the running build instead of building the source")
    d.add_argument("--minutes", type=float, default=60, help="how long players get to reach the Nexus (60)")
    d.add_argument("--now", action="store_true", help="if the other slot is still handing over, move its players now")
    upd = sub.add_parser("update")
    upd.add_argument("--minutes", type=float, default=60, help="how long players get to reach the Nexus (60)")
    sub.add_parser("status")
    sub.add_parser("stop")
    sub.add_parser("serve")
    r = sub.add_parser("request")
    r.add_argument("command", nargs=argparse.REMAINDER, help="the command and its options (update, deploy --reuse...)")
    args = ap.parse_args()
    configure(args.iso, args.build_from)
    if not WINDOWS:
        # systemctl stop, or the proxy ending a scheduled restart that hangs: clean up (stop a
        # starting world server, free the deploy lock) rather than die on the spot
        signal.signal(signal.SIGTERM, lambda *_: sys.exit("stopped (SIGTERM)"))
    {"up": cmd_up, "update": cmd_update, "deploy": cmd_deploy, "status": cmd_status, "stop": cmd_stop,
     "serve": cmd_serve, "request": cmd_request}[args.cmd](args)


if __name__ == "__main__":
    main()
