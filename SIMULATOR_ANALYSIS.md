# Simulator — Technical Analysis

Scope: `OptionsTrader.WinForms/SimulatorForm.cs`, `SimulatedChartPanel.cs`, `SimTradesStore.cs`, `SimulationDataLoader.cs`, `SimEventLogMarkdownWriter.cs`.

## 1. Purpose and UI layout

`SimulatorForm` ("Simulador", 1190x1000) is a **replay** window completely independent of `Form1`'s live polling and `MultiChartForm`'s streaming (it can be open at the same time as either). **It does not paper-trade against a live feed** — it replays previously recorded options-chain snapshots for a real symbol+day (captured while the app was running live), via `SimulationDataLoader`.

**Usage flow:** choose Symbol (`_cmbSymbol`, from `TickerSettingsStore`) and Date (`_cmbDate`, from `SimulationDataLoader.GetAvailableDates`), click "Cargar" (Load, `LoadSelectedDay`) loads that day's `SimulationStep` items + hourly/intraday candle context, and positions the view exactly at 9:30:00 ET (market open), not at the first recorded step — via `FindClosestStepIndex`, the same helper used by "Go to time". From there you advance:
- Manual: `◀ Back` / `Forward ▶` (`Step(-1/1)`), or `+1 Min` (`StepOneMinute`, processes PnL/auto-close on every intermediate step, without skipping them).
- Automatic: Play/Pause with a `Timer` at a selectable speed (1/3/5/10 steps/sec), auto-pauses on reaching the end of the data.
- Direct jump: "Go to time" panel with hour buttons (9–15) and minute buttons (00/15/30/45) — jumps to the closest step.

Each step executes `RenderCurrentStep`: repopulates the options-chain grid, re-renders the 3 charts up to that point, and refreshes PnL/auto-close/Min-Max for open demo trades.

