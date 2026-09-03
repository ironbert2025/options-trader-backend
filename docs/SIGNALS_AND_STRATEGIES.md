# Trading Signals (Crosses, Bounces, Demand Zones, T-Line, Bollinger)

Reference document for all signal detectors added on top of the Live Chart (and its
twin in the Simulator) in recent work sessions. Complements
[`LIVE_CHART_STREAMING.md`](LIVE_CHART_STREAMING.md), which documents the
streaming/WebSocket infrastructure — this document only covers the DETECTION LOGIC for each signal.

All these signals live in **`ChartPanel.cs`** (live app) with a deliberately
separate copy (not inherited, not shared) in **`SimulatedChartPanel.cs`** (Simulator) — see
[`SIMULATOR_TELEGRAM_AND_LOGGING.md`](SIMULATOR_TELEGRAM_AND_LOGGING.md) for the reasoning behind that
intentional duplication.

---

## 1. Manual Cross-SMA (Cross / Bounce) — 1h panel (Simulator only)

Removed from the live Live Chart (the 4 pairs of ↑/↓ toggles and the logic that manages them were
removed from `ChartPanel.cs`/`MultiChartForm.cs`). The same mechanics remain alive only in the
**Simulator** (`SimulatorForm.cs`/`SimulatedChartPanel.cs` — see
[`SIMULATOR_TELEGRAM_AND_LOGGING.md`](SIMULATOR_TELEGRAM_AND_LOGGING.md) §2), with no equivalent in the
live app. Manual monitors activated by button (↑/↓ × SMA 20/40/100/200). When arming a
monitor:

- The direction (`_crossUp`) is determined by comparing the current price against the chosen SMA: if the
  price is below it, an UPWARD cross is expected; if it's above, a DOWNWARD cross.
- Multiple SMAs can be armed in sequence — they're resolved one at a time in ascending period
  order (`AdvanceCrossSequence`); once the last one resolves, `OnCrossSequenceFinished` clears the
  buttons.

**Genuine cross formula** (`EvaluateCrossings`, evaluated only on the currently active SMA
in the sequence, on each newly closed 1h candle):

```
crossed = candle of the correct color (green if UP, red if DOWN)
          AND this candle's close already ended up on the crossed side of the current SMA
          AND the PREVIOUS candle's close was still on the other side of the PREVIOUS candle's SMA
```

This is a comparison of **2 consecutive points** (previous close vs. previous SMA, current close vs.
current SMA) — **not** "this candle's open vs. this same candle's SMA". This matters because the
SMA moves between candles: no individual candle may have its open/close "sitting" right on the
SMA, yet the price genuinely crossed when compared point by point. This bug (comparing against the
wrong SMA) was detected and fixed in August/2026 against real AAPL data, and since then
it's the reference pattern reused in Floor/Ceiling (see §2) and in the Simulator.

**Bounce** (`bounced`, if there was no cross, the SAME SMA keeps being watched): the candle reached out toward
the SMA from its side and was rejected back, closing on the original side.
- **Case 1** — the wick DID touch/cross the SMA intra-candle, but the close returned to the original side.
- **Case 2** — the wick fell short, but by less than 30% (`BounceProximityRatio`) of the size of
  the rejection move itself — "it went for it and almost touched it".

Each resolution (Cross or Bounce) only writes one line to the Simulator's text log
(`LogSimEvent`) — no Telegram or `EventLogStore`, unlike the other signals in this
document, which do run in the live app.

## 2. Auto-armed Floor / Ceiling ("Piso" / "Techo") — 1h panel

Automatic analysis **once per process**, run right before market open (only if it's
before 9:30 AM ET), over the close of the last already-closed hourly candle (yesterday):

```
fastSMA < slowSMA  AND  price < fastSMA  →  "Techo" (ceiling; short-term bearish trend,
                                                      price coming from below looking for resistance)
fastSMA > slowSMA  AND  price > fastSMA  →  "Piso"  (floor; short-term bullish trend,
                                                      price coming from above looking for support)
```

Evaluated independently for the (20,40) pair and the (100,200) pair — **they're never
opposite within the same pair** (if 20 is a ceiling, 40 is too). Each non-null result arms **both
periods of the pair separately** (2 independent watches) — draws the "Piso"/"Techo" label next
to each SMA and it stays there all day.

**Resolution of each watch** (`EvaluatePisoTechoWatches`, per closed 1h candle): the same 2-point
formula as Cross-SMA (§1) for a Cross, and the same 30% proximity formula for a Bounce —
applied per period (20, 40, 100 or 200) completely independently. Each watch resolves **only once**
(`watch.Done`) and isn't evaluated again for the rest of the day.

