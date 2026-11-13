Release 2.5.7: chat channels, picked with tabs on the chat box or /all, /local, /guild and /party (deploys the client and the world server; nobody is disconnected)

## For players

- **Chat:** tabs on the chat box's top edge (All, Local, Guild, Party) pick where the lines you type go, the box taking the channel's colour, and `/all`, `/local`, `/guild` or `/party` in front of a line sends just that line there. `/all` reaches players on every world; `/local` is the world you are in, as plain chat always was.

## Details

### Chat channels

- **What changed:** `/all` and `/local` are read as channels in `PlayerTextHandler` and go through the same checks as plain text (name, mute, spam; `/all` alone answers with its usage). `/all` publishes a new `ChatType.All` over the inter-server chat channel, and every world server delivers it to its players (`Player.AllReceived`: recipient `*All*`, no speech bubble). Plain text, `/guild` and `/party` work as before. On the client, `hud::ChatChannel` (All, Local, Guild, Party) draws four tabs on the chat box's top edge (`draw_chat_tabs`, `ChatClickTarget::Channel`): each sized to its label with a dot in its channel's colour, the picked one joined to the box like a browser tab, and the box's edge and glow in that colour (All amber, Local pale lilac, Guild green, Party blue, as their lines in the chat); the others lighten under the mouse; Enter sends the line through `ChatChannel::send_line`, which puts the channel's command in front unless the line already starts with `/`. The choice lives in `App.chat_channel` and isn't saved.
- **Files:** `source/Shared/isc/ChatType.cs`, `source/WorldServer/core/net/handlers/PlayerTextHandler.cs`, `source/WorldServer/core/miscfile/ChatManager.cs`, `source/WorldServer/core/objects/player/Player.Chat.cs`, `client-rs/app/src/hud.rs`, `client-rs/app/src/main.rs`, `client-rs/app/src/login.rs` (the `--chat <channel>[:text]` test flag, in `client-rs/AGENTS.md`), `client-rs/app/lang/*.json` (Local, Guild and Party added to all ten tables; All was there), `client-rs/CHANGELOG.md`, `website/wiki/getting-started.md`.
- **Redeploy:** the world server (rolling; nobody is disconnected) and the client. Nothing in Redis. Until every world server runs this build, `/all` only reaches the players on the ones that do: an older world server doesn't know the new chat type and drops it.
- **Not done or known to be off:** `/all`, `/local`, `/guild` and `/party` aren't in the `/help` list (they aren't command classes).

### Deploy from the PC: gateway build in WSL

- **What changed:** on Windows, `deploy/deploy.py` (`build_gateway`) no longer runs cargo on the checkout through `/mnt/c`, which was slow and made WSL show its "Performance Tip" pop-up on every deploy. It sends `client-rs` (without `target`) into WSL as a tar on stdin, unpacks it in `~/.cache/bsk-gateway-src` and builds there (same target folder, `~/.cache/bsk-gateway`). Linux and the GitHub workflow build as before.
- **Files:** `deploy/deploy.py`.
- **Redeploy:** nothing; it only changes how a deploy from the PC builds.

## Checks

- **Chat channels:** `cargo test -p bsk-client` (98 passed, the translation tables included); `dotnet build` of `source/WorldServer` succeeds with no warnings; `python tools/build_wiki.py` builds. `--shot`s on an isolated stack at 1300x800 and 2560x1440 with `--chat` on All, Guild and Party, in English and Polish (the longest labels), with a tab under the mouse. Not run across two live world servers.
- **Deploy from the PC: gateway build in WSL:** `build_gateway` run on its own builds `bsk-gateway` in WSL from the copy (11 s, only the gateway recompiled) and copies it into the bundle. No full deploy run.
