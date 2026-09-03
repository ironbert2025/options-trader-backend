# Simulator, Telegram, and Event/Trade Logging

Reference document about 3 pieces built in parallel to the trading signals (see
[`SIGNALS_AND_STRATEGIES.md`](SIGNALS_AND_STRATEGIES.md)): the **Simulator** (offline practice on
already-captured data), the **Telegram** integration, and the various places where a record of
what happens is kept (CSV + Obsidian notes). For live streaming/WebSocket detail,
see [`LIVE_CHART_STREAMING.md`](LIVE_CHART_STREAMING.md).

---

## 1. Where live data comes from (summary)

The live app receives 2 types of messages from Schwab over the streaming WebSocket (full detail in
`LIVE_CHART_STREAMING.md` §3):

- **`CHART_EQUITY`** — a 1-minute bar per symbol; defines Open/High/Low and where each candle
  begins/ends (15m or 1h bucket).
- **`LEVEL_ONE_EQUITIES`** — the last traded price, at much higher frequency (several times per
  second); only updates the `Close` of the candle CURRENTLY FORMING, so the chart follows the
  real price without waiting for the 1-minute bar to close.

Both go through `UpdateLivePriceFromExternalSource` / `Streamer_OnNewCandle` in `ChartPanel.cs` —
these are also the 2 points where the signals that need tick-by-tick live pricing are evaluated
(today, only "Opening Volatility" — see `SIGNALS_AND_STRATEGIES.md` §5). The rest of the signals
(Floor/Ceiling, Demand Zone, T-Line) only need the candle close, so they're evaluated only in
`Streamer_OnNewCandle`. (Cross-SMA no longer runs in the live app — only in the Simulator, see §2.)

## 2. The Simulator — offline practice, no streaming

`SimulatorForm.cs` + `SimulatedChartPanel.cs` are a **deliberately separate copy** (not a
subclass, no shared code) of `Form1`/`ChartPanel` — so that nothing that happens in the
Simulator can accidentally affect the live app's behavior, even though the code looks very
similar.

**Data model:** there's no connection or WebSocket. `SimulationDataLoader.LoadHourlyCandlesWithContext`
loads the already-captured/backfilled candles for a chosen symbol and date
(`C:\OptionsData\MarketData\Candles\{Symbol}_Hourly1h.csv`, the same file that backs the live
1h panel — see §5). When loading a day, the view positions itself exactly at 9:30:00 ET
(market open) instead of at the first recorded step, via `FindClosestStepIndex` (shared with
"Go to time"). The user steps forward step by step (◀ ▶ or "Go to time"), and at each step
`SimulatorForm` **recalculates and resends the full list of visible candles up to that point** —
`SimulatedChartPanel.CargarHastaPasoAsync` replaces the entire series on the chart, unlike the
live chart, which appends incrementally via its `_liveBucket`.

**"Resend everything" → "evaluate only what's new" bridge** (`EvaluateNewlyClosedCandles`): the
last candle in the list is always assumed "still forming" (same as the live `_liveBucket`);
everything before it is treated as closed. If the step was backward (◀) or a jump to an earlier
time, this is detected because the closed-candle list shrank, and at that point the entire
sequence state is reset (Cross-SMA, T-Line, Demand Zone, Floor/Ceiling, Bollinger) so as not to
re-trigger events already seen.

**What's ported from live, and with what differences:**

