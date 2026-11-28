# AIR client (deprecated)

**Deprecated.** Players use the Rust client ([../client-rs/AGENTS.md](../client-rs/AGENTS.md)).
Don't build, test or change this client unless the owner asks; test in game with the Rust client's
scripted flags. Read [../AGENTS.md](../AGENTS.md) first.

## Its `src/` is still the game's art and data

The Rust client reads its XML and sprite sheets from there (`tools/gen_rs_manifest.py`), so keep
those files, and edit an XML here together with its server copy in `source/Shared/resources/xml`.

- Object, item and ground XML is listed in `kabam/rotmg/assets/EmbeddedData.as`; the sprite sheets
  in `AssetLoader.as` and `assets/doomed/DoomedAssets.as` (the `dr` sheets).
- Slot types are named in `kabam/rotmg/constants/ItemConstants.as` (126 and 128 are the newer
  classes', since 26 and 28 are pet eggs and lutes); `kabam/rotmg/classes/ClassesConfig.as` loads
  the classes.

## Only if the owner asks for a change

Decompiled 7.0 ActionScript 3 (`_arg_1`-style names: match the file you're in), with
Robotlegs/Swiftsuspenders and as3-signals; built with AIR SDK 32 in `tools/airsdk/`.

```
python tools\build_client.py [host] [--package]   # client\bin\client.swf, or dist\betterSkillys-Client (+ .zip)
Play Client.bat                                   # runs client\bin\client.swf with adl
```

- `build_client.py` compiles the account server address into a copy of
  `kabam/rotmg/application/impl/LocalhostSetup.as` under `build/src`: never edit a host into `src/`.
  `--package` builds into `dist/betterSkillys-Client.new` and swaps it in.
- **New messages:** an outgoing class in `kabam/rotmg/messaging/impl/outgoing/`, mapped and sent
  in `GameServerConnection.as`, with the same id in the server's `MessageIds.cs`.
- **Drawing:** `GameSprite.onEnterFrame`, then `Map.draw` and `Renderer.renderScene` (Stage3D).
  Keep the software path (`Parameters.isGpuRender()` false) working, and allocate nothing per
  frame (no new objects or strings, no `TextField.text` unless the value changed).
- **Stage3D pitfalls:** upload a vertex buffer in full once (#3608, often silent); every index is
  checked against the bound vertex buffer (#3609); textures are cached by `BitmapData`, so changed
  pixels need a new one; `mscale` is 1 until the player enters the game (a 0-wide back buffer
  raises #3780).