- **Cross at Ceiling ("Techo")** → the price broke upward through a resistance → bullish signal.
- **Bounce at Floor ("Piso")** → the price bounced upward off a support → bullish signal.
- **Cross at Floor ("Piso")** → the price broke downward through a support → bearish signal.
- **Bounce at Ceiling ("Techo")** → the price was rejected downward off a resistance → bearish signal.

Each resolution triggers Telegram + `EventLogStore.Append(..., "PisoTechoCruce"/"PisoTechoRebote",
"Piso"/"Techo", ...)` and the C# event `OnPisoTechoResolvedEvent(evento, pisoTecho)`, which
`MultiChartForm` uses as the trigger for the Bollinger watch on the 15m RTH panel (see §5).

**Explicit design — the label stays on screen even if it's broken:** if the price opens below
a "Piso" (floor) (or above a "Techo" (ceiling)), the internal watch is invalidated and cleared
(`InvalidateIfBrokenByOpen`), but the visual "Piso"/"Techo" label on the chart is **NOT removed** —
it stays visible for the rest of the RTH session, by explicit request, even though the price has already crossed it.

## 3. Bounce in Demand Zone — 15m RTH+Overnight panel

The user manually draws a Demand Zone with the DZ/SZ tool (2 clicks → 2 lines:
green/Proximal on top, red/Distal on the bottom). Each pair of lines where the Proximal (demand) price >
Distal (supply) price is registered as a zone to watch (`_demandZones`).

**Resolution** (`EvaluateDemandZoneRebounds`, per closed 15m candle):
1. **Enters** the zone (`zone.Entered`) when the candle's Low touches or nearly touches the Proximal
   line (same 30% proximity criterion as Cross-SMA/Floor-Ceiling).
2. **Invalidates** (`zone.Done`, no bounce) if the Low breaks below the Distal line — the zone
   is "burned".
3. **Confirms a bounce** (`zone.Done`, with an event) if, after entering, the Close closes back
   above the Proximal line.

Triggers Telegram + `EventLogStore.Append(..., "DemandZoneRebound", "Alza", ...)`.

## 4. T-Line + SMA20 breakout — 1h and 15m RTH panels

The user draws a T-Line (trend line, 2 clicks). **Both panels (1h and 15m RTH) persist
per symbol in their own `TLineStore` file** (tag "1h" and tag "RTH" respectively) — previously only the
1h panel persisted. **Multiple T-Lines can be drawn at once** on the same panel; each one is
evaluated independently and fires its own signal at most once. Drawing or deleting a
T-Line **is no longer mirrored** between the 2 panels (a recent change — previously it was mirrored to all 3
panels of the Live Chart, including panel 3, which has now lost the T-Line tool entirely).
`TLineValueAt` extrapolates the line's value to any time (not only between its 2 anchor points),
using the slope between them.

**Resolution** (`EvaluateTLineSignal`, per closed candle, only once per drawn T-Line):
```
Bullish breakout = open < T-Line
                    AND high  > T-Line  AND high  > SMA20
                    AND close > T-Line  AND close > SMA20

Bearish breakout = open > T-Line
                    AND low   < T-Line  AND low   < SMA20
                    AND close < T-Line  AND close < SMA20
```
That is: the candle opened on one side, and both its wick and its close ended up confirmed on the other
side of BOTH references (T-Line and SMA20) — a simultaneous, clean cross of the two.

On the 1h panel this triggers Telegram with the combined image of the 3 charts
(`MultiChartForm.SendTLineSignalTelegramPushAsync`, not the individual panel) + `EventLogStore`. On
15m RTH it's only an on-screen event (`OnTLineSignalEvent`), with no push of its own.

**Creation vs. resolution record (`CtRecordStore`/`CtLogWriter`):** in addition to `EventLogStore`,
every T-Line drawn (from any of its 3 sources — 1h panel, 15m RTH panel, or the Daily popup,
see below) creates a "Pendiente" (pending) record in `CtRecordStore` at the moment it's drawn, which is later
updated IN THE SAME RECORD (not appended as a new one) when it resolves — to "Alza"/"Baja" if
it breaks, or to "EliminadoSinResolver" if it's deleted before resolving. It's a single global JSON file per
PC (`C:\OptionsData\EventLog\ct_records_{MachineName}.json`, not rotated by day or symbol), from which
`CtLogWriter` automatically regenerates a complete `.md` note every time something changes.

**Third T-Line source — Daily popup:** `DailyChartForm` (see
[`LIVE_CHART_STREAMING.md`](LIVE_CHART_STREAMING.md)) also has its own T-Line tool in
its "Hourly"/"15 Min" tabs (tags "DailyHora"/"Daily15Min" in `TLineStore`), which is automatically mirrored
to the corresponding 1h/15m RTH panel of the Live Chart — drawing or deleting it there
also draws/deletes the copy on the Live Chart, but not the other way around (it's one-way,
Daily → Live Chart).

