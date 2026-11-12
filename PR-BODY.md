Release 2.5.4

## For players

## Details

### Player sprites are a release check

`tools/community_sprites.py check` (new) fails while sprites admins approved in the Sprite Editor wait on the server without being taken into the repository (it lists them), or while the community sheets, the catalog and the item XML are out of step: a catalog cell empty or missing, an item no longer in the XML, or an item no longer pointed at its player sprite or credited (`<SpriteBy>`), as a regeneration could leave it. `--offline` checks only the repository; when the server can't be reached it fails and says so rather than passing. `rework_file` gained `write=False` to tell what it would change. AGENTS.md's "Releasing" has it as a new step 1, before the changelog section is named (`apply` writes under Unreleased); client-rs/AGENTS.md and SETUP-LOCAL.md point to it. Tooling and docs only: nothing to redeploy.

- `apply` skips a new look that is the item's sprite as it is now, pixel for pixel once scaled to the drawing's size (`unchanged`, reading the item's sheet through the client's manifest), and `check` names it as one to turn down in the Review tab: taking it in would change nothing players see and credit the player for someone else's art.
- `apply` keeps a blank line under `## For players` when it writes the first line of an empty section.
- **The owner must:** turn down #2 in the Sprite Editor's Review tab. It's the only approved sprite on the live server: a "new look" for the April Fools Skin Bag that is its existing 8×8 sprite doubled to 16×16, with no pixel changed (the item's description credits Seelpit for its art). Until it's turned down, `check` fails.

## Checks

- **Player sprites are a release check:** `check --offline` passes on the clean repository; against https://dodmg.com/api it fails, listing #2. With a test sprite applied it passes; it fails once the server XML is put back without it (`rework` named), and again once the sheet's cell is wiped. Against the live list, `apply` skips #2 as the bag's sprite as it is now and `check` names it to turn down; a changed test sprite still goes in, with a blank line kept under an empty `## For players`. Test changes undone afterwards.
