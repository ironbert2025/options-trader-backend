# Plan: live options via WebSocket (`LEVELONE_OPTIONS`)

Research + plan to add a "WebSocket" mode to the OptionsChain grid, as an alternative to the
current REST polling (interval configurable per symbol, 6s by default — see
`TickerSettingsStore.PollingIntervalSeconds`). The number of strikes per side (`strikeCount`) is
also configurable per symbol (`TickerSettingsStore.StrikeCount`, default 40) — any REST fallback
size planned for this WebSocket mode should use that value instead of a fixed number.
**Not implemented yet** — this document is the reference for when it gets started.

---

## Research (confirmed against real code from two different SDKs)

### Service name
**`LEVELONE_OPTIONS`** — no underscore between LEVEL and ONE (same type of mistake we had with
`LEVEL_ONE_EQUITIES` vs `LEVELONE_EQUITIES`, see [`docs/LIVE_CHART_STREAMING.md`](LIVE_CHART_STREAMING.md) —
confirm again against `ws_raw.log` when this is implemented, don't assume this name is 100%
verified in production yet).

### Subscription symbol format (different from the normal ticker)
It's not "SPY" — it's a specific contract identifier with fixed padding:
```
{Underlying, padded to 6 chars with spaces}{YYMMDD}{C|P}{Strike padded to 8 digits}
```
Real examples confirmed in code: `"GOOGL 240712C00200000"`, `"AAPL 240517P00190000"` (200.00 → `00200000`).

### Field mapping (LEVEL_ONE_OPTION)
| Field | Index | Field | Index |
|---|---|---|---|
| symbol | 0 | strikePrice | 24 |
| description | 1 | contractType (C/P) | 25 |
| bidPrice | 2 | underlying | 26 |
| askPrice | 3 | expirationMonth | 27 |
| lastPrice | 4 | timeValue (≈ExtrinsicValue) | 29 |
| totalVolume | 8 | expirationDay | 30 |
| openInterest | 9 | dte (days to expiration) | 31 |
| volatility (IV) | 10 | **delta** | 32 |
| quoteTime | 11 | **gamma** | 33 |
| tradeTime | 12 | **theta** | 34 |
| intrinsicValue | 13 | **vega** | 35 |
| bidSize/askSize | 20/21 | rho | 36 |
| netChange | 23 | underlyingPrice | 39 |

Covers 1:1 all the fields already used by `OptionQuoteDto` (Bid, Ask, SpotPrice, IntrinsicValue,
ExtrinsicValue, Delta, Gamma, Theta, Vega, IV).

**Sources:**
- [Schwabdev stream.py — GitHub](https://github.com/tylerebowers/Schwabdev/blob/main/schwabdev/stream.py)
- [Schwabdev stream_demo.py — GitHub](https://github.com/tylerebowers/Schwabdev/blob/main/docs/examples/stream_demo.py)
- [schwab-py streaming docs](https://schwab-py.readthedocs.io/en/latest/streaming.html)
- [schwab-td-ameritrade-streamer index.d.ts — GitHub](https://github.com/allensarkisyan/schwab-td-ameritrade-streamer/blob/main/index.d.ts)

---

## The underlying problem

With equities (`LEVELONE_EQUITIES`) we subscribe to a fixed handful of symbols (SPY, QQQ...)
that never change. With options, **the set of contracts of interest changes constantly** — as
the spot moves, strikes enter and leave the OTM range shown in the grid. The WebSocket doesn't
deliver "the whole chain" at once, it only pushes updates for the specific contracts already
subscribed to.

**Conclusion: we still need REST periodically** — to discover which strikes exist/come into
range. The WebSocket replaces the price refresh for already-known contracts, not the chain
discovery.

---

## Implementation plan

### 1. UI — new radio button
Quotes tab, near "Start Polling"/"Fetch Quotes": **"Polling"** / **"WebSocket"** radio buttons.
Selection persisted (new `QuoteSourceSettingsStore`, same pattern as the rest of the local
settings in `%AppData%\OptionsTrader\`).

### 2. New streaming layer (`SchwabStreamerClient`)
- `SubscribeLevelOneOptions(IEnumerable<string> occKeys)` / `UnsubscribeLevelOneOptions(...)` — same pattern as `SubscribeLevelOneEquity`.
- New helper `BuildOptionStreamKey(symbol, strike, expDate, isCall)` — builds the string with the exact padding shown above.
- Parsing in `HandleMessage` for `serviceName == "LEVELONE_OPTIONS"`, mapping the table's fields to a new `OnOptionTick(string occKey, OptionQuoteDto quote)` event.

### 3. Chain discovery (REST, much less frequent)
- When entering WebSocket mode: an initial REST fetch (reuses `GetOptionsChainAsync`, already existing) to learn the available strikes for the current expiration (and the next one, if "Hide Next ExpDate" isn't checked).
- Periodic refresh (e.g. every 30-60s, not every 6s) only to: (a) detect new strikes that came into range if the spot moved, and (b) rebuild the subscription (UNSUBS for the ones that left range + ADD for the new ones).

### 4. Grid update
- Polling mode: unchanged, full rebuild every 6s via `PopulateQuotesGrid`.
- WebSocket mode: each `OnOptionTick` updates **only that contract's row** in `dgvQuotes` (bid/ask/greeks), without rebuilding the whole grid — needs an `occKey → DataGridViewRow` index.

### 5. Multi-instance / shared hub
Shares the SAME Schwab connection (one per account) already used by equity candles and L1. The
local hub relay (`CandleHubServer`/`CandleHubClient`, see [`docs/LIVE_CHART_STREAMING.md`](LIVE_CHART_STREAMING.md))
needs to be extended again to also relay option ticks — each instance needs to receive only the
ticks for ITS subscribed contracts.

### 6. Risks / things to verify live before trusting this
- Confirm that Schwab pushes **greeks** (delta/gamma/etc.) at the same frequency as bid/ask, or whether it recalculates them less frequently.
- Confirm the exact service name against real traffic (`ws_raw.log`) — same type of surprise as `LEVEL_ONE_EQUITIES` vs `LEVELONE_EQUITIES`.
- Handling subscription "churn": if the spot moves fast, constantly entering/leaving range could generate a lot of ADD/UNSUBS — decide on a buffer (subscribe to a slightly wider range than shown) to avoid constantly re-subscribing.
- Subscription volume: ~6-10 strikes per side (call/put) plus "Next ExpDate" ≈ 20-40 simultaneous contracts per instance — confirm Schwab doesn't have a low symbol-per-connection limit for `LEVELONE_OPTIONS`.

### Suggested build order (when started)
1. `SubscribeLevelOneOptions`/parsing in `SchwabStreamerClient`, validated by hand against `ws_raw.log` with 1-2 contracts before anything else (same protocol used to debug `LEVELONE_EQUITIES`).
2. `BuildOptionStreamKey` + unit/manual tests of the exact format.
3. Radio button UI + `QuoteSourceSettingsStore`.
4. Chain discovery logic + subscription rebuild (churn).
5. Incremental grid update.
6. Extend the local hub for multi-instance.
