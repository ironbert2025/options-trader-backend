# Live Chart (WebView2 + Lightweight Charts + Schwab Streaming)

Reference document for this feature, developed entirely on the `feature/trade-pnl-min-max` branch. Goal: so that anyone (or myself, in another session) can pick it back up without rereading the whole commit history.

> This document replaces the previous version (which described the initial state, not validated against real traffic). Everything below is already implemented and in use.

---

## What it is

A **live** candlestick chart of the underlying (spot, e.g. SPY/QQQ/TSLA/AAPL/DIA/IWM), fed by a direct WebSocket stream to Schwab — **completely isolated** from the rest of the app (it doesn't touch Quotes polling, trading, or any existing logic). It opens via the **"Live Chart"** button on the Quotes tab (popup window, `MultiChartForm`, 3 panels). There's also a **Charts tab embedded in Form1** (`TwoPanelChartsControl`), always available without opening a separate window, with only 2 of the 3 panels (1h and 15m RTH) but its own trades/options grid and trade-mode radios — see [`LIVE_CHART_ANALYSIS.md`](../LIVE_CHART_ANALYSIS.md) for details.

## 1. The 3 panels

A single `MultiChartForm` contains **3 `ChartPanel`** side by side (horizontal):

| Panel | Mode (`ChartPanelMode`) | Interval | Session |
|---|---|---|---|
| **1h** | `Hourly15` | 1-hour candles | Regular (RTH), 9:30 AM - 4:00 PM ET |
| **15m RTH** | `Fifteen_RTH` | 15-min candles | Regular (RTH), 9:30 AM - 4:00 PM ET |
| **15m RTH+Overnight** | `Fifteen_Full` | 15-min candles (toggle to 5 min) | Regular + pre/after-hours |

Each `ChartPanel` adds ticks to its own bucket (1h/15m) independently, in memory (`_liveBucket`/`_liveBucketIndex`/`_liveAnchor`), without touching the network again — a single data stream feeds all 3.

## 2. One Schwab connection, multiple instances of the program

Schwab allows **only one streaming connection per account**, but the trader runs **one instance of the program per ticker** (each with its own grid/chart). This is solved with a **local hub** (`OptionsTrader.WinForms/LocalCandleHub.cs`):

- The first instance to start **binds the fixed port `51919`** (`CandleHubServer.TryStart`, `IPAddress.Any`) and becomes the **"hub"**: it's the only one that opens the real connection to Schwab (`SchwabStreamerClient`), and it **rebroadcasts** each candle/tick as newline-delimited JSON to whoever is connected.
- Any other instance (on the same PC, or on **another PC on the same LAN**) that fails to bind the port connects as a **client** (`CandleHubClient`) to the hub — same port, `IPAddress.Any` accepts local (`127.0.0.1`) and remote (LAN IP) connections simultaneously.
- **Access from another PC**: **"Hub Host"** button on Quotes — saves the remote hub's IP (`HubHostSettingsStore` → `%AppData%\OptionsTrader\hubhost.json`). If configured, that instance connects directly to that IP as a client, without trying to become a hub. Requires opening port 51919 in the firewall of the PC acting as hub.
- `ICandleFeed` (`OptionsTrader.Application/Interfaces/ICandleFeed.cs`) abstracts the source — `ChartPanel` doesn't need to know whether it's receiving the real connection (`SchwabStreamerClient`) or a relay (`CandleHubClient`).
- Automatic reconnection with backoff if the hub goes down; clients retry every 5s indefinitely.

This mechanism was built in `b759645` and extended to LAN in `507cd5f`.

## 3. Price sources: `CHART_EQUITY` vs `LEVEL_ONE_EQUITIES`

When validating against real traffic, it was detected that the chart didn't exactly match ThinkorSwim. Investigating (comparing `TickPriceStore` against the near-continuous spot from the options chain) confirmed: average absolute difference ~$0.32, max ~$0.83 on SPY (~$740) — a structural deviation, not a bug.

**Cause**: `CHART_EQUITY` only pushes **one 1-minute bar** per symbol — the `Close` that arrives at a given moment may reflect a price from several seconds earlier within that minute, not the latest real trade.

**Solution** (`9666309`): also subscribe to `LEVEL_ONE_EQUITIES` (last real quote, much higher frequency — several times per second):

- `CHART_EQUITY` remains the owner of **Open/High/Low and each candle's boundaries** (where a bucket starts/ends).
- `LEVEL_ONE_EQUITIES` only updates the **`Close` of the candle CURRENTLY FORMING** (`Streamer_OnLevelOneTick` in `ChartPanel.cs`), so the displayed price tracks the last real trade without waiting for the 1-minute bar to close.
- Fields used (assumed based on Schwab's public documentation, **not yet confirmed against real traffic** — unlike `CHART_EQUITY`, which was validated with `ws_raw.log`): `"3"` = Last Price, `"35"` = Trade Time. If the price looks like 0 or odd on the chart, check `ws_raw.log` — the code already guards against prices ≤ 0 (they never reach the chart, but they are still saved to the raw file).
- **Raw data from both sources is saved separately**, precisely so that tomorrow it can be compared which one tracks the real price better:
  - `TickPriceStore` (existing) — 1 row/minute, derived from `CHART_EQUITY`. `C:\OptionsData\MarketData\Ticks\{Symbol}\{Symbol}_Ticks_{yyyyMMdd}.csv`.
  - `LevelOneTickStore` (new) — every `LEVEL_ONE_EQUITIES` tick, milliseconds. `C:\OptionsData\MarketData\TicksLevelOne\{Symbol}\{Symbol}_L1Ticks_{yyyyMMdd}.csv`.
- Relayed by the local hub (`CandleHubServer.BroadcastLevelOne` / `CandleHubClient.OnLevelOneTick`) — all instances benefit, not only the one holding the real connection. An instance reading from a **remote** hub (another PC on the LAN) also writes its own local `TickPriceStore`/`LevelOneTickStore` (previously only the hub instance wrote them) — necessary so the Simulator/"Sim 4 ETF" have data to replay on that machine.

## 4. Time-zone handling (resolved, don't touch without reason)

Lightweight Charts displays the Unix timestamp you pass it as **literal UTC digits** — it doesn't convert to the browser's local time zone. Solution: `CandleData.Time` is always stored in **real UTC**; right before sending it to the JS side (`ChartPanel.FakeUtcEpochSeconds` / `ToChartJson`), it's converted to **New York (Eastern)** time via `TimeZoneInfo`, and that value is "disguised" as UTC so the chart displays it as-is — so it always shows in NY time, regardless of what time zone the PC is configured with. Any new time passed to `chart.html` (lines, marks, etc.) must go through this same trick.

## 5. Candle aggregation

Schwab's `pricehistory` (REST, history) only returns 1-minute candles — `ChartPanel.AggregateToInterval` groups them into 15- or 60-minute buckets on the client side (C#), anchored at 9:30 AM ET for RTH, or midnight ET for the full-day panel. Live aggregation (`Streamer_OnNewCandle`) uses the same bucket logic so boundaries always match between history and live candles.

**Daily view** (1h panel, "Daily" button, opens `DailyChartForm`): aggregates up to ~200 days of hourly candles into daily candles (`HourlyCandleStore.MaxCandles = 1500` ≈ 200 days × 7 candles/day, also separately backed up in `DailyCandleStore` → `C:\OptionsData\MarketData\Candles\{Symbol}_Daily.csv`), recalculating the 4 SMAs over daily closes. Hourly history backed up in `C:\OptionsData\MarketData\Candles\{Symbol}_Hourly1h.csv`, initially backfilled from Yahoo Finance (script `backfill_hourly.js`, not versioned in the repo — it lived in the session scratchpad) to get past Schwab's 10-day limit. It's not just a read-only view: it has its own T-Line tool (mirrored to the Live Chart), "D.PM"/"D40"/"D100"/"D200" checkboxes (draw the corresponding daily SMA over the Live Chart) and "SMA Watch" buttons (arm watches for the daily SMA crossing the live price of the 1h panel) — see [`LIVE_CHART_ANALYSIS.md`](../LIVE_CHART_ANALYSIS.md).

## 6. Indicators

- **SMA 20/40/100/200** — 1h panel, calculated in JS (`configureSmas`). No hover markers (`crosshairMarkerVisible: false`).
- **Bollinger Bands (20, 2 std devs)** — 1h and 15m RTH panels (`configureBollinger`).
- **Cross-SMA monitors (manual)** — **removed from the live Live Chart**; the same toggle logic (↑/↓ × 20/40/100/200) and Telegram push still exists, but only in the **Simulator** (`SimulatorForm.cs`/`SimulatedChartPanel.cs`), with no equivalent in `MultiChartForm`.
- **Red previous-day High/Low lines** — auto-drawn when the chart opens, on all 3 panels (`ChartPanel.EvaluatePrevDayHiLoAsync`/`DrawPrevDayHiLoAsync`); one side is skipped if the price already gapped past it. Deleting an H-Line is synced across the 3 panels (`OnHLineDeletedEvent`).
- **"Exposed on 3 charts" banner** — in premarket, if the live price breaks the same side of Bollinger(20,2) on Daily + 1h + 15m RTH simultaneously, a banner appears above the 15m RTH panel (`ChartPanel.GetBollingerDirection`/`GetDailyBollingerDirection`, orchestrated in `MultiChartForm`).

## 7. Drawing tools

All implemented as *Series Primitives* of Lightweight Charts v4 in `chart.html` (there's no native series type for this):

| Tool | Panel(s) | Notes |
|---|---|---|
| T-Line | 1h, 15m RTH (panel 3 no longer has this tool) | Persisted per symbol in **both** panels, each with its own `TLineStore` (tag "1h"/"RTH"); multiple independent lines per panel, no 1-line limit; no longer mirrored between panels |
| H-Line | 1h, 15m RTH | Red line to the right edge; same tool reused on both panels |
| Rect (blue) | 15m RTH+Overnight | Rectangle via 2 clicks |
| Rect (gray) | 1h | For marking sideways movement |
| DZ/SZ | 15m RTH+Overnight | Demand/supply zones, filled between pairs |
| Arrow (diagonal) | 15m RTH+Overnight | Red if the 1st click is higher than the 2nd, green otherwise |
| Vertical arrows (↑/↓) | 1h | Tip at the click point; draggable; persisted per symbol (`VerticalArrowStore`) |
| T-Line (Daily popup) | `DailyChartForm`, "Hourly"/"15 Min" tabs | Automatically mirrored to the corresponding 1h/15m RTH panel of the Live Chart (one-way only) |
| Rect (Daily popup) | `DailyChartForm` | Persisted per symbol (`RectStore`, contextTag "Daily"/"DailyColor") — distinct from the blue Rect in the Live Chart |

**Selectable/deletable pattern** (gray, blue, T-Line, vertical arrows): clicking near the edge/line selects it (yellow outline), the `Delete` key deletes the selected one. `Clear` (per panel) deletes everything drawn on that panel — and on the 1h panel it also clears the persisted stores.

**Persistence** (`TLineStore`, `VerticalArrowStore`, both in `OptionsTrader.WinForms`): simple CSV per symbol in `C:\OptionsData\ChartDrawings\{Symbol}\`, no database. Chart→C# communication via `window.chrome.webview.postMessage` → `CoreWebView2.WebMessageReceived` in `ChartPanel.cs`.

## 8. Pre-market blue line (15m RTH panel)

When "Live Chart" is opened **before 9:30 AM ET**, a blue line starts at the moment of the click, following the live price (`startPreMarketLine` / `updatePreMarketLine` in `chart.html`) until the market opens — at which point C# simply stops sending updates, so it freezes on its own, with no extra "freeze" logic. **Not persisted to disk** — closing and reopening the chart (that day or the next) restarts the whole process from scratch. If opened after 9:30, nothing appears.

## 9. Local snapshot of the 3 charts per trade

When a trade is recorded (demo or real, single convergence point: `Form1.RecordEntryAsync`), if a `MultiChartForm` is open for that symbol, the 3 panels are captured via `CoreWebView2.CapturePreviewAsync` (renders the actual chart, **not** a screen capture — works even if the window is minimized or occluded), combined side by side in the same order they appear on screen, and saved to `C:\OptionsData\ChartSnapshots\{Symbol}\{Symbol}_{timestamp}_trade{tradeId}.png`. Local only — doesn't upload to S3 or touch the database. Best-effort: never blocks the trade flow.

## 10. Other related windows

- **"Block Mov"** (`FourEtfChartsForm.cs`): window with 4 1h charts (SPY, QQQ, DIA, IWM) side by side, no toolbar — for watching overall market movement. DIA/IWM are added manually to the subscription list (`Form1.SetUpLiveFeedAsync`), pending removal from the Tickers table like the others.
- **"Sim 4 ETF"** (`FourEtfSimulatorForm.cs`): unlike "Block Mov" (above, live), this is an **offline replay** window (disk-based, no streaming) — SPY/QQQ/IWM/DIA in a 2x2 grid, 15m RTH+Overnight, shared Play/pause, a single DZ/SZ toggle for all 4 charts, and an options-chain grid with a symbol selector.

## 11. Files involved

- **`OptionsTrader.Application/DTOs/Streaming/CandleData.cs`** — DTO `{Time (UTC), Open, High, Low, Close}`.
- **`OptionsTrader.Application/Interfaces/ICandleFeed.cs`** — `OnNewCandle`, `OnLevelOneTick`, `OnDisconnected`.
- **`OptionsTrader.Infrastructure/Schwab/SchwabStreamerClient.cs`** — hand-built WebSocket client. `ConnectAsync`/`LoginAsync`/`SubscribeChartEquity`/`SubscribeLevelOneEquity`, message parsing, reconnection with backoff. `LogRawMessage` dumps all raw traffic to `C:\OptionsTraderPush\ws_raw.log` to validate the format against real Schwab traffic.
- **`OptionsTrader.Infrastructure/Schwab/TickPriceStore.cs`** / **`LevelOneTickStore.cs`** — raw tick capture (see §3).
- **`OptionsTrader.WinForms/ChartPanel.cs`** — embeddable `Panel` with the WebView2: history loading, candle aggregation (historical and live), indicators, drawing, image capture (`CaptureImageAsync`).
- **`OptionsTrader.WinForms/MultiChartForm.cs`** — container window, assembles the 3 `ChartPanel`s, per-column toolbar, combined capture (`CaptureCombinedChartImageAsync`).
- **`OptionsTrader.WinForms/LocalCandleHub.cs`** — `CandleHubServer`/`CandleHubClient` (see §2 and §3).
- **`OptionsTrader.WinForms/HubHostSettingsStore.cs`**, **`TLineStore.cs`**, **`VerticalArrowStore.cs`**, **`HourlyCandleStore.cs`**, **`DailyCandleStore.cs`**, **`RectStore.cs`**, **`SmaDailyWatchStore.cs`**, **`CtRecordStore.cs`**, **`CtLogWriter.cs`** — local persistence (see corresponding sections).
- **`OptionsTrader.WinForms/DailyChartForm.cs`** — "Daily" window (daily candles, its own mirrored T-Line, D.PM/D40/D100/D200 checkboxes, SMA Watch buttons).
- **`OptionsTrader.WinForms/TwoPanelChartsControl.cs`** — Charts tab embedded in Form1 (2 panels, its own trades/options grid).
- **`OptionsTrader.WinForms/FourEtfChartsForm.cs`** — "Block Mov" window.
- **`OptionsTrader.WinForms/FourEtfSimulatorForm.cs`** — "Sim 4 ETF" window (offline replay, distinct from "Block Mov").
- **`OptionsTrader.WinForms/ChartAssets/`** — `lightweight-charts.js` (v4.1.3, local, no CDN) + `chart.html` (all the chart's JS: indicators, drawing, lines, Daily view).
- **`Form1.cs` / `Form1.Designer.cs`** — `btnLiveChart`, `btnFourEtfCharts`, `btnHubHost` buttons; `SetUpLiveFeedAsync` (hub/client selection, subscriptions); `RecordEntryAsync` (trade snapshot).

## 12. What this feature does NOT touch

- The 6s polling of the Quotes tab, `PopulateQuotesGrid`, `FetchAndUpdateQuotesAsync`.
- Any trading logic (`PlaceRealTradeAsync`, `CloseTradeRowAsync`, etc.) — the chart snapshot is added *after* the trade has already been saved, without altering its flow.
- The existing OAuth2 authentication (`SchwabAuthService`) — reused as-is, unchanged.

## 13. Pending / future ideas

1. Confirm the `LEVEL_ONE_EQUITIES` field numbers (`3`, `35`) against `ws_raw.log` with real traffic — compare `TickPriceStore` vs `LevelOneTickStore` vs the real price (e.g. ThinkorSwim) to decide whether the live `Close` should be based 100% on L1.
2. Move DIA/IWM from "added manually" to the real Tickers table.
3. ~~Offline simulator (phase 2) over captured ticks~~ — implemented: see the individual Simulator (`SimulatorForm.cs`) and "Sim 4 ETF" (`FourEtfSimulatorForm.cs`, §10), documented in detail in [`SIMULATOR_TELEGRAM_AND_LOGGING.md`](SIMULATOR_TELEGRAM_AND_LOGGING.md).
