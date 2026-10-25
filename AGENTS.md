# betterSkillys: working notes

A Realm of the Mad God private server (C#, .NET 8) and its clients, for a small group of players.
Players know the game as **Do Damage**: that's the name in the client, on the website and in
anything players read; `betterSkillys` stays the repository's and the code's name. The live game
runs on a Linux VPS at **dodmg.com**. Read the guide for the part you're changing:

- [client-rs/AGENTS.md](client-rs/AGENTS.md): the client players use, in Rust (desktop and
  browser), and the gateway that serves browser play.
- [source/AGENTS.md](source/AGENTS.md): the servers (account server, world server, behaviours,
  worlds, Redis data).
- [SETUP-LOCAL.md](SETUP-LOCAL.md): how to run, deploy and share the game ("Running on a VPS"),
  and the list of changes made here. Add to its "Changes from upstream" or "Content ported from
  Doomed Realms" section when you change behaviour or add content.
- [client/AGENTS.md](client/AGENTS.md): the old Adobe AIR client. **Deprecated**: don't build,
  test or change it unless the owner asks.
- `website/` is the site at dodmg.com (built by `tools/build_site.py`); `deploy/` puts
  everything on the VPS.

## What runs where

**Live, on the VPS** (dodmg.com, 169.58.244.146, Ubuntu 24.04; systemd services, see
SETUP-LOCAL.md):

| Part | Port | Notes |
|---|---|---|
| Caddy | 80, 443 | https for everything: the website at `/`, browser play at `/play/`, and `/api` and `/ws` to the gateway |
| Gateway (`bsk-gateway@a`/`b`/`c`) | 8095-8097, VPS only | web client, account requests and the game over WebSocket; one slot takes new players, another may be handing over |
| Account server (`bsk-account`) | 8080 | open for older desktop clients |
| World proxy and world servers (`bsk-world`) | 2051 (slots 12051 / 22051, VPS only) | rolling deploys, as on Windows |
| Redis (`bsk-redis`) | 6379, VPS only | data in `/opt/bsk/redis/data` |

**On this PC:** the old live servers are **stopped** (the game moved to the VPS on 2026-10-16;
this PC's `source/Redis/data` is the data as it was then, not live). `tools/update_relay.py` may
be running on 8080: it hands the client releases to desktop clients built for this PC's address
so they switch to dodmg.com. Isolated test servers use 6380 / 8081 / 2052, slot N adding N
(`tools/testclient/iso_stack.py`, under `build/iso*/`).

The tools the VPS uses (`world_proxy.py`, `rolling_restart.py`) must keep working on both Windows
and Linux. Doomed Realms, another server on this PC, uses 8888, 2050 and 843: don't take those
ports. `Run Server.bat` would start a separate copy of the servers on this PC, on its old data, so
it asks first; never run test copies under the live process names (`redis-server.exe`,
`App.exe`, `WorldServer.exe`; `iso_stack.py` doesn't).

## Ground rules

- **Real people play on the live servers.** Anything that changes what they run (a deploy, a
  publish, a restart) is the owner's call: ask first, every time, unless they just asked for it.
  Test on an isolated stack instead. What disconnects whom:
  - a deploy (`deploy/deploy.py`) kicks nobody: a new world server is a rolling deploy, a new
    gateway starts in another slot while the old one hands over, the account server restart only
    pauses sign-ins, and Caddy keeps WebSockets through a reload (SETUP-LOCAL.md, "Deploys that
    kick nobody"). The first deploy onto the gateway slots (from the single `bsk-gateway` of
    before 2026-10-17) is the exception: it disconnects https players once;
  - restarting `bsk-world` itself kicks everyone (they're saved first);
  - restarting or stopping a gateway slot (`bsk-gateway@<slot>`) or Caddy drops the players on it,
    nearly everyone (browser and https desktop clients). The Rust client then takes them back to
    the Nexus by itself.
- **Stopping a world server cleanly:** it saves everyone on Ctrl+C (Windows) or SIGTERM (Linux,
  `systemctl stop`). `taskkill /F`, which `rolling_restart.py stop` uses on Windows, skips the
  save. To stop one from a script on Windows, attach to its console and send Ctrl+C
  (`AttachConsole` and `GenerateConsoleCtrlEvent`).
- **Never drive a player's window**: the clients' titles are "Do Damage" (the Rust client) and
  "betterSkillys" (the old AIR client). Test with the Rust client's scripted flags, in its own
  window, on an isolated stack (client-rs/AGENTS.md).
- **The client and server ship together** when a message changes (ids, fields). An old client
  must keep working with a new server where possible. Note in the commit which side needs
  redeploying.
- **Client releases are published from the main checkout only, after the release's pull request
  is merged** (Git, "Release branches"). It holds the signing key
  (`build/client-update.key`) and `dist/client`. Clients only take releases signed with that key,
  so publishing from a worktree or a fresh clone makes a new key and strands every player.
- **Secrets** stay in git-ignored files: `*.local.txt` (`test-account.local.txt`),
  `deploy/*.local.json` (the VPS config), `build/` (signing key) and `~/.ssh/dodmg_ed25519` (the
  VPS key). Never sign in anywhere with a password pasted into a chat: ask the owner to set up a
  key instead. `build/`, `dist/`, `client/bin/`, `tools/airsdk/` and `source/Redis/data/` are not
  tracked.
- **Changelog:** every change players can notice (client, servers, content, website, fixes) gets a
  line in [client-rs/CHANGELOG.md](client-rs/CHANGELOG.md) under `## Unreleased` at the top, in
  the same commit as the change. Write it in plain words for players, not code names; the game's
  Changelog window and the website show it. Put it in its `### ` group, summed up with what's
  already there rather than as a new line for each detail. Start the section if it isn't there.
  Publishing a client version renames it to that version, so only release lines that are true for
  players by then: hold back a line whose change isn't live yet (put it back under `Unreleased`).
- **Wiki:** a finished change players can notice (content, items, drops, classes, commands, a
  feature) also updates the wiki, `website/wiki/` (built by `tools/build_wiki.py`), in the same
  commit. Much of it is read from the game's data (the gear pages and their drops, dungeons,
  commands, the forge's recipes), so build it (`python tools/build_wiki.py --out <a scratch
  folder>`) and read the pages the change touches. Fix the generator (`tools/wiki_*.py`) when it
  doesn't pick the change up, and edit the hand-written guides (`website/wiki/*.md`) where they
  describe it. Measured tables (Gear statistics) stay as measured: add a note when one goes out of
  date.
- **Generated files** come from the `tools/port_doomed*.py` scripts (see source/AGENTS.md). Change
  the script and regenerate rather than editing the output by hand.
- **DNS:** dodmg.com's records live at Register365 with a TTL of a day, so a change can take that
  long to reach everyone. Check several resolvers (1.1.1.1, 8.8.8.8, 9.9.9.9) before relying on
  one.

## Git

- **Release branches (since 2026-10-24).** `main` only ever gets one squashed commit per
  released version, through a pull request; nothing else lands on it directly.
  - **Collecting work:** finished work goes into the release branch for the next version,
    `release/<version>`, such as `release/2.0.3`.
    - Find the open one with `git branch --list 'release/*'`; if there is none, create it from
      `main`, named for the next patch version (the owner may pick another number).
    - Sessions work in worktrees under `.claude/worktrees/`, rebase onto the release branch
      (`git rebase release/<version>`), then fast-forward it from the main checkout:
      `git -C <main checkout> fetch . <branch>:release/<version>`, which refuses anything but a
      fast-forward. When the main checkout has the release branch checked out, `fetch` refuses
      it: use `git -C <main checkout> merge --ff-only <branch>` there instead.
    - A hook stops edits into the main checkout from a worktree session.
    - **Clean up once it's in.** When a session's work has been fast-forwarded into the release
      branch, that session removes its own worktree and branch (or the app's worktree clean-up
      does):
      `git -C <main checkout> worktree remove <worktree>`, then
      `git -C <main checkout> branch -D <branch>`. Check first that nothing is uncommitted
      (`git status --short`; `__pycache__` and `TODO.MD` don't count) and that the work is on the
      release branch (`git merge-base --is-ancestor <branch> release/<version>`). Plain
      `worktree remove` refuses a worktree with changes: look at them rather than reaching for
      `--force`. On Windows the emptied folder stays until the session that used it is closed.
  - **Releasing** (the owner asks for it):
    1. On the release branch, name the changelog section and raise the version (client-rs/AGENTS.md,
       "Releasing to players"), and check the client tests, `python tools\build_site.py` and the
       server builds.
    2. Push it (`git push -u origin release/<version>`) and open a pull request against `main`
       (`gh pr create --base main --head release/<version>`), its body listing what's in the
       release.
    3. Squash and merge it (`gh pr merge --squash --delete-branch`, the title naming the
       version), then update the main checkout (`git -C <main checkout> switch main` and
       `git -C <main checkout> pull --ff-only`). `--delete-branch` removes the release branch on
       origin; delete it locally too (`git -C <main checkout> branch -D release/<version>`), and
       remove any worktrees and branches whose work it carried.
    4. Only then deploy, from the main checkout (`python deploy\deploy.py`), so what players get is
       exactly `main`. It publishes the client first when the client changed since the last
       release (client-rs/AGENTS.md, "Releasing to players").
- **No merge commits** on `main` or a release branch: if the branch moved while you worked,
  rebase onto it instead of merging it in. Never `git merge` one branch into another, and don't
  rewrite commits already pushed to `origin` unless the owner asks.
- Another session may be editing the same tree. Stage and commit explicit paths only (never
  `git add -A` or `git commit -a`), and check `git status` for changes that aren't yours.
- `core.autocrlf` is on: the repository stores LF, while many working files are CRLF. Scripts
  that patch source text should detect and keep each file's line endings; `sed -i` in Git Bash
  can turn a CRLF file into LF.
- `origin` is [Meapy/Do-Damage](https://github.com/Meapy/Do-Damage), a standalone repository
  (not a fork). Its history was condensed on 2026-10-18 to one commit per milestone; the full
  history before that is the local branch `full-history` and the old fork, Meapy/betterSkillys
  (SETUP-LOCAL.md, "Code history"). Never push `full-history`. A branch cut from `main` before
  then shares no history with it: move it over with
  `git rebase --onto main full-history <branch>`.
- Commit messages, merge messages and authors name the change, not the tool that made it: when
  merging `main` into a worktree branch, write the message yourself
  (`git merge -m "Merge main" main`), since the default names the `claude/...` branch.
- The remotes `aurusenth` (Aurusenth/betterSkillys) and `upstream` (HaloCelsius/betterSkillys)
  were merged in earlier. Their histories aren't in `main` any more, so pick up a later change
  from them by hand (cherry-pick or copy), not with `git merge`.

## Tools

- Python 3 for the scripts in `tools/` and `deploy/`.
- The .NET 8 SDK for the servers.
- Rust (stable, with the `wasm32-unknown-unknown` target and `wasm-bindgen-cli`) for
  `client-rs/`, and WSL with Ubuntu 24.04 and Rust inside it, which builds the Linux gateway for
  the VPS.
- Git Bash rewrites arguments that start with `/` into Windows paths: prefix
  `MSYS_NO_PATHCONV=1` for slash commands, and give Windows programs Windows paths
  (`C:/Users/...`, not `/c/Users/...`).
- AIR SDK 32, only for the deprecated AIR client, unzipped into `tools/airsdk/`.