| Signal | Ported to Simulator | Key difference |
|---|---|---|
| Cross-SMA (manual Cross/Bounce) | Only here | No longer exists in the live Live Chart (removed) — the Simulator is the only remaining implementation |
| Floor/Ceiling | Yes | Recalculated **once per loaded simulated day** (`SetPisoTechoResultsAsync`), not once per process — a simulated day is the closest equivalent to "a new pre-market session" |
| Demand Zone bounce | Yes | Same |
| T-Line + SMA20 breakout | Yes | Multiple independent lines, in memory (there's no `TLineStore` — no practice T-Line should survive closing the Simulator) |
| Opening Volatility (Bollinger) | Yes | Evaluated against the **Close of each revealed candle** (there's no continuous live tick in the Simulator) instead of a continuous price |
| Daily bounce | Not ported | — |

**`WatchStartDate`** (Floor/Ceiling): without this gate, loading a simulated day would evaluate
the ENTIRE preloaded context backlog at once (up to ~200 days of history) as "just closed",
triggering events against candles from months ago the instant the day is loaded. It's set to the
simulated day's date — only candles on or after that date can resolve a watch.

**No Telegram, with permanent on-disk logging:** the Simulator never sends Telegram messages, but
every line that `LogSimEvent` writes to the on-screen text log (T-Line, Cross-SMA, DZ/SZ,
Floor/Ceiling, Opening Volatility, Daily Bounce, manual opens/closes) is also persisted via
`SimEventLogMarkdownWriter.AppendEvent` — one `.md` file per replay run (see §4). In addition,
Demand Zone Rebound is also persisted to `events_log.csv` (the same `EventLogStore` used by the
live app) by explicit request — the rest of the Simulator's signals don't touch that particular
CSV.

## 3. Telegram

**A single channel, 3 push types, all "best-effort"** (a failure must never affect the flow that
triggered it — trade, signal, etc.):

| Push type | Trigger | Attached image | Where the code lives |
|---|---|---|---|
| Trade close | `Form1.CloseTradeRowAsync` (every close, demo or real, manual or automatic) | The "_Close" snapshot of the 3 charts already captured on close | `Form1.SendTradeCloseTelegramPushAsync` |
| Single-panel signal | Demand Zone, Floor/Ceiling, Opening Volatility — 1 single panel (Cross-SMA no longer applies live, see §2) | Capture of the panel that triggered it (`CoreWebView2.CapturePreviewAsync`) | `ChartPanel.SendChartToTelegramAsync` (the single convergence point for these 3) |
| T-Line + SMA20 breakout | 1 panel (1h or 15m RTH), but push with all 3 charts | Combined side-by-side image of the 3 panels | `MultiChartForm.SendTLineSignalTelegramPushAsync` |
| Auto-push after DZ/SZ bounce | Armed after confirming a Demand/Supply Zone bounce on the 15m RTH+Overnight panel (`ChartPanel.OnAutoZonePushTickEvent`) | Combined snapshot of the 3 charts, resent on every closed 15m candle until "Stop Push" is pressed | `MultiChartForm.SendAutoZonePushAsync` |

**Credentials:** `TelegramSettingsStore` (`%AppData%\OptionsTrader\telegram.json`) — bot token +
chat ID, configurable from the UI.

**`TelegramNotifier.cs`** (ported from the `TradeSignal` project, already proven in production
there): `SendAsync` (text), `SendPhotoAsync` (image + optional caption), `DeleteMessageAsync`.
Every text send is also saved locally (`C:\OptionsTraderPush\{Symbol}_{timestamp}.txt`),
regardless of whether the push to Telegram succeeded or not.

**`TelegramPushStore`** — a record of every successful push (message ID, chat, symbol, type,
time) — later allows deleting specific messages if needed (`DeleteMessageAsync`).

## 4. Persistent logging — 3 distinct places, each with its own purpose

| File | Contains | Format | Scope |
|---|---|---|---|
| `C:\OptionsData\EventLog\events_log.csv` | Every resolved signal (Cross-SMA, Floor/Ceiling, Demand Zone, Volatility) for **all** symbols | Cumulative CSV, 1 row per event | Live app (+ Simulator Demand Zone, see §2) |
| `C:\ObsidianVault\RobertVault\0010-Options\12-DailyTrades\{yyyy_MM_dd}_{PC}_Trades.md` | Every closed trade (demo or real) — Open/Close/TradeLog images uploaded to S3 | Markdown, 1 file per day per PC | Live app only (`DailyTradeLogWriter`) |
| `C:\ObsidianVault\RobertVault\0010-Options\12-DailyTrades\{yyyy_MM_dd}_{PC}_EventLogs.md` | Every event notification actually pushed to Telegram, text + image | Markdown, 1 file per day per PC | Live app only (`EventLogMarkdownWriter`) |
| `C:\ObsidianVault\RobertVault\0010-Options\12-DailyTrades\{runDate}_{PC}_{Symbol}_Sim_{dataDate}_EventLogs.md` | Every line that appears in the Simulator's text log (T-Line, Cross-SMA, DZ/SZ, Floor/Ceiling, Volatility, Daily Bounce, manual opens/closes) | Markdown, 1 file per replay run (symbol + data date + date it was run) | Simulator only (`SimEventLogMarkdownWriter`) |

**Why 3 and not just 1:** `events_log.csv` is for offline analysis in Excel (a structured row
per event, all symbols together, useful for statistics). The 2 Obsidian notes are for human
reading of the day, and are kept separate from each other because trades and events are
different things (an open trade doesn't necessarily imply a resolved signal, and vice versa) —
mixing them into the same file would make it harder to scan either one separately.

**One file per PC** (`{PC}` = `Environment.MachineName`) for the 2 Obsidian files: since there
can be more than one instance running on different machines on the same network at the same time
(see "Hub Host" in `LIVE_CHART_STREAMING.md` §2), this prevents two processes from competing to
write the same line to the same file on the same day.

**`EventLogMarkdownWriter`** (new, August/2026): writes a `### {Symbol} — {time}` block +
the exact caption sent to Telegram + the image referenced with `file://` (the same PNG already
saved locally for the push — no extra upload/copy). It's only called if the push to Telegram
succeeded (`ok == true`), from the live app's 2 Telegram convergence points
(`ChartPanel.SendChartToTelegramAsync` and `MultiChartForm.SendTLineSignalTelegramPushAsync`).

## Files involved

- **`OptionsTrader.WinForms/SimulatorForm.cs`** / **`SimulatedChartPanel.cs`** — complete individual Simulator.
- **`OptionsTrader.WinForms/FourEtfSimulatorForm.cs`** — "Sim 4 ETF", second simulator window (SPY/QQQ/IWM/DIA in a 2x2 grid, offline replay from disk, see `LIVE_CHART_STREAMING.md` §10).
- **`OptionsTrader.WinForms/SimulationDataLoader.cs`** — historical candle loading for the Simulator.
- **`OptionsTrader.WinForms/SimEventLogMarkdownWriter.cs`** — permanent note per Simulator replay run (Obsidian).
- **`OptionsTrader.WinForms/TelegramNotifier.cs`** / **`TelegramSettingsStore.cs`** / **`TelegramPushStore.cs`** — Telegram integration.
- **`OptionsTrader.WinForms/EventLogStore.cs`** — cumulative event CSV.
- **`OptionsTrader.WinForms/DailyTradeLogWriter.cs`** — daily trade note (Obsidian).
- **`OptionsTrader.WinForms/EventLogMarkdownWriter.cs`** — daily event note (Obsidian).
