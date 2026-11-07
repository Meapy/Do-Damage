Release 2.4.3. Deploys the world server; the website and wiki update too. Nobody is disconnected.

## For players

- **Pets:** the pets realm and godland monsters drop are four times rarer: the realm's critters 0.0375-0.125% a kill (were 0.15-0.5%), the gods' and quest monsters' pets 0.125% (were 0.5%), the Abyssal Kraken's, the Wyrm Matriarch's and the Sumo Master's Baby 0.25% (were 1%) and the Lucky Ent God's Leprechaun 0.625%. Realm events, dungeons and the minigames drop theirs as before.
- **Website:** dodmg.com and its wiki are easier for Google and other search engines to find ("dodmg", "RotMG private server"): a sitemap of every page, canonical addresses, and a preview picture when a link to the site is shared on Discord and elsewhere. The gallery's Nexus and character screen shots are new: the island Nexus (was the Halloween castle plaza) and the character list with its search and filters. The home page is freshened up: the game in numbers under the Play buttons (69 dungeons, 29 classes, 706 pieces of gear, 6 Oryx stages, 11 languages), feature tiles for what the game has now (pets, imprints and the Collection, the Divine Forge, community goals, languages and crosshairs), long patch notes folded under "Show all", the ping, loot-bag, quest-log and vault keys, and questions on getting started and languages.

The wiki is updated with these changes.

## Details

### Realm and godland pet drops cut by 75%

Every pet generator in the lands' behaviours (`BehaviorDb.Shore.cs`, `Lowland.cs`, `Midland.cs`, `Highland.cs`, `Mountain.cs`) and in Realm2's (`BehaviorDb.Realm2.cs`, the `R2Mob` and `R2Quest` extras) drops at a quarter of its chance: 56 lines. Left as they were: the realm events (the Beer God in `Highland.cs`, and the events' own files: CubeGod, SkullShrine, Sphinx, Pentaract, LotLL, Hermit, RockDragon, GhostShip, Crystal, Cyclops, Woodland's Megamoth), the dungeons, and the pets in `DoomedExtraLoot` (Birdman Chief's Eagle, the minigames' sheep, chicken and turkey). The wiki reads the rates from the behaviours. World server only; no data in Redis changes.

### Website: search engines

- **What:** `tools/build_site.py` has `--url` (default `https://dodmg.com/`, filling `{{URL}}`) and `for_search`, which adds `<link rel="canonical">` and `og:url` to every built page and writes `sitemap.xml` (91 pages: home, changelog, wiki and gear pages) and `robots.txt` (`/api/` and `/downloads/` disallowed, the sitemap named). The home page's title and description name "dodmg" and "RotMG private server", it has Open Graph and Twitter card tags with the Oryx screenshot, and JSON-LD for the site (`WebSite`, alternate names dodmg / DoDmg / dodmg.com) and the game (`VideoGame`). The tagline and footer say "RotMG" and "dodmg.com"; the wiki home's title and the changelog's description name it as a RotMG private server.
- **Why:** searching "dodmg" or "rotmg private server" didn't find the site: it had no sitemap or robots.txt, the pages barely used the words people search for, and the site is only weeks old on its domain.
- **Files:** `tools/build_site.py`, `tools/build_wiki.py` (wiki home title), `website/index.html`, `website/changelog.html`, `deploy/deploy.py` (passes `--url https://<site>/` to both site builds), `SETUP-LOCAL.md`, changelog.
- **Deploy:** website only; nobody is disconnected.
- **Owner to do:** add dodmg.com to [Google Search Console](https://search.google.com/search-console) as a Domain property (verify with the TXT record it gives, at Register365), submit `https://dodmg.com/sitemap.xml` and request indexing of the home page; the same at [Bing Webmaster Tools](https://www.bing.com/webmasters) (which can import from Search Console). Links to the site from elsewhere (RotMG private server lists, r/RotMGPrivateServers, the Discord's invite pages, the GitHub repository's website field) are what move it up for "rotmg private server".
- **Not done:** no lastmod dates in the sitemap (the build doesn't know when a page last changed); the browser client at `/play/` isn't in the sitemap.

### Website: new Nexus and character screen shots

- **What:** `website/screenshots/nexus*.webp` and `characters*.webp` retaken with the Rust client's `--shot` on an isolated stack (the test account "Tester", admin there only; its own `APPDATA` so the fps counter was off and no saved login was used): the island Nexus with an item tooltip, and the character list (v2.4.2). Alt text and the Nexus caption updated in `website/index.html`. The Oryx, Godlands, Cnidarian Reef and Fungal Cavern shots are kept: they still show those places, with the HUD as it was in October.
- **Deploy:** website only.

### Website: home page polish

- **What:** `website/index.html`: a row of figures under the hero's buttons (`{{DUNGEONS}}`, `{{CLASSES}}` and `{{GEAR}}`, counted by `build_wiki.build`, which now returns them and runs before the pages are filled in; gear counts only what players can get: tiered, untiered, Legendary, Ascended and Ethereal, 706 of the 1,214 items); the feature tiles rewritten for today's game (10 tiles; three new sprites in `site.js`: paw, gem, banner, globe); the Controls list gains middle mouse (pings), Shift (loot bags), J and K; the FAQ is two columns with two new questions (where to start, languages). `site.js` folds a release taller than 520 px on the home page under a fade with a "Show all" / "Show less" button (without JavaScript it shows in full). `style.css`: `.facts`, `.release.folded` and `.more`, the two-column `.faq`, and narrow-screen rules for them, the wide tiles and the keys.
- **Files:** `website/index.html`, `website/site.js`, `website/style.css`, `tools/build_site.py`, `tools/build_wiki.py`, changelog.
- **Deploy:** website only.
- **Not done:** the four older gameplay screenshots keep the October HUD.

## Checks

- Realm and godland pet drops: `dotnet build source\WorldServer\WorldServer.csproj -o build\wscheck` (0 errors); `tools/build_wiki.py` into a scratch folder, the Pets page showing the new rates (Brown Pup from the Hobbit Rogue at 0.037%).
- Website search engines: `tools/build_site.py --out <scratch>`: `robots.txt` and `sitemap.xml` written (91 URLs), canonical and `og:url` on the home, changelog, wiki and gear pages (`https://dodmg.com/wiki/classes/adept.html`), the home page's JSON-LD parses, no `{{...}}` left.
- Website shots: built the site and looked at the gallery in a browser (new Nexus and character screen, the rest unchanged); the Nexus WebP is 253 KB (was 173 KB), the thumbnail 63 KB.
- Home page polish: built the site and looked through every section in a browser at desktop width and at 375 px (no sideways scroll; the figures wrap to two rows), clicked Show all and Show less on the patch notes, and checked a wiki page still renders.
