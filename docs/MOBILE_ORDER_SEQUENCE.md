# Order-sending sequence from the mobile app (thinkorswim)

This document shows, step by step, the real flow a trader follows when sending an options order from the broker's mobile app (thinkorswim by Schwab). It's the same flow described in the **"Project origin and motivation"** section of the [`README.md`](../README.md): from the moment the trader decides to enter until the order is actually sent to the market, **no less than 20 seconds** pass — enough time for the price to move significantly relative to what was observed at the moment of the decision.

This is precisely the reason for being of this project's **Windows App**: to automate and compress this same flow to **under 3 seconds**.

📹 **Video of the complete sequence (~20 seconds):** [Watch on Google Drive](https://drive.google.com/file/d/1nJOHh_ZM6y3UUC_i5ikqjJbbLqsSNDmv/view?usp=drive_link)

---

## Step by step

### 1. Mobile home screen
Locate the thinkorswim app icon among the other apps on the phone.

<img src="images/mobile/01-home-screen.png" alt="Home screen" width="280">

### 2. App login
Authentication with Login ID and Face ID.

<img src="images/mobile/02-login.png" alt="Login" width="280">

### 3. Account selection
Choose the account to trade on (paperMoney or Live Trading), among the linked accounts.

<img src="images/mobile/03-select-account.png" alt="Account selection" width="280">

### 4. Account overview
Review the account status (positions, Net Liq) before trading.

<img src="images/mobile/04-account-overview.png" alt="Account overview" width="280">

### 5. Symbol search
Search for the ticker to trade (for example SPY) in the symbol search.

<img src="images/mobile/05-symbol-search.png" alt="Symbol search" width="280">

### 6. Symbol detail and option chain
View the underlying's spot price, Bid/Ask and IV, and the available expiration dates.

<img src="images/mobile/06-spy-detail.png" alt="SPY detail" width="280">

### 7. Strike selection
Choose the strike and option type (Call/Put) within the option chain.

<img src="images/mobile/07-option-chain.png" alt="Option chain" width="280">

### 8. Order setup
Configure order type (Limit/Market), quantity, and review the estimated trade cost.

<img src="images/mobile/08-order-entry.png" alt="Order setup" width="280">

### 9. Final adjustment before review
Confirm quantity, market order type, and account before moving on to review.

<img src="images/mobile/09-order-entry-market.png" alt="Market order" width="280">

### 10. Simulated order confirmation
Confirmation screen with full detail: trade cost, break-even, max profit/loss, and effect on buying power.

<img src="images/mobile/10-order-confirmation.png" alt="Order confirmation" width="280">

### 11. Order executed (Filled)
Verify in the day's order history that the order was executed (Filled) and at what price.

<img src="images/mobile/11-order-filled.png" alt="Order executed" width="280">

### 12. Post-trade account summary
Review the account summary (day's P/L, active positions, orders) after trading.

<img src="images/mobile/12-account-summary.png" alt="Account summary" width="280">

---

## Comparison with the Windows App

| | Mobile app (thinkorswim) | Windows App (this project) |
|---|---|---|
| Manual steps | Login → account → symbol search → option chain → order setup → review → send → verification | A single click on the strike row in the quotes grid |
| Approximate time | ~20 seconds | < 3 seconds |
| Slippage risk | High — the price can move considerably during the manual process | Minimal — the order is sent and confirmed almost instantly |

See also [`docs/FEATURES.md`](FEATURES.md) and [`docs/USER_GUIDE.md`](USER_GUIDE.md) for the detail of the equivalent flow in the Windows App.
