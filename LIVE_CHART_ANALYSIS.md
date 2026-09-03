# Live Chart — Technical Analysis

Scope: `OptionsTrader.WinForms/ChartPanel.cs`, `MultiChartForm.cs`, `TwoPanelChartsControl.cs`, `DailyChartForm.cs`, `ChartAssets/chart.html`, and the persistence stores (`EventLogStore`, `TLineStore`, `VerticalArrowStore`, `RectStore`, `RectGrisStore`, `SmaDailyWatchStore`, `HourlyCandleStore`, `DailyCandleStore`, `CtRecordStore`, `CtLogWriter`).

Besides the `MultiChartForm` popup window described below, there is a **Charts tab embedded in Form1** (`TwoPanelChartsControl`) — only 2 of the 3 panels (1h and 15m RTH, no panel 3/Overnight), always available without opening a separate window. It has its own options grid ("Today"/"Next"), its own mirror trades grid (18 columns, includes a "Demo/Real" column), AWS/Telegram checkboxes and configurable per-symbol polling, and its own 2 independent Demo-Target/Real-Target radio buttons (orange/green, default Demo-Target on connect) — separate from the group of 4 radios on the Options Quotes tab, controlling only trades opened from the Charts tab grid, always with a target. The automatic analyses (Floor/Ceiling, T-Line+SMA20, etc.) described in this document run the same way in both Charts tab panels, independently of `MultiChartForm`.

Each ticker runs as an independent WinForms process. `MultiChartForm` is the "Live Charts — {Symbol}" window and hosts 3 `ChartPanel` instances (one per timeframe), each with its own WebView2 rendering `chart.html` (Lightweight Charts). The 3 panels share the same `SchwabStreamerClient`/`ICandleFeed` — they do not open independent connections.

## 1. Panels and their overlays

| Panel | Mode | Candles | Session | Own automatic overlays |
|---|---|---|---|---|
| **1h** | `Hourly15` | 1h | RTH only (9:30–16:00 ET) | SMA 20/40/100/200, Bollinger(20,2) with dotted yellow middle band + optional edge markers, day dividers, Floor/Ceiling, PM, BB widening + Δ, prev-day Hi/Lo, previous-day close line (label "C"), ATH (checkbox), "1st Bounce 90%", "Potential CT Up/Down" hint (T-Line), "Daily Analysis" hint, "Stk="/"ΔS=" line |
| **15m RTH** | `Fifteen_RTH` | 15m | RTH only | Bollinger(20,2) with **solid** yellow middle band + edge markers (checkbox), PM, BB widening + Δ, prev-day Hi/Lo, "C" line, ATH, "Exposed on 3 charts" banner, Floor/Ceiling reference lines (mirrored from 1h, **now also visible in premarket**), white trade entry/close line (**new**, previously only on panel 3) |
| **15m RTH+Overnight** | `Fifteen_Full` | 15m (toggle to 5m) | RTH + pre/after-hours | prev-day Hi/Lo, "C" line, ATH, Floor/Ceiling reference lines (mirrored), Demand/Supply zones (manual but evaluated automatically), white trade entry/close line, "ΔS" label — **no longer has the T-Line tool** |