**Embedded Charts tab — independent panel 2:** the Charts tab (`TwoPanelChartsControl`, see
[`LIVE_CHART_ANALYSIS.md`](../LIVE_CHART_ANALYSIS.md)) also evaluates T-Line + SMA20 on its own
panel 2 (15m RTH), independently of its panel 1 (1h) — same detector, same formula, same
`CtRecordStore`/Telegram record, each panel with its own lines.

## 5. "Volatility Opening" (Bollinger Bands) — 15m RTH panel

**Idea:** once Floor/Ceiling (§2) confirms that the price has already broken through or bounced off a reference
SMA (1h), the exact entry moment is found by watching the Bollinger Bands on the
15m RTH panel — when they're "opening" (widening) AND the live price reaches the band on the
correct side, that's the entry point.

**Armed** (`ArmVolatilityOpeningWatch(bullish)`) from `MultiChartForm`, subscribed to the
1h panel's `OnPisoTechoResolvedEvent` — the 4 possible Floor/Ceiling combinations each point to a
specific direction:

| 1h Resolution | Direction | Band watched |
|---|---|---|
| Cross at Ceiling | Bullish (CALL) | Upper |
| Bounce at Floor | Bullish (CALL) | Upper |
| Cross at Floor | Bearish (PUT) | Lower |
| Bounce at Ceiling | Bearish (PUT) | Lower |

Once armed, it's valid **with no time limit** (valid for the rest of the session) until it fires once
(`_volatilityOpeningFired`, doesn't re-arm after firing).

**Bollinger Bands** (`BollingerBandsAt`, calculated in C# **only for this detection** — it's an
independent copy of the calculation that already exists in `chart.html` for drawing, period 20, 2
standard deviations over the closes of already-closed 15m candles).

**Evaluation** (`EvaluateVolatilityOpening`, on **every live tick** — not on candle close, by
explicit request, to capture the exact moment — via `UpdateLivePriceFromExternalSource` and
also on every closed 1-minute candle as a fallback):

```
current_width  = UpperBand(now) - LowerBand(now)
previous_width = UpperBand(3 candles ago) - LowerBand(3 candles ago)
opening        = current_width > previous_width   (the bands are widening)

bullish: fires if opening AND live_price >= UpperBand(now)
bearish: fires if opening AND live_price <= LowerBand(now)
```

Triggers Telegram + `EventLogStore.Append(..., "VolatilityOpening", "Alza"/"Baja", ...)`.

## 6. Daily candle bounce against daily SMA20 — 1h panel

Purely informational, evaluated only once per instance when loading history
(`EvaluateDailyBounce`): aggregates hourly candles into daily ones, and if YESTERDAY's daily candle
(the last already-closed one, today never counts) bounced off the daily SMA20 (same 30% proximity
formula, Bounce only — there's no daily Cross detection), a hint is shown on the chart. **Does not** send
Telegram or get recorded in `EventLogStore` — it's just a visual hint on open.

---

## Summary of shared constants

| Constant | Value | Use |
|---|---|---|
| `BounceProximityRatio` | 30% | "Almost touched" threshold for every Bounce (Cross-SMA, Floor/Ceiling, Demand Zone, Daily Bounce) |
| Bollinger | period 20, 2 std dev | "Volatility Opening", and the drawing in `chart.html` (independent calculations) |
| `VolatilityWidthLookback` | 3 candles | How many 15m candles back the band width is compared to confirm it's opening |

## Files involved

- **`OptionsTrader.WinForms/ChartPanel.cs`** — all detections §1-§6 (live app).
- **`OptionsTrader.WinForms/SimulatedChartPanel.cs`** — ported copy of §1, §2, §3, §4 and §5 (no
  Telegram, log-only) for the Simulator — see
  [`SIMULATOR_TELEGRAM_AND_LOGGING.md`](SIMULATOR_TELEGRAM_AND_LOGGING.md).
- **`OptionsTrader.WinForms/MultiChartForm.cs`** — orchestrates the bridge between panels (Floor/Ceiling in
  1h → arms Bollinger in 15m RTH) and the T-Line push with the combined snapshot.
- **`OptionsTrader.WinForms/TwoPanelChartsControl.cs`** — embedded Charts tab, evaluates T-Line + SMA20
  on its own panel 2 (15m RTH) independently.
- **`OptionsTrader.WinForms/EventLogStore.cs`** — cumulative CSV of all events
  (`C:\OptionsData\EventLog\events_log.csv`).
- **`OptionsTrader.WinForms/CtRecordStore.cs`** / **`CtLogWriter.cs`** — global record of creation
  vs. resolution for each T-Line (JSON + regenerated `.md` note).
