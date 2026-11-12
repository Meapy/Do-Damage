Release 2.5.4

## For players

## Details

### Player sprites are a release check

`tools/community_sprites.py check` (new) fails while sprites admins approved in the Sprite Editor wait on the server without being taken into the repository (it lists them), or while the community sheets, the catalog and the item XML are out of step: a catalog cell empty or missing, an item no longer in the XML, or an item no longer pointed at its player sprite or credited (`<SpriteBy>`), as a regeneration could leave it. `--offline` checks only the repository; when the server can't be reached it fails and says so rather than passing. `rework_file` gained `write=False` to tell what it would change. AGENTS.md's "Releasing" has it as a new step 1, before the changelog section is named (`apply` writes under Unreleased); client-rs/AGENTS.md and SETUP-LOCAL.md point to it. Tooling and docs only: nothing to redeploy.

- At the time of writing the live server has one approved sprite waiting: #2, a new look for the April Fools Skin Bag by Pillow. This release's step 1 takes it in.

## Checks

- **Player sprites are a release check:** `check --offline` passes on the clean repository; against https://dodmg.com/api it fails, listing #2. With a test sprite applied it passes; it fails once the server XML is put back without it (`rework` named), and again once the sheet's cell is wiped. Test changes undone afterwards.
