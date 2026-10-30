---
title: Marketplace
nav: 7
sprite: chest
eyebrow: Players selling to players
summary: The Marketplace in the Nexus: listing items from your inventory or vault for fame, browsing and buying, the market square's stalls, the tax and limits, and your box and history.
---
The **Marketplace** is where players sell items to each other for fame. You list an item at your
price; anyone can buy it, even while you're offline, and the fame is yours the moment it sells.

## Finding it

Cross the east bridge from the Nexus spawn into the village; the **market square** is south-east
of it, past the cross street. The **Market Broker** stands at its head, and greets you with how much is for sale (or how many of your items are) as you come by: stand by it and press **View** (or <kbd>0</kbd>) to open the Marketplace window. `/market`
takes you to the Broker from anywhere.

Either side of the square, two pavilions of **stalls** show what players are selling, the newest
first. Walk onto a stall to see its item, its price and how long it has left, and buy it there as
you would at a shop. With more listings than stalls, the stalls take turns every 30 seconds; the
window always has them all.

The Marketplace works in the Nexus. Guests can't use it until they register.

## Buying

The window's **Browse** tab lists everything for sale:

- **Search** by an item's name or a seller's name.
- **Filter** by kind (weapons, abilities, armor, rings, other) and by tier (UT, T12 and up, T8 to
  T11, T7 and below).
- **Sort** by newest, cheapest, dearest, or ending soon.
- Hover over an item for its full tooltip, imprints included.

Click **Buy**, then **Confirm**. The price comes off your fame and the item goes into your
inventory. If your inventory is full, it waits in your **box** (below), so a purchase is never
lost. If someone else bought it first, or its seller took it back, the window says so and you pay
nothing.

## Selling

On the **Sell** tab, pick where the item comes from, your **Inventory** or your **Vault** (you
needn't go there), and click it. Then:

- set a **price** in fame, up to {{v:market_max_price}};
- choose how long it lasts: **1, 3 or 7 days**;
- click **List on the Marketplace**.

The item leaves your inventory or vault at once and is held by the Marketplace until it sells,
ends or you take it back. The window shows what you'll get: the Broker keeps
**{{v:market_tax}}%** of each sale as its fee. You can have **{{v:market_limit}} listings** at a
time.

`/sell <slot> <price> [days]` lists from your inventory without opening the window: slots 1-8,
9-16 for the backpack, for {{v:market_default_days}} days unless you say (1 to
{{v:market_max_days}}).

Untradeable items can't be listed (their tooltips say "Untradeable").

**When it sells**, the fame is added to your account straight away, whether you're online or not.
You get a message in the chat, or when you next come to the Nexus if you were away.

## My listings, the box and the history

- **My listings** shows what you're selling, its price, what you'll get and the time left. **Take
  back** returns an item to your inventory.
- The **box** holds items waiting for you: bought with a full inventory, taken back with a full
  inventory, or **listings that ended without selling**. Each keeps its imprints. **Collect** one,
  or **Collect all that fit**. The Nexus reminds you when something is waiting.
- The **history** lists your last 50 Marketplace events: what you listed, sold (and to whom),
  bought, took back and what ended unsold.

## Good to know

- The price you see is the price you pay: a seller can't change a listing's price after you've
  seen it. To change your price, take the item back and list it again.
- You can't buy your own listings.
- Fame from sales counts towards your account's fame like any other.
