Release 2.4.5: fixes the deploy of 2.4.4 (the move to play.dodmg.com). Deploys everything on both machines; players are already offline for the move and come back with it.

## For players

- **Servers:** Europe 2 at play.dodmg.com comes up (2.4.4's deploy stopped before starting it).

## Details

### bsk.env quoting

- `deploy/deploy.py`: `BSK_GATEWAY_FLAGS` is written in quotes. With 2.4.4 it holds several words (`--forward-address --allow 169.58.244.146:2051`, the gateway reaching Testing), and install.sh sources `bsk.env` with bash, which ran `--allow` as a command: 2.4.4's deploy stopped on the game machine before starting Redis, the account server, the world servers or the gateway (`/opt/bsk/bsk.env: line 7: --allow: command not found`). systemd's `EnvironmentFile` strips the quotes, and `$BSK_GATEWAY_FLAGS` in the gateway unit still splits into words.
- Redeploy: everything (2.4.4's deploy didn't finish). No client change, so no client release. Nothing in Redis changes (the data copied for the move is in place on the game machine).

## Checks

- bash (`set -a; . bsk.env`) and systemd (`systemd-run -p EnvironmentFile=`, on the game machine) both read the quoted line as the three words `--forward-address`, `--allow`, `169.58.244.146:2051`; `py_compile` of deploy.py.
