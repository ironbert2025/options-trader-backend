# Structure of `C:\OptionsData`

Local folder (outside the repository) where the WinForms app saves everything that doesn't live
in the database or in S3 — market data, trade data, chart drawings, and logs. Reorganized by data
type in `94cf365` (before that, everything landed loose and flat in the root, mixing candles,
ticks, IV, and drawings by naming convention).

## Structure

```
C:\OptionsData\
├── MarketData\
│   ├── Candles\{Symbol}_Hourly1h.csv          — persisted 1h candles (HourlyCandleStore)
│   ├── Candles\{Symbol}_Daily.csv             — persisted daily candles (DailyCandleStore)
│   ├── Ticks\{Symbol}\{Symbol}_Ticks_{yyyyMMdd}.csv          — 1 row/min, derived from CHART_EQUITY (TickPriceStore)
│   └── TicksLevelOne\{Symbol}\{Symbol}_L1Ticks_{yyyyMMdd}.csv — every LEVEL_ONE_EQUITIES tick, ms (LevelOneTickStore)
├── Trades\
│   └── Iv\
│       ├── {Symbol}_{Call|Put}_{date}_{exp}.csv  — option quotes per polling cycle (CsvLogger)
│       └── IV_Historial_Apertura.csv              — opening ATM IV snapshot per symbol/day (IvHistorialWriter)
├── ChartDrawings\
│   └── {Symbol}\
│       ├── {Symbol}_TLines_{modeTag}.csv  — T-Lines per panel (TLineStore; modeTag: 1h/RTH/DailyHora/Daily15Min)
│       ├── {Symbol}_Arrows.csv            — vertical arrows on the 1h panel (VerticalArrowStore)
│       ├── {Symbol}_Rects_{contextTag}.csv — zone rectangles (RectStore)
│       ├── {Symbol}_RectGris.csv          — reference gray rectangle (RectGrisStore)
│       └── {Symbol}_SmaWatches.csv        — armed daily SMA watches (SmaDailyWatchStore)
├── ChartSnapshots\
│   └── {Symbol}\{Symbol}_{timestamp}_trade{tradeId}.png  — combined snapshot of the 3 charts when logging a trade
├── EventLog\
│   ├── events_log.csv                     — signal events (Crosses, Bounces, DZ/SZ) (EventLogStore)
│   └── ct_records_{MachineName}.json      — global T-Line record (creation/resolution) (CtRecordStore)
├── Simulator\
│   └── Trades\{Symbol}\{Symbol}_{yyyyMMdd}.csv  — trades opened/closed in the Simulator (SimTradesStore)
├── Logs\
│   └── iv_historial_errors.log   — IvHistorialWriter errors
└── Backups\
    └── backup_before_*\          — point-in-time backups of candle backfill runs
```

## Why this division

- **By data type first, symbol second**: each store defines a fixed subfolder — adding a new symbol doesn't require touching any path.
- **`Ticks`/`TicksLevelOne` with a per-symbol subfolder**: these accumulate the most files (one per day, forever), so the root folder of each stays navigable.
- **`Trades\Iv` separate from `MarketData`**: this is data from an operation/polling cycle, not pure market data — useful if trade reports get built later without mixing it with candles/ticks.
- **`ChartDrawings` separate**: pure UI state (could be deleted without losing anything of historical value), unlike everything else, which is real data.
- **`ChartSnapshots` separate**: these are images, not CSVs — and they're associated with a specific trade, not a symbol in general.
- **`Backups`**: an existing convention (`backup_before_*`) moved into its own folder instead of living alongside the active CSVs.

## Which store writes where

| Store (code) | Folder |
|---|---|
| `HourlyCandleStore` (`OptionsTrader.WinForms`) | `MarketData\Candles\` |
| `DailyCandleStore` (`OptionsTrader.WinForms`) | `MarketData\Candles\` |
| `TickPriceStore` (`OptionsTrader.Infrastructure.Schwab`) | `MarketData\Ticks\{Symbol}\` |
| `LevelOneTickStore` (`OptionsTrader.Infrastructure.Schwab`) | `MarketData\TicksLevelOne\{Symbol}\` |
| `CsvLogger` (`OptionsTrader.WinForms`) | `Trades\Iv\` |
| `IvHistorialWriter` (`OptionsTrader.WinForms`) | `Trades\Iv\` (master CSV) + `Logs\` (errors) |
| `TLineStore` / `VerticalArrowStore` / `RectStore` / `RectGrisStore` / `SmaDailyWatchStore` (`OptionsTrader.WinForms`) | `ChartDrawings\{Symbol}\` |
| `Form1.SaveTradeChartSnapshotAsync` | `ChartSnapshots\{Symbol}\` |
| `EventLogStore` / `CtRecordStore` (`OptionsTrader.WinForms`) | `EventLog\` |
| `SimTradesStore` (`OptionsTrader.WinForms`) | `Simulator\Trades\{Symbol}\` |

See [`docs/LIVE_CHART_STREAMING.md`](LIVE_CHART_STREAMING.md) for detail on the stores related to the live chart, and [`docs/FEATURES.md`](FEATURES.md) (§8 and §11) for the rest of the app's persistence.
