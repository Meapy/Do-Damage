Release 2.6.6: 

## For players

### Gold Store
- PayPal payments for gold name the account the gold goes to: the item reads "1,000 Gold for <name>" on the receipt and in PayPal.

## Details

### PayPal payments name the buyer's account
- **What and why:** the owner asked for the in-game name in the PayPal payments, to tell at a glance who bought gold. It was only in the order's description, which PayPal's activity list and payment emails don't show. Now the item's name is "<gold> Gold for <name>" and the invoice id is `DD-<name>-<request id>` (still unique per order). The account server's "Gold: paid" log line names the account too, not just its id.
- **Files:** `source/App/PayPal.cs` (`CreateOrder`), `source/App/Controllers/GoldController.cs` (log line).
- **Deploys:** the account server only; nobody is disconnected. Orders made before keep their old wording; checking a payment (`custom_id`, amount, currency) is unchanged. No Redis change.

## Checks

- PayPal names: `dotnet build source\App\App.csproj` (0 errors), `dotnet test source\Tests --filter GoldStore` (9 passed). Not tried against PayPal's sandbox.
