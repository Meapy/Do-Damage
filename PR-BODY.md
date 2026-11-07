Release 2.4.4. Deploys the account server and Caddy's config (the deploy rewrites the Caddyfile and reloads Caddy). Nobody is disconnected.

## For players

- **Website:** `https://dodmg.com/realmdex/stats` gives the number of players online as plain text, for RealmDex's server list.

## Details

### RealmDex stats endpoint

- `source/App/Controllers/RealmdexController.cs`: `GET`/`POST realmdex/stats` answers the players online as plain text (`text/plain`, e.g. `12`), the same count as `app/status` (`StatusHistory.Current()`: the public world servers, admin-only ones left out). `Cache-Control: no-store` and `Access-Control-Allow-Origin: *`, so a page on another site can read it too.
- `deploy/deploy.py`: the Caddyfile rewrites `/realmdex/stats` to `/api/realmdex/stats`, which the gateway forwards to the account server. It also answers on the account server's own port (`http://dodmg.com:8080/realmdex/stats`).
- Redeploy: the account server (a restart only pauses sign-ins) and Caddy (reloads only because the Caddyfile changed, keeping WebSockets). Nothing in Redis changes. Nothing for the owner to do, beyond giving RealmDex the URL.

## Checks

- RealmDex stats endpoint: account server builds (Release, no warnings); on an isolated stack (slot 7) `GET /realmdex/stats` answered `200`, `text/plain; charset=utf-8`, body `0`, matching `app/status`'s `players`. The Caddy rewrite isn't tried locally (no Caddy here): check `https://dodmg.com/realmdex/stats` after the deploy.