**Layout:** top toolbar (Symbol/Date/Cargar/Play-Pause/steps); `_dgvChain` (chain grid, same 12 columns/format/coloring as Form1's live grid); controls on the right (`_grpCounts`, `_grpContracts` — **local, never touch the real stores**; `_grpTrade` with only "No Trade"/"No Trade-Target", no real broker options; `_grpSpeed`; `_pnlGoToTime`; `_pnlSmaEvents` with Cross-SMA 20/40/100/200 buttons + T-Line + Clear; `_pnlDzSz`); `_chartsHost` (3 `SimulatedChartPanel` instances, same 2:2:3 ratio as `MultiChartForm`); `_dgvTrades` (demo trades grid, 16 columns); `_txtEventLog` (black/green log of events + manual opens/closes).

## 2. Chart implementation vs. Live Chart

`SimulatedChartPanel` reuses the **same `chart.html` / Lightweight Charts / WebView2** as the live `ChartPanel` (same path, same cache-busting, same JS functions: `configureSmas`, `configureBollinger`, `loadHistory`, `markStrike`, `markPisoTechoRefLine`, `toggleTLine`, `toggleDzSz`, `markPisoTecho`, `updateFirstRebound`, `updatePuntoMedio`, `updateBollingerWidening`/`updateBollingerDelta`, `configureOvernightBands`, `resetViewForNewDay`, `configureVisibleDays`, `addMirroredZoneLine`/`removeMirroredZonePair`/`clearMirroredZoneLines`).

But it is **deliberately NOT a subclass or variant of `ChartPanel`** — the class's own comment says so explicitly: "completely separate so nothing here can ever affect the live chart's behavior, even by accident." It is a complete parallel implementation (`SimulatedChartPanel : Panel`) with its own copies of the detection logic. Key structural differences:
- No streaming connection or REST fetch — it only renders the list of candles that `SimulatorForm` pushes via `CargarHastaPasoAsync(candles, visibleDays)`, with a full replacement (`loadHistory`) instead of the live `ChartPanel`'s incremental state machine.
- A "closed candle" is detected artificially: `EvaluateNewlyClosedCandles` treats every candle except the last (assumed still forming) as closed, comparing against the length of the previous call to detect steps backward (rollback of all watch/sequence state) or forward (evaluates each new candle in order).
- The WebView instances are reused across different-day loads (one instance per panel for the form's lifetime), unlike the live chart (created once per session) — hence the explicit need for `ResetViewForNewDayAsync()` when loading a day, to clear residual pan/zoom.

## 3. Automatic analyses: ported vs. absent

**Ported (present)**, mostly 1:1 copies of `ChartPanel`'s logic:

| Feature | State in Simulator | Persistence/Telegram |
|---|---|---|
| Cross-SMA (Cross/Bounce 20/40/100/200) | Present (`EvaluateCrossings`/`ToggleCrossMonitor`/`AdvanceCrossSequence`), panel 1h | Log only (`OnCrossSequenceEvent`), no Telegram/persistence |
| T-Line + SMA20 breakout | Present (`EvaluateTLineSignal`), **multiple independent lines in memory** (`_tLines`, a list + `_tLineSignalFiredFor` as a set), no store — ported from the Live Chart, no longer has the old 1-line limit | Log only (`OnTLineSignalEvent`) |
| Demand/Supply Zone bounce (DZ/SZ) | Present (`EvaluateDemandZoneRebounds`/`EvaluateSupplyZoneRebounds`), armed on Overnight, mirrored to 15m RTH | **Only exception**: it DOES write to `EventLogStore` (`events_log.csv`, the same file shared with the live app) — "per explicit request" |
| PM (Midpoint) | Present (`EvaluatePuntoMedioSlope`/`MarkPuntoMedioAsync`), 1h and 15m RTH, with cross-panel size coordination ("large" if both match) same as `MultiChartForm` | Event only, no persistence |
| BB widening + Δ | Present (`EvaluateBollingerWideningLabel`), 1h and 15m RTH | Purely visual, no log |
| Floor/Ceiling (Cross/Bounce, "1st Bounce", ref-line) | Present, evaluated **once per day load** (not once per app process as in the live app — a new simulated day is the closest equivalent to "a new premarket session"). The ref-line now also ends at 16:00 ET of the simulated day (`GetSessionEndFakeEpoch`, the same change ported from `ChartPanel`) instead of running to the chart's edge | Log only (`OnPisoTechoOutcomeEvent`) — explicitly does **NOT** write to `events_log.csv` ("per explicit request"), unlike DZ/SZ |
| Volatility Opening | Present, armed from `SimulatorForm` when the 1h panel resolves a Floor/Ceiling | Log only |
| Daily bounce ("Rebote Diario") | Present (`SimulatorForm.EvaluateDailyBounce`), once per day load against the last daily candle before `_simDate` | Log only |

**Absent/not implemented in the Simulator:**
- **Prev-day High/Low (auto-drawn red H-Lines)** — `DrawPrevDayHiLoAsync`/`EvaluatePrevDayHiLoAsync`/`OnPrevDayHiLoDebugEvent`/`markPrevDayHiLo` **have no counterpart at all** in `SimulatedChartPanel` (confirmed by grep — zero references). It is the **only** automatic analysis from the original list that is missing; everything else (Floor/Ceiling, T-Line+SMA20, DZ/SZ bounce, PM, BB widening, daily bounce) has a ported equivalent.
- **"BB" in premarket** — in the Live Chart, "BB" is now also evaluated during real premarket (before 9:30 AM ET). The Simulator has no concept of its own "premarket" (it starts directly with the recorded day's steps), so this change does not apply/has no counterpart here.

**Recent divergences between Live Chart and Simulator:**
- **Panel 3 without T-Line**: in the Live Chart, panel 3 lost the T-Line tool. The Simulator only has T-Line on panel 1h anyway, so this point does not create a real divergence.
- **ATH (checkbox/reference line)**: has no counterpart in `SimulatedChartPanel`/`SimulatorForm` (no `AllTimeHigh`/`ATH` matches) — not ported.
- **Bollinger edge markers**: ARE ported (`SetBollingerEdgeMarkersVisibleAsync`, `enableBollingerEdgeMarkers()`).
- **White trade entry/close line on panel 2 (15m RTH)**: the Live Chart recently added it to that panel; in the Simulator, `MarkEntrySpotAsync` is **only called on `_fullChart`** (the equivalent of panel 3/Overnight) — the Simulator's 15m RTH panel does **not** draw this line. Confirmed divergence.
- **Spread ≥ 6 disables Strike**: this IS replicated in the Simulator (`c9f97bd`/comments in Form1 confirm "same rule in Live Chart + Simulator"), unlike the divergences above.
- **"PM + BB aligned" log (backtesting)** — `MultiChartForm.CheckPmBbAlignment` (new, see `LIVE_CHART_ANALYSIS.md`) is not ported to the Simulator; `SimulatorForm` does not track the color cross between panels for BB, only for PM (label size, not logging).
- **"Exposed" (premarket text next to the blue line)** — same reason as "BB in premarket": there is no premarket blue line in the Simulator.

## 4. Manual drawing tools: Simulator vs. Live Chart

The live `ChartPanel` exposes: Rect, Rect Gris (persisted), H-Line (**a single button**, over panel 2, arms drawing mode on all 3 panels at once — drawing or deleting on any one mirrors to the other 2), Arrow, vertical arrow (persisted, on 1h), plus DZ/SZ and T-Line.

`SimulatedChartPanel` only implements **two**:
- **T-Line** (`ToggleTLineModeAsync`/`ClearTLineAsync`) — present, memory only (no store).
- **DZ/SZ** (`ToggleDzSzModeAsync`/`ClearDzSzAsync`) — present, armed on the Overnight panel, mirrored to the 15m RTH panel (`AddMirroredZoneLineAsync`/`RemoveMirroredZonePairAsync`), same pattern as the live app.

**Absent in the Simulator:** Rect, Rect Gris, H-Line (manual tool — the auto-drawn prev-day Hi/Lo variant doesn't exist either, see section 3), Arrow, and the vertical arrow tool. There are no `ToggleRect`, `ToggleRectGris`, `ToggleHLine`, `ToggleArrow` methods/events, nor a vertical arrow one, in `SimulatedChartPanel.cs`.

## 5. Disk persistence

**`SimTradesStore.cs`** — a minimal, append-only CSV logger, explicitly documented as "completely separate from `OpenTradesStore`/the real Trades API... never read back to restore state":
- **Path:** `C:\OptionsData\Simulator\Trades\{Symbol}\{Symbol}_{yyyyMMdd}.csv` — **one file per symbol and per simulated day**.
- **Format:** `Symbol,SimDate,OptionType,StrikePrice,Contracts,EntryStepTime,EntryPrice,ExitStepTime,ExitPrice,PnL,PnLPercent`. Header only if the file is new; a row is added on closing each trade (manual or auto-close by target).
- **Not cleared by date** in the sense of deleting previous files — it is append-only, accumulating across sessions per symbol/day. However, the `_dgvTrades` grid and `_openSimTrades` in memory **are** cleared on every `LoadSelectedDay` — the grid is never restored from the CSV, it's a "review later" log, write-only.
- Wrapped in try/catch — "best-effort logging, should never break the simulator."

**Other Simulator state (not persisted):**
- Counts/Contracts selections — local session fields, never written to `CountsSettingsStore`/`ContractsSettingsStore`.
- T-Line — memory only, no store equivalent to `TLineStore`.
- DZ/SZ zones — no store equivalent to `RectGrisStore`; only in-memory lists (`_demandZones`/`_supplyZones`), cleared in `ClearDzSzAsync`.
- Event log text (`_txtEventLog`) — cleared and repopulated on every `LoadSelectedDay`; it does not survive closing the window on screen, but each line is still persisted in `SimEventLogMarkdownWriter`'s `.md` file (and the 2 DZ/SZ events also go to `EventLogStore`, see sections 3/7).
- `_forcedStrikes` — cleared on every day load.

## 6. Data source — no live Schwab connection

The Simulator does **not** connect to any live feed. It works exclusively with pre-recorded historical data via `SimulationDataLoader`:
- `GetAvailableDates(symbol)` — enumerates which days have recorded data.
- `LoadDay(symbol, date)` — loads that day's `SimulationStep` items (options-chain snapshots exactly as originally recorded by the live app).
- `LoadHourlyCandlesWithContext(symbol, date)` — hourly candles with 7 days of prior context (same as `ChartPanel.LoadHistoryAsync`'s default for the 1h panel).
- `LoadUnderlyingCandlesWithContext(symbol, date, contextDays: 3)` — intraday candles with 3 days of context, shared by the 15m RTH and RTH+Overnight panels (aggregated on the fly via `CandleAggregation`).

There is no call to `SchwabClient`, streaming, or REST polling in either file. `SimulatedChartPanel`'s class comment confirms it: "NO streaming connection and NO REST history fetch." The only external reads are local read-only configuration: `TickerSettingsStore`, `BalanceStore`, `PositionSizeSettingsStore`, `TargetSettingsStore`.

## 7. Telegram / EventLogStore / SimEventLogMarkdownWriter

The Simulator is almost completely isolated/local, with **two deliberate exceptions**:
- **No Telegram integration anywhere** — every automatic analysis (Cross-SMA, T-Line, PM, BB widening, Floor/Ceiling, Volatility Opening, daily bounce) is explicitly "log only" ("no Telegram, no persistence, per request (\"es un simulador\" / it's a simulator)").
- **Everything that goes through `LogSimEvent`** (T-Line, Cross-SMA, DZ/SZ, Floor/Ceiling, Volatility Opening, Daily Bounce, manual trade opens/closes) **is persisted via `SimEventLogMarkdownWriter.AppendEvent`**, one `.md` file per run — not lost when the Simulator window closes. Path: `C:\ObsidianVault\RobertVault\0010-Options\12-DailyTrades\{runDate}_{MachineName}_{Symbol}_Sim_{dataDate}_EventLogs.md`, where `runDate` is today (when the replay was run) and `dataDate` is the historical day being replayed — the same symbol/day can be replayed multiple times on different dates, and each run leaves its own file.
- **`EventLogStore.Append` IS also called**, but only for the 2 Demand/Supply Zone bounce events:
```csharp
_fullChart.OnDemandZoneReboundEvent += (caption, price, proximal, distal) =>
{
    LogSimEvent(caption);
    EventLogStore.Append(_symbol, "15Min", "DemandZoneRebound", "Alza", caption, price,
        $"Proximal={proximal:F2};Distal={distal:F2}");
};
```
  (and symmetrically for SupplyZoneRebound/"Baja") — writes to the **same persisted `events_log.csv`** used by the live app, explicitly marked as a "per explicit request" exception, in addition to the `SimEventLogMarkdownWriter` `.md` file that already receives every event equally.
- Aside from that, total isolation: separate stores (`SimTradesStore` vs `OpenTradesStore`), separate configuration (local fields vs. the real stores), no real-time shared state with `Form1`/`MultiChartForm` beyond read-only configuration reads.

## 8. Key differences vs. the Live Chart

| Aspect | Live Chart (`ChartPanel`) | Simulator (`SimulatedChartPanel`) |
|---|---|---|
| Data source | Live Schwab (streaming + REST history) | Recorded snapshots (`SimulationDataLoader`), no network |
| Class relationship | — | Parallel implementation, does NOT inherit from `ChartPanel` (intentional isolation) |
| Floor/Ceiling — when evaluated | Once per app process (premarket, before 9:30) | Once per simulated day load |
| Floor/Ceiling — persists to `events_log.csv` | Yes | **No** (explicitly excluded) |
| Demand/Supply Zone bounce — persists to `events_log.csv` | Yes | **Yes** (the only exception, ported as-is) |
| Auto-drawn Prev-day High/Low | Yes | **Does not exist** |
| "BB" in premarket / "Exposed" blue line | Yes | **Does not exist** (no premarket concept in the replay) |
| "PM + BB aligned" log (backtesting) | Yes (`crossLog`, one line per transition) | **Not ported** |
| Floor/Ceiling ref-line — session limit | Ends at today's 16:00 ET | Ends at the simulated day's 16:00 ET (ported the same) |
| Manual tools | T-Line, H-Line (single button, 3 panels), Rect, Rect Gris, DZ/SZ, Arrow, Green/Red Arrow | Only T-Line and DZ/SZ |
| T-Line/Arrows/Rect Gris persistence | Yes (`TLineStore`/`VerticalArrowStore`/`RectGrisStore`) | No — all in memory, lost on day change/close |
| Trades — where saved | Via `Form1` → ASP.NET Core API → SQL Server (RDS) | `SimTradesStore` — local CSV per symbol/day, **never read back** |
| Telegram | Yes, in almost every analysis (Cross/Bounce, T-Line+SMA20, DZ/SZ, Volatility Opening) | **No, in any case** |
| Options/counts/contracts grid | Connected to the real stores (`CountsSettingsStore`, etc.) | Local selections, never touch the real stores |
| Multiple instances | One WinForms process per ticker, a single active Live Chart per symbol | A single Simulator window, independent of how many Live Charts are open |
