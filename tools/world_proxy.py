"""The world proxy: owns the public world server port and forwards each game connection to one of
two world servers on this PC, so a new world server can take over without disconnecting anyone.

    python tools/world_proxy.py <state folder>       # build/live, or build/iso<N>/live

tools/rolling_restart.py starts it and keeps its state, <state folder>/state.json:

    {"front_port": 2051, "bind": "0.0.0.0", "active": "b",
     "backends": {"a": {"port": 12051, "pid": 123}, "b": {"port": 22051, "pid": 456}}}

- Each connection's first message is the client's Hello. Its game id says where it goes: world
  ids from 1000000 belong to world server slot a, from 2000000 to slot b (each server's
  serverSettings.worldIdBase), so a client changing worlds inside the old server stays there.
  The Nexus (-2), the other negative ids and anything unknown go to the active server.
- Before the Hello it sends a PROXY_INFO message (id 250) with the client's address, as the
  servers would otherwise see every player as 127.0.0.1 (IP bans, the account's IP). A client's
  own PROXY_INFO before its Hello is dropped, except from this machine (or an address in
  BSK_TRUSTED_GATEWAYS, another machine's gateway bridging to this one): the gateway
  (`bsk-gateway --forward-address`, for players in a browser or on wss) sends the player's
  address that way, and the proxy passes that one on instead of the gateway's.
- It reads the state again whenever the file changes: a deploy only rewrites it.
- When the active server asks for its scheduled restart (restart.request in its folder), it runs
  `rolling_restart.py deploy --reuse`, so the 2-hourly restart kicks nobody either. A request
  that comes while another command runs waits for it (and says so); one from a server that is no
  longer the active one is dropped (it's being replaced already); a scheduled restart still
  running after RESTART_HOURS is stopped (SIGTERM: the tool stops the server it was starting).
  A server whose restart didn't happen asks again later (GameServer).
- A world server that is still starting ("starting" in the state) gets no connections.
- It runs the command in deploy.request (a JSON list of rolling_restart.py arguments, written by
  `rolling_restart.py request`), its output in deploy.log and its exit code in deploy.result: on
  Linux the world servers it starts then belong to the proxy's service, as the first one does.
"""
import asyncio
import json
import os
import socket
import struct
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
WINDOWS = os.name == "nt"
HELLO, PROXY_INFO = 16, 250
POLICY = struct.pack(">i", 1014001516)  # "<pol": a Flash policy-file request
SLOT_BY_RANGE = {1: "a", 2: "b"}       # world id // 1000000
MAX_FIRST = 1 << 20
RESTART_HOURS = 2  # a deploy --reuse waits for a hand-over (60 minutes) and a start (10)

STATE_DIR = None
state = {}
state_mtime = 0
log_file = None


def log(msg):
    line = time.strftime("%Y-%m-%d %H:%M:%S ") + msg
    print(line, flush=True)
    if log_file:
        log_file.write(line + "\n")
        log_file.flush()


def load_state():
    global state, state_mtime
    path = os.path.join(STATE_DIR, "state.json")
    try:
        mtime = os.stat(path).st_mtime
        if mtime == state_mtime:
            return
        with open(path, encoding="utf-8") as f:
            new = json.load(f)
    except (OSError, ValueError):
        return  # being rewritten: next time
    if state.get("active") != new.get("active"):
        log("active world server: %s (port %s)" % (new.get("active"), backend_port(new, new.get("active"))))
    state, state_mtime = new, mtime


def backend_port(st, slot):
    b = st.get("backends", {}).get(slot)
    return b and not b.get("starting") and b.get("port")


