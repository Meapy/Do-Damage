Release 2.4.8: Dagger of Divine Wrath and Wand of Absolution show the right sprites (deploys the client; nobody is disconnected)

## For players

- **Items:** the Dagger of Divine Wrath and the Wand of Absolution, and their shots, showed each other's new art; each now shows its own.

## Details

### T15 dagger and wand sprites swapped back

- 2.4.7's hand-drawn `BladesAndT15s8x8.png` had the dagger's and the wand's rows the wrong way round (icon and shot, cells 34/35 and 70/71). The two rows are swapped in the image; the XML is unchanged (the Dagger of Divine Wrath still uses cells 34/35, the wand 70/71).
- Redeploy: client only (the sheet). Nobody is disconnected; nothing in Redis changes.

## Checks

- Dagger and wand: the sheet's T15 column rendered after the swap (row 2 the dagger and its shot, row 4 the wand and its). Not looked at in game.
