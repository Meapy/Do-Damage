# AIR client (deprecated)

**Deprecated.** Players use the Rust client ([../client-rs/AGENTS.md](../client-rs/AGENTS.md)),
and the live servers are on the VPS (dodmg.com), not the `188.141.12.3` address below. Don't
build, test or change this client unless the owner asks; test in game with the Rust client's
scripted flags. Read [../AGENTS.md](../AGENTS.md) first.

**Its `src/` is still the game's art and data:** the Rust client reads its XML and sprite sheets
from there (`tools/gen_rs_manifest.py`), so keep those files, and edit an XML here together with
its server copy in `source/Shared/resources/xml`. Object, item and ground XML is listed in
`kabam/rotmg/assets/EmbeddedData.as`; the sprite sheets in `AssetLoader.as` and
`assets/doomed/DoomedAssets.as` (the `dr` sheets). Slot types are named in
`kabam/rotmg/constants/ItemConstants.as` (126 and 128 are the newer classes', since 26 and 28
are pet eggs and lutes), and `kabam/rotmg/classes/ClassesConfig.as` loads the classes.

## Reference

The client is decompiled 7.0 ActionScript 3 (`_arg_1`-style names: match the file you're in),
with Robotlegs/Swiftsuspenders and as3-signals.

```
python tools\build_client.py                           # client\bin\client.swf, account server localhost:8080
python tools\build_client.py 188.141.12.3 --package    # dist\betterSkillys-Client (+ .zip)
Play Client.bat                                        # runs client\bin\client.swf with adl
```

- The account server address is compiled into `kabam/rotmg/application/impl/LocalhostSetup.as`
  by `build_client.py` (in a copy under `build/src`): never edit the host into `src/`.
- `--package` builds into `dist/betterSkillys-Client.new` and swaps it in; never replace that
  folder by hand while it may be running. Builds are release builds; never time a debug build.
- **Drawing:** `GameSprite.onEnterFrame` updates the map and camera, then `Map.draw` (ground
  chunks, culling, objects as `IGraphicsData`) and `Renderer.renderScene` turn it into Stage3D
  draws (one vertex buffer per frame, an atlas of up to six 1024² pages, one call for shadows).
  Debug switches: `Renderer.batching`, `Renderer.atlasing`, `Map.chunking`, `Map.culling`,
  `Face3D.caching`. Keep the software path (`Parameters.isGpuRender()` false) working.
- **Stage3D pitfalls:** upload a vertex buffer in full once (Error #3608 otherwise, often silent);
  every index is checked against the bound vertex buffer (#3609); textures are cached by
  `BitmapData`, so changed pixels need a new one; `GraphicsFillExtra.getColorTransform` uses up
  the tint (`peekColorTransform` only looks).
- **Per-frame code** allocates nothing: no new objects or strings, no `Number` dictionary keys,
  no `TextField.text` unless the value changed.
- **New messages:** an outgoing class in `kabam/rotmg/messaging/impl/outgoing/`, mapped and sent
  in `GameServerConnection.as`, with the same id in the server's `MessageIds.cs`. Deploy both.
- **Test client** (`tools/testclient/`, its own app id; needs the Tester account in
  `test-account.local.txt`): `build_test_client.py --port 8081 --release`, then
  `testclient.ps1 start|front|enter|send "<line>"|shot <png>`. `send` lines without `#` go to
  chat; `#` switches toggle the renderer's optimisations (`#batch|atlas|chunk|cull|cache 0|1`,
  `#errors 1`, `#fps <n>`, `#sample <frames>`...). AIR stops running frames while its window is
  covered, and bringing it forward takes the screen: check `testclient.ps1 idle` and ask the
  person at the PC first.
- **Quirks:** `mscale` defaults to 1 until the player enters the game (a 0-wide back buffer
  raises #3780); the FPS option defaults to 60; `GameSprite` keeps `lastUpdate_` current when a
  frame throws.