Additionally: the **Daily** button (1h) opens `DailyChartForm` (separate window, its own WebView2) with daily candles aggregated from `HourlyCandleStore` (up to 250 days, to have enough history for SMA100/200). It is not just informational — it has 3 tools of its own:
- **T-Line** (tabs "Hour"/"15 Min", tags "DailyHora"/"Daily15Min" in `TLineStore`) — automatically mirrors to the corresponding 1h/15m RTH panel of the Live Chart (one-way only, Daily → Live Chart).
- **"D.PM"/"D40"/"D100"/"D200"** (checkboxes) — draw the daily SMA 20/40/100/200 as a solid line (each SMA's own color) over the 1h/15m RTH panel of the Live Chart, persisted per symbol via `Form1.GetDailySmaLinesEnabledFor`/`SetDailySmaLineEnabledFor` (`TickerSettingsStore`).
- **"SMA Watch"** (toolbar buttons, periods 20/40/100/200) — arms a cross-monitor against the live price, distinct from the two tools above: it draws nothing on the Live Chart, it only arms the watch (`ChartPanel.EvaluateSmaCrossWatches`, panel 1h) to detect when price crosses that daily SMA. Persisted per symbol via `SmaDailyWatchStore` (`C:\OptionsData\ChartDrawings\{Symbol}\{Symbol}_SmaWatches.csv`), survives closing/reopening either of the 2 windows until manually disarmed.

Below the 3 charts: a live options grid (mirroring Form1) and a trades grid (mirroring Form1, clicking Strike/Close forwards to Form1's real handler). A `crossLog` (TextBox) shows all detected events and WebSocket events (connect/disconnect/reconnect).

In `Form1` (outside the Live Chart, but the same work session): clicking the time label ("HH:mm:ss AM/PM", next to Start Polling/Fetch Quotes) saves a screenshot of the whole window (`DrawToBitmap`, works while minimized) to `C:\OptionsData\ChartSnapshots\{Symbol}\{Symbol}_{timestamp}_WholeUI.png`, and logs the path in Form1's Logger.

## 2. Automatic analyses

All run in C# over `_closedCandles` (already-closed candles, recalculated with a simple SMA the same as the JS overlay) — they do not depend on the chart drawing.

### Floor/Ceiling (panel 1h, once per process session)
- `EvaluatePisoTechoOnce`: calculated **only once per app instance** (static, `s_pisoTechoAnalyzed`), only if the chart is opened **before 9:30 AM ET**. It evaluates the (20,40) and (100,200) pairs independently per SMA (not per pair): in a bearish alignment (fast<slow) each SMA is "Ceiling" (Techo) only if the price stays below THAT SMA; in a bullish alignment, "Floor" (Piso) only if it stays above.
- Each SMA that resolves Floor/Ceiling arms a **watch** (`s_pisoTechoWatches`) — evaluated on each closed 1h candle (`EvaluatePisoTechoWatches`), and also live on a gap-cross (`EvaluatePisoTechoGapLive`, using the SMA recalculated with the live price).
- Resolution: **Cross** (Cruce — price crosses and closes on the other side — by close, or by a gap at the open) or **Bounce** (Rebote — approaches without crossing — or approaches at least 30% of the rejection move — and closes rejected). Each watch resolves only once (`Done`) and does not re-arm during the day.
- `ValidatePisoTechoAgainstOpen` (once, at the RTH open) and `ValidatePisoTechoAgainstLivePrice` (continuous in premarket) invalidate an SMA whose level was already broken by the open/gap before the watch got to evaluate — the internal watch is cleared, but **the visual label is NOT removed** (by explicit request, it stays visible for the whole RTH session even if the price has already broken it).
- The dotted reference lines (15m RTH/Overnight) now end at today's RTH session close (16:00 ET) instead of extending to the chart's right edge — `markPisoTechoRefLine`/`GetTodaySessionEndFakeEpoch` (`MultiChartForm`) / `GetSessionEndFakeEpoch` (`SimulatorForm`).
- The last 1h candle of the day (15:00–16:00) never receives the "next bucket" that closes the others — `EvaluateLastHourCandleBeforeCloseIfNeeded` forces it at 15:59 so as not to miss a genuine Cross/Bounce in the last hour.
- Persistence: **in-memory static only** (`s_pisoTecho*` variables), survives closing/reopening the Live Chart window on the same process day, but is lost on app restart or the next day (no disk store).
- Log: each resolution writes to `EventLogStore` (`Hora`, `PisoTechoCruce`/`PisoTechoRebote`) and fires `OnPisoTechoResolvedEvent`, which MultiChartForm forwards to Telegram (combined snapshot of the 3 charts) and to `crossLog`.
- **"1st Bounce: 90%"** (yellow label, 1h, bottom-right corner): visible while SMA20 and SMA40 are BOTH "Ceiling" (Techo), the SMA20 watch hasn't resolved yet, and no candle has touched SMA20 since it was armed (`s_sma20TechoTouched`).

### "Volatility Opening" (panel 15m RTH)
- Armed by default on the first RTH tick of the day (`ArmVolatilityOpeningWatchDefault`, both sides) and additionally when the 1h panel resolves a Floor/Ceiling (Cross on Ceiling or Bounce on Floor → bullish; Cross on Floor or Bounce on Ceiling → bearish).
- Evaluated on every live tick: fires when the Bollinger(20,2) bands are wider than 3 candles ago AND the SMA20 (middle band) is sloped in the armed direction. Fires only once per session.
- Log in `EventLogStore` (`15Min`, `VolatilityOpening`) + Telegram (single-panel, via `SendChartToTelegramAsync`).
- Separate informational event, `OnVolatilityAlreadyOpenEvent`: if the bands were ALREADY widening when the watch was armed, only logs to `crossLog` (no Telegram/EventLogStore).

### Demand/Supply Zone bounce (panel 15m RTH+Overnight)
- The user draws pairs of lines with the **DZ/SZ** tool (1st line = green/Proximal, 2nd = red/Distal). Geometry decides the type: Proximal > Distal → Demand Zone (below the price); Proximal < Distal → Supply Zone (above).
- `EvaluateDemandZoneRebounds`/`EvaluateSupplyZoneRebounds`, for each closed 15m candle: **Entry** when the Low/High touches the zone (or approaches within 30% of the rejection move); **Broken** (Rota — invalidated forever) if the Low/High breaks the Distal line; **Confirmed bounce** when, while Entered and not Broken, the Close closes back outside the Proximal line (can confirm on the same candle it enters).
- On confirmation, it arms `_autoZonePushArmed`: from then on, **every closed 15m candle** triggers an automatic push of the combined snapshot to Telegram until **"Stop Push"** is pressed (or until another zone reconfirms).
- Log in `EventLogStore` (`15Min`, `DemandZoneRebound`/`SupplyZoneRebound`) + Telegram (self-contained, individual panel) + additional combined Telegram via auto-push.

### T-Line + SMA20 breakout (panel 1h and panel 15m RTH — panel 3 no longer has T-Line)
- **No longer a 1 T-Line limit**: several T-Lines can now be drawn at once on the same panel, each evaluated completely independently (`_tLineSignalFiredFor`, a `HashSet` of lines instead of a single flag). Each T-Line fires its signal at most once, and only resets if that specific line is deleted.
- Panel 1h and panel 15m RTH are now **fully independent of each other**: each has its own `TLineStore` file (tag "1h" or "RTH"), and drawing/deleting a T-Line on one panel **no longer mirrors** to the other (unlike the H-Line, which still mirrors across all 3 panels). **Panel 3 (15m RTH+Overnight) lost the T-Line tool entirely**, by explicit request.
- On candle close: fires if it opened on one side of the T-Line, the High/Low crossed both the T-Line AND the SMA20 (the panel's own) during the candle, and the close ended up on the other side of both.
- Log in `EventLogStore` (`Hora`, `TLineBreakout`) + combined Telegram (`SendTLineSignalTelegramPushAsync`, in MultiChartForm). Also, each drawn T-Line creates a "Pendiente" (Pending) record in `CtRecordStore` at the moment it is drawn (creation), which is updated IN THAT SAME RECORD when it resolves (Alza/Baja, i.e. Up/Down) or when deleted without resolving (EliminadoSinResolver) — see section 5.
- The "Potential CT Up/Down" hint is decided by technical convention when drawing the line (descending → up; ascending → down) — purely visual, an overlay within the chart.

### PM (Midpoint) — SMA20 slope
- Continuous (every tick, **premarket and RTH alike**), on panels 1h and 15m RTH: green if SMA20 rises vs. 3 candles ago, red if it falls. It's not "once" — it is redrawn constantly.
- `MultiChartForm` cross-checks the direction of BOTH panels: if they match (both green or both red), it draws the "PM" label in large size on both; if they don't match, normal size. Cross-panel decision — no panel knows it on its own.

### BB (Bollinger widening) + Δ (panel 1h and 15m RTH)
- Purely visual/continuous (no armed/fired state): "BB" label while THAT panel's own bands are widening (same criterion as "Volatility Opening" but without requiring an armed direction), colored the same way as PM.
- "Δ": distance from the live price to the nearest band, only while the price stays BETWEEN both bands (hidden once it has broken a band).
- **Now also evaluated in premarket** (`EvaluateBollingerWideningLabel` is called from the premarket branch of `Streamer_OnNewCandle`/`UpdateLivePriceFromExternalSource`, same as PM) — previously it only ran once the RTH session started; now "BB" can already be seen next to "PM" before 9:30.

### PM + BB aligned in color (backtesting log)
- `MultiChartForm` tracks, in addition to the PM cross between panels, whether "BB" also matches in color (green/red) between 1h and 15m RTH — and whether PM and BB match each other. When all 4 conditions hold at once (PM 1h == PM 15m RTH == BB 1h == BB 15m RTH, all showing the same direction), **a single line** is logged to `crossLog` with the exact time (`HH:mm:ss  PM + BB alineados en Alza (verde)/Baja (rojo) (1h y 15m RTH)`) — only on the transition into the aligned state (it does not repeat on every tick while it stays aligned). `ChartPanel.OnBollingerWideningLevelEvent` is the new event that makes this possible (same pattern as `OnPuntoMedioLevelEvent`).

### "Exposed" (text next to the premarket blue line, panel 1h and 15m RTH)
- On every premarket tick, compares the live price against THAT panel's own Bollinger(20,2) bands (`GetBollingerDirection`) — "Exposed" above the line if the price already broke the upper band, below if it broke the lower band, hidden if it stays within.
- The text follows the line's actual anchor point (recomputed every frame based on time/price) instead of staying fixed at the center of the canvas — fixed because it previously looked "stuck on screen" when zooming/panning.
- Frozen (together with the blue line) once 9:30 arrives — it keeps showing throughout the RTH session with the value it had at the moment of the open (`s_preMarketLineState`, see section 4), it does not disappear when the market opens.

### "Exposed on 3 charts" (yellow banner, 15m RTH — premarket only)
- On every premarket tick of the 1h panel (`OnPreMarketPriceUpdated`), MultiChartForm compares the Bollinger(20,2) direction of Daily (aggregated in memory from `HourlyCandleStore`), 1h, and 15m RTH. If all 3 match (all Above or all Below) → banner. Re-evaluated on every tick, nothing stays "stuck" — it disappears as soon as one stops matching.

### Prev-day High/Low (all 3 panels, auto-drawn)
- Drawn **once per chart opening** (`_drewPrevDayHiLo`), as red H-Lines, only the side that the reference price did NOT already break (avoids drawing the High if there was a gap-up above it, for example). In `Fifteen_Full` and after 9:30 it is drawn immediately upon loading history; in `Hourly15`/`Fifteen_RTH` before 9:30 it is deferred to the first premarket tick (the same moment the premarket blue line appears).

### Daily bounce (panel 1h, informational, once per session)
- `EvaluateDailyBounce`, right after loading history: aggregates 1h candles into daily ones, takes the last ALREADY CLOSED daily candle (yesterday), and applies the same case-1/case-2 bounce formula against the daily SMA20. Only detects Bounce (not Cross). Purely informational: log in `crossLog` + "Daily Analysis" overlay within the chart + `EventLogStore` (`Diario`, `DailyBounce`) — **no Telegram**.

### Day dividers (panel 1h)
- Dotted vertical lines separating the last 5 days of 1h candles (last 4 lines). Manual toggle (checkbox "Día", enabled by default), it is not an analysis, it's purely decorative.

## 3. Automatic vs. manual drawings

| Element | Origin | Panel(s) | Persists to disk | Deleted by | Mirrored between panels |
|---|---|---|---|---|---|
| Candles + SMA + Bollinger | Automatic | All (SMA only 1h) | No (always recalculated) | — | Not applicable |
| Floor/Ceiling labels + ref-lines | Automatic | 1h (labels), 15m RTH/Overnight (ref-lines) | No — static memory only | Invalidation by open/gap, or new process session | Yes (ref-line mirrored to the other 2) |
| Prev-day Hi/Lo (red H-Line) | Automatic | All | No | Manual delete (click + Delete) — fires `OnHLineDeletedEvent` | Yes, to the other 2 panels |
| PM / BB / Δ / "1st Bounce" | Automatic | 1h / 15m RTH | No | — (recalculated every tick) | PM yes (shared size); BB/Δ are not visually mirrored, but their cross-panel alignment IS tracked for the backtesting log (see section 2) |
| "Exposed on 3 charts" banner | Automatic | 15m RTH | No | — (re-evaluated every tick) | No |
| **T-Line** | Manual (toolbar) | 1h, 15m RTH (panel 3 no longer has this tool) | **Yes** — own `TLineStore` per panel (tag "1h" and tag "RTH", separate files) | Click + Delete (`tline_delete`) | **No** — each panel is independent; no longer mirrored to the other (recent change, previously mirrored to all 3) |
| **H-Line** | Manual (**a single button**, over panel 2 — arms drawing mode on all 3 panels at once) | 1h, 15m RTH, 15m Overnight | No (no HLineStore) | Click + Delete | Yes — drawing (`hline_add`/`addMirroredHLine`) and deleting on any of the 3 mirrors to the other 2 |
| **Rect** (light blue) | Manual (toolbar) | 15m RTH+Overnight | No | Only via Clear (not individually) | No |
| **Rect Gris** (gray rect) | Manual (toolbar) | 1h | **Yes** — `RectGrisStore` | Click on edge + Delete | No |
| **DZ/SZ** (Demand/Supply) | Manual (toolbar) | 15m RTH+Overnight | No (memory only: `_demandZones`/`_supplyZones`) | Only via Clear | No |
| **Arrow** (diagonal) | Manual (toolbar) | 15m RTH+Overnight | No | Only via Clear | No |
| **Green/Red Arrow** (vertical) | Manual (toolbar) | 1h | **Yes** — `VerticalArrowStore` (includes drag/move) | Click shaft + Delete | No |
| **Stk (green, "Stk=xxx")** | Automatic when opening a trade | All (or Overnight only depending on the call) | No | Click + Delete (`strike_delete`) | Yes, to the other 2 |
| **ΔS (Delta-S)** | Automatic when closing a trade | Overnight (explicit call `MarkDeltaSOnOvernightChartAsync`) | No | Deleted together with its Stk line (recent commit) | — |
| **White trade entry/close line** | Automatic when opening/closing a trade | Panel 2 (15m RTH) **and** panel 3 (Overnight) — previously only panel 3 | No | Recalculated/redrawn | — |
| **ATH (reference line)** | Automatic (calculated) + show/hide checkbox | All | Yes — `AllTimeHighStore` | Checkbox hides/shows, does not delete the data | Yes, the value calculated in 1h propagates to the other 2 |
| **Previous-day close line ("C")** | Automatic on loading history | All | No | — (redrawn) | Yes, each panel calculates it on its own from its own candles |

**Clear** (button per column) deletes everything drawn on that panel, and on 1h, also clears `TLineStore`, `VerticalArrowStore`, and `RectGrisStore` on disk (deletes the file).

## 4. What persists and what doesn't

| Store | File | Content | Per symbol | Cleared daily |
|---|---|---|---|---|
| `TLineStore` | `C:\OptionsData\ChartDrawings\{Symbol}\{Symbol}_TLines.csv` | T-Lines from panel 1h (t1,p1,t2,p2 — epoch "ET disguised as UTC") | Yes | No — persists until manually deleted (Delete or Clear) |
| `VerticalArrowStore` | `C:\OptionsData\ChartDrawings\{Symbol}\{Symbol}_Arrows.csv` | Vertical arrows from panel 1h (time, price, up) | Yes | No |
| `RectGrisStore` | `C:\OptionsData\ChartDrawings\{Symbol}\{Symbol}_RectGris.csv` | Gray rectangles from panel 1h (t1,p1,t2,p2) | Yes | No |
| `HourlyCandleStore` | `C:\OptionsData\MarketData\Candles\{Symbol}_Hourly1h.csv` | 1h candles accumulated across sessions (up to 1500, ~200 business days) for SMA100/200 and the Daily view | Yes | No — grows day by day |
| `EventLogStore` | `C:\OptionsData\EventLog\events_log.csv` | Cumulative CSV log of ALL analysis events (all symbols, a single file shared between processes, with lock retries) | No (Symbol column within the CSV) | No |
| `CtRecordStore` | `C:\OptionsData\EventLog\ct_records_{MachineName}.json` | Global record (all symbols, all T-Line sources) of creation (Pendiente/Pending) and resolution (Alza/Baja/EliminadoSinResolver, i.e. Up/Down/DeletedUnresolved) of each T-Line — one JSON per PC, not rotated by day/symbol, `CtLogWriter` regenerates a complete `.md` note on every change | No (Symbol field within the JSON) | No |
| `SmaDailyWatchStore` | `C:\OptionsData\ChartDrawings\{Symbol}\{Symbol}_SmaWatches.csv` | Daily SMA periods (20/40/100/200) currently armed for live cross monitoring, from `DailyChartForm` | Yes | No |
| `RectStore` | `C:\OptionsData\ChartDrawings\{Symbol}\{Symbol}_Rects_{contextTag}.csv` | Rectangles from the Daily popup (`DailyChartForm`, not the Live Chart) — contextTag "Daily"/"DailyColor" | Yes | No |
| `DailyCandleStore` | `C:\OptionsData\MarketData\Candles\{Symbol}_Daily.csv` | Persisted aggregated daily candles | Yes | No |
| **Does not persist** | — | Floor/Ceiling (static in memory, `s_pisoTecho*`), premarket blue line (`s_preMarketLineState`, in memory), manual H-Lines, light-blue Rect, DZ/SZ, diagonal Arrow, "Volatility Opening"/PM/BB state (everything is recalculated from `_closedCandles` on every opening) | — | — |

Note: `s_preMarketLineState` (premarket blue line + frozen Bollinger exposure) is static in memory, keyed by `{symbol}_{mode}` — survives closing/reopening the Live Chart on the same process day but is lost on app restart.

## 4bis. Per-ticker gating: AWS and Telegram

`TickerSettingsStore` stores, for each symbol, two independent checkboxes:

- **AWS**: if off, a trade opened from the Live Chart grid does **not** `POST` to the ASP.NET Core API nor upload screenshots to S3 — it uses a negative trade id as a local fallback. The trade still appears in `dgvTrades` (mirror) and in the combined daily Markdown note, but the referenced images are local `file://` paths instead of S3 URLs. It also doesn't trigger a Telegram push (there is nothing to send without the persisted record).
- **Telegram**: gates only the **"event"** pushes — Floor/Ceiling, T-Line breakout, Volatility Opening, DZ/SZ (manual and auto-push) — but does **not** affect trade open/close pushes, which follow their own path independent of this checkbox.

Both are queried via `Form1.IsAwsEnabledFor(symbol)` / `IsTelegramEnabledFor(symbol)`, reused by `ChartPanel`/`MultiChartForm` before every push or remote persistence.

## 5. Events and trades saved to disk

- **`EventLogStore`** (`C:\OptionsData\EventLog\events_log.csv`): one row per detected event, columns `Date,Time,Symbol,Timeframe,EventType,Direction,Description,Price,Reference`. Events that write there: `DailyBounce`, `TLineBreakout`, `DemandZoneRebound`, `SupplyZoneRebound`, `PisoTechoCruce`, `PisoTechoRebote`, `VolatilityOpening`. A single file shared across the processes of all tickers (exclusive lock with retry, same pattern as `OpenTradesStore`).
- **`EventLogMarkdownWriter`**: invoked every time a Telegram push succeeds (`AppendEvent(symbol, caption, screenshotPath)`) — a parallel Markdown record of each successful push with its caption and PNG path (exact path not confirmed without reading the file, but it is triggered from `SendChartToTelegramAsync`, `SendTLineSignalTelegramPushAsync`, `SendPisoTechoTelegramPushAsync`, and `SendAutoZonePushAsync`).
- **`CtRecordStore`/`CtLogWriter`**: a T-Line-specific record, separate from `EventLogStore`/`EventLogMarkdownWriter` — a global JSON per PC (not rotated by day/symbol) that tracks the CREATION of each T-Line (status "Pendiente"/Pending, the moment it is drawn) and then updates THAT SAME record when it resolves ("Alza"/"Baja", i.e. Up/Down) or is deleted without resolving ("EliminadoSinResolver"). `CtLogWriter` subscribes to the changes and regenerates a complete `.md` note (`{MachineName}_CT.md`) on every mutation — unlike the other writers in this list, it is not append-only.
- **Real trades**: the Live Chart does NOT save them directly — trades (real or demo) are opened/closed through `Form1`, which calls the ASP.NET Core API (`OptionsTrader.API`) to persist them to the SQL Server database (RDS), as dictated by the project's Clean Architecture (`Domain.Trade` → DTO → API). The Live Chart only REACTS to those trades (draws Stk/ΔS, refreshes the mirror grid) — it does not write to the trades table. This clearly distinguishes it from the Simulator, which persists its own trades to a local CSV (`SimTradesStore`), without going through the API/DB.
- **Telegram screenshots**: PNGs saved to `C:\OptionsTraderPush\{Symbol}_{Tipo}_{yyyyMMdd_HHmmss}.png`, captured via `CoreWebView2.CapturePreviewAsync` (not a screen capture — works even while the window is minimized/hidden).

## 6. Chronological test checklist — Premarket → RTH Close

**Before opening the app (once a day, before 9:30 AM ET):**
- [ ] Verify that `C:\OptionsData\ChartDrawings\{Symbol}\*` and `C:\OptionsData\MarketData\Candles\{Symbol}_Hourly1h.csv` exist from previous sessions (if applicable) — confirm that T-Lines/Arrows/RectGris reload when the chart opens.

**Premarket (before 9:30 AM ET):**
- [ ] Open "Live Charts — {Symbol}" BEFORE 9:30 — confirm `EvaluatePisoTechoOnce` runs (check `crossLog` or the Floor/Ceiling label on panel 1h).
- [ ] Confirm the premarket blue line + price value appears on the corresponding panel.
- [ ] Verify the dotted Floor/Ceiling ref-lines appear mirrored on the 15m RTH and 15m Overnight panels.
- [ ] If price moves in premarket, confirm live invalidation of Floor/Ceiling if it breaks the level (`ValidatePisoTechoAgainstLivePrice`) — the internal watch is cleared, but the label should stay visible across all 3 panels (not removed, by design).
- [ ] Confirm the "Exposed on 3 charts" banner appears/disappears correctly based on whether Daily+1h+15m Bollinger match.
- [ ] Confirm prev-day Hi/Lo is drawn on all 3 panels on the first premarket tick (1h/15m RTH panels) or immediately (Overnight panel).
- [ ] Confirm that "BB" (next to "PM") already appears/updates in premarket, not only after 9:30.
- [ ] Confirm the "Exposed" text follows the blue line's anchor point when zooming/panning (it should not stay fixed at the center of the canvas).

**RTH Open (9:30 AM ET):**
- [ ] Confirm `ValidatePisoTechoAgainstOpen` runs only once — an opening gap that breaks a level should clear the internal watch (the label/ref-line stays visible, not removed).
- [ ] Confirm `ArmVolatilityOpeningWatchDefault` arms both sides on the 15m RTH panel on the first RTH tick.
- [ ] Confirm today's day divider appears to the right of the last divider on panel 1h.

**During the RTH session:**
- [ ] Draw an H-Line (with the single button over panel 2) on any of the 3 panels — confirm it appears on the other 2, and that deleting it (Delete) on any one deletes it on all 3.
- [ ] Confirm the premarket blue line + "Exposed" remain frozen and visible for the whole RTH session (they do not disappear when the market opens).
- [ ] Trigger (or wait for) PM and BB to match in color on both panels (1h and 15m RTH) — confirm a SINGLE line in `crossLog` with the exact time, without repeating while the alignment holds.
- [ ] Confirm the dotted Floor/Ceiling reference lines (15m RTH/Overnight) end at session close (16:00 ET), not extending to the chart's edge.
- [ ] Draw several T-Lines on panel 1h — confirm each is evaluated independently (no 1-line limit) and that they do NOT mirror to the 15m RTH panel (each panel is independent).
- [ ] Trigger (or simulate) a T-Line+SMA20 cross/bounce on 1h — confirm log in `crossLog`, a row in `events_log.csv`, and a combined push to Telegram.
- [ ] Draw a DZ pair (green above/red below) on the Overnight panel — bring the price to touch the zone and confirm Entry → Bounce → auto-push armed on every subsequent 15m candle until "Stop Push".
- [ ] Repeat with an SZ (Supply) pair.
- [ ] Confirm a Floor/Ceiling resolved on 1h correctly arms "Volatility Opening" on the 15m RTH panel with the right direction (Cross on Ceiling/Bounce on Floor → bullish; the reverse → bearish).
- [ ] Confirm the PM/BB/Δ labels update live and that PM's size grows when 1h and 15m RTH match in direction.
- [ ] Open a trade (demo or real) from the options grid — confirm the green Stk line on all 3 panels; close it — confirm the ΔS label on the corresponding panel.
- [ ] Delete a Stk line on one panel — confirm it disappears on the other 2.
- [ ] Close and reopen the "Live Charts" window the same day — confirm T-Line/Arrows/RectGris (1h) reload from disk, and that Floor/Ceiling/premarket line are redrawn from the static in-memory state (without re-analyzing).
- [ ] Force a WebSocket disconnect/reconnect — confirm it appears in `crossLog` via `LogWebSocketEvent`.

**Closing of the last 1h candle (15:59–16:00 ET):**
- [ ] Confirm `EvaluateLastHourCandleBeforeCloseIfNeeded` evaluates the 15:00-16:00 candle even if no tick arrives for the next hour (check `events_log.csv` for a Floor/Ceiling event with a timestamp ~15:59).

**Post-close / persistence verification:**
- [ ] Review `C:\OptionsData\EventLog\events_log.csv` — confirm all of today's rows have correct Symbol/EventType/Direction.
- [ ] Review `C:\OptionsData\ChartDrawings\{Symbol}\*.csv` — confirm today's drawn T-Lines/Arrows/RectGris are saved.
- [ ] Review `C:\OptionsData\MarketData\Candles\{Symbol}_Hourly1h.csv` — confirm today's candles were appended (one more row per 1h RTH candle of the day).
- [ ] Confirm Floor/Ceiling does NOT survive an app restart (it's static memory, no store) — on reopening tomorrow it should be re-analyzed from scratch in premarket.
- [ ] If the manual screenshot was used (click on Form1's time label), confirm the PNG in `C:\OptionsData\ChartSnapshots\{Symbol}\*_WholeUI.png` and the corresponding line in Form1's Logger.