def route(game_id):
    """The slot a Hello with this game id goes to."""
    active = state.get("active")
    if game_id is not None and game_id > 0:
        slot = SLOT_BY_RANGE.get(game_id // 1000000)
        if slot and backend_port(state, slot):
            return slot
    return active


def hello_game_id(body):
    """The game id in a Hello's body (after the message id), or None."""
    try:
        (n,) = struct.unpack_from(">h", body, 0)
        (game_id,) = struct.unpack_from(">i", body, 2 + n)
        return game_id
    except struct.error:
        return None


def parse_proxy_info(body):
    try:
        (n,) = struct.unpack_from(">h", body)
        return body[2:2 + n].decode("utf-8") if 0 < n <= 64 and len(body) >= 2 + n else None
    except (struct.error, UnicodeDecodeError):
        return None


# Other machines whose gateway may give its players' addresses too (BSK_TRUSTED_GATEWAYS, IPs
# separated by commas): Europe 2's proxy trusts dodmg.com's gateway, which bridges its players.
TRUSTED_GATEWAYS = {a.strip() for a in os.environ.get("BSK_TRUSTED_GATEWAYS", "").split(",") if a.strip()}


def is_local(address):
    return address == "::1" or address.startswith("127.") or address in TRUSTED_GATEWAYS


async def read_first(creader):
    """The connection's next message (or the Flash policy request), as bytes."""
    first = await asyncio.wait_for(creader.readexactly(4), 30)
    if first != POLICY:
        (length,) = struct.unpack(">i", first)
        if not 5 <= length <= MAX_FIRST:
            raise ValueError("bad length")
        first += await asyncio.wait_for(creader.readexactly(length - 4), 30)
    return first


def proxy_info(address):
    data = address.encode("utf-8")
    body = struct.pack(">h", len(data)) + data
    return struct.pack(">iB", 5 + len(body), PROXY_INFO) + body


async def pipe(reader, writer):
    try:
        while True:
            data = await reader.read(65536)
            if not data:
                break
            writer.write(data)
            await writer.drain()
    except (OSError, asyncio.CancelledError):
        pass
    finally:
        try:
            writer.close()
        except OSError:
            pass


def nodelay(writer):
    sock = writer.get_extra_info("socket")
    if sock is not None:
        try:
            sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        except OSError:
            pass


async def handle(creader, cwriter):
    nodelay(cwriter)
    peer = cwriter.get_extra_info("peername") or ("?", 0)
    address = peer[0]
    if address.startswith("::ffff:"):
        address = address[7:]
    try:
        first = await read_first(creader)
        if first != POLICY and first[4] == PROXY_INFO:
            # the gateway's word for its player's address; anyone else's is dropped
            forwarded = parse_proxy_info(first[5:]) if is_local(address) else None
            if forwarded:
                address = forwarded
            first = await read_first(creader)
        game_id = None
        if first != POLICY and first[4] == HELLO:
            game_id = hello_game_id(first[5:])
    except (asyncio.TimeoutError, asyncio.IncompleteReadError, OSError, ValueError):
        cwriter.close()
        return

    load_state()
    slot = route(game_id)
    tried = []
    for s in (slot, state.get("active")):
        port = backend_port(state, s)
        if not port or s in tried:
            continue
        tried.append(s)
        try:
            breader, bwriter = await asyncio.wait_for(asyncio.open_connection("127.0.0.1", port), 5)
            break
        except (OSError, asyncio.TimeoutError):
            log("world server %s (port %s) didn't answer" % (s, port))
    else:
        log("no world server for %s (game %s)" % (address, game_id))
        cwriter.close()
        return

    nodelay(bwriter)
    if first != POLICY:
        bwriter.write(proxy_info(address))
    bwriter.write(first)
    log("%s -> %s (game %s)" % (address, tried[-1], game_id))
    await asyncio.gather(pipe(creader, bwriter), pipe(breader, cwriter))


def run_tool(args, out_name):
    """Runs rolling_restart.py with the state's global arguments, output to a file here."""
    out = open(os.path.join(STATE_DIR, out_name), "a")
    flags = {"creationflags": subprocess.CREATE_NO_WINDOW} if WINDOWS else {"stdin": subprocess.DEVNULL}
    return subprocess.Popen([sys.executable, os.path.join(HERE, "rolling_restart.py")]
                            + state.get("tool_args", []) + args, stdout=out, stderr=subprocess.STDOUT, **flags)


def collect_stopped():
    """Linux: world servers started by `rolling_restart.py serve` are this process's children,
    and stay zombies until collected."""
    if WINDOWS:
        return
    for b in state.get("backends", {}).values():
        try:
            if b.get("pid"):
                os.waitpid(b["pid"], os.WNOHANG)
        except ChildProcessError:
            pass


async def supervise():
    """Reloads the state, and runs a rolling restart when the active server asks for one, or the
    command a deploy asks for (deploy.request)."""
    restarting = None
    restart_started = 0
    deploying = None
    waiting_said = False
    while True:
        load_state()
        collect_stopped()
        active = state.get("active")
        request = active and os.path.join(STATE_DIR, active, "restart.request")
        deploy_request = os.path.join(STATE_DIR, "deploy.request")
        for slot in ("a", "b"):
            other = os.path.join(STATE_DIR, slot, "restart.request")
            if slot != active and os.path.exists(other):
                try:
                    os.remove(other)
                    log("world server %s asked for a restart, but it isn't the active one (it's being replaced): "
                        "dropped" % slot)
                except OSError:
                    pass
        if restarting is not None and restarting.poll() is None and time.time() - restart_started > RESTART_HOURS * 3600:
            log("the scheduled restart is still running after %d hours: stopping it" % RESTART_HOURS)
            restarting.terminate()
            restart_started = time.time()  # once more after as long, if it doesn't stop
        if restarting is not None and restarting.poll() is not None:
            log("scheduled restart finished (exit code %s)%s" % (
                restarting.returncode, "" if restarting.returncode == 0 else ": see restart.log"))
            restarting = None
        if deploying is not None and deploying.poll() is not None:
            log("requested command finished (exit code %s)" % deploying.returncode)
            with open(os.path.join(STATE_DIR, "deploy.result"), "w", encoding="utf-8") as f:
                json.dump({"code": deploying.returncode}, f)
            deploying = None
        if request and os.path.exists(request):
            if restarting is None and deploying is None:
                try:
                    os.remove(request)
                except OSError:
                    pass
                log("world server %s asks for its scheduled restart: starting a new one beside it" % active)
                restarting = run_tool(["deploy", "--reuse"], "restart.log")
                restart_started = time.time()
                waiting_said = False
            elif not waiting_said:
                log("world server %s asks for its scheduled restart: waiting for the %s that is running" % (
                    active, "scheduled restart" if restarting is not None else "requested command"))
                waiting_said = True
        if os.path.exists(deploy_request) and restarting is None and deploying is None:
            try:
                with open(deploy_request, encoding="utf-8") as f:
                    args = [str(a) for a in json.load(f)]
                os.remove(deploy_request)
            except (OSError, ValueError):
                args = None
            if args:
                log("running the requested command: %s" % " ".join(args))
                deploying = run_tool(args, "deploy.log")
        await asyncio.sleep(1)


async def main():
    global STATE_DIR, log_file
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    STATE_DIR = os.path.abspath(sys.argv[1])
    log_file = open(os.path.join(STATE_DIR, "proxy.log"), "a", encoding="utf-8")
    load_state()
    if not state:
        sys.exit("no state in %s (start it with tools/rolling_restart.py up)" % STATE_DIR)
    if WINDOWS:
        try:
            import ctypes
            ctypes.windll.kernel32.SetConsoleTitleW("World Proxy (port %d)" % state["front_port"])
        except Exception:
            pass
    server = await asyncio.start_server(handle, state.get("bind", "0.0.0.0"), state["front_port"])
    log("world proxy on %s:%d, active world server %s" % (state.get("bind", "0.0.0.0"), state["front_port"], state.get("active")))
    with open(os.path.join(STATE_DIR, "proxy.pid"), "w") as f:
        f.write(str(os.getpid()))
    async with server:
        await asyncio.gather(server.serve_forever(), supervise())


if __name__ == "__main__":
    asyncio.run(main())
