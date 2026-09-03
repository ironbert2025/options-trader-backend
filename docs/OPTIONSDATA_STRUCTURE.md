# Estructura de `C:\OptionsData`

Carpeta local (fuera del repositorio) donde la app WinForms guarda todo lo que no vive en la base de datos ni en S3 — datos de mercado, datos de trades, dibujos del chart y logs. Reorganizada por tipo de dato en `94cf365` (antes todo caía suelto y plano en la raíz, mezclando velas, ticks, IV y dibujos por convención de nombre).

## Estructura

```
C:\OptionsData\
├── MarketData\
│   ├── Candles\{Symbol}_Hourly1h.csv          — velas de 1h persistidas (HourlyCandleStore)
│   ├── Ticks\{Symbol}\{Symbol}_Ticks_{yyyyMMdd}.csv          — 1 fila/min, derivado de CHART_EQUITY (TickPriceStore)
│   └── TicksLevelOne\{Symbol}\{Symbol}_L1Ticks_{yyyyMMdd}.csv — cada tick de LEVEL_ONE_EQUITIES, ms (LevelOneTickStore)
├── Trades\
│   └── Iv\
│       ├── {Symbol}_{Call|Put}_{fecha}_{exp}.csv  — cotizaciones de opciones por ciclo de polling (CsvLogger)
│       └── IV_Historial_Apertura.csv              — snapshot de IV ATM de apertura por símbolo/día (IvHistorialWriter)
├── ChartDrawings\
│   └── {Symbol}\
│       ├── {Symbol}_TLines.csv   — T-Lines del panel 1h (TLineStore)
│       └── {Symbol}_Arrows.csv   — flechas verticales del panel 1h (VerticalArrowStore)
├── ChartSnapshots\
│   └── {Symbol}\{Symbol}_{timestamp}_trade{tradeId}.png  — snapshot combinado de los 3 charts al registrar un trade
├── Logs\
│   └── iv_historial_errors.log   — errores de IvHistorialWriter
└── Backups\
    └── backup_before_*\          — respaldos puntuales de corridas de backfill de velas
```

## Por qué esta división

- **Por tipo de dato primero, símbolo después**: cada store define un subfolder fijo — agregar un símbolo nuevo no requiere tocar ninguna ruta.
- **`Ticks`/`TicksLevelOne` con subcarpeta por símbolo**: son las que más archivos acumulan (uno por día, para siempre), así la carpeta raíz de cada una queda navegable.
- **`Trades\Iv` separado de `MarketData`**: es data de una operación/ciclo de polling, no de mercado puro — útil si más adelante se arman reportes de trades sin mezclarlo con velas/ticks.
- **`ChartDrawings` separado**: es puro estado de UI (se podría borrar sin perder nada de valor histórico), distinto de todo lo demás que sí es data real.
- **`ChartSnapshots` separado**: son imágenes, no CSV — y están asociadas a un trade puntual, no a un símbolo en general.
- **`Backups`**: convención ya existente (`backup_before_*`) movida a su propia carpeta en vez de vivir al lado de los CSV activos.

## Qué store escribe dónde

| Store (código) | Carpeta |
|---|---|
| `HourlyCandleStore` (`OptionsTrader.WinForms`) | `MarketData\Candles\` |
| `TickPriceStore` (`OptionsTrader.Infrastructure.Schwab`) | `MarketData\Ticks\{Symbol}\` |
| `LevelOneTickStore` (`OptionsTrader.Infrastructure.Schwab`) | `MarketData\TicksLevelOne\{Symbol}\` |
| `CsvLogger` (`OptionsTrader.WinForms`) | `Trades\Iv\` |
| `IvHistorialWriter` (`OptionsTrader.WinForms`) | `Trades\Iv\` (CSV maestro) + `Logs\` (errores) |
| `TLineStore` / `VerticalArrowStore` (`OptionsTrader.WinForms`) | `ChartDrawings\{Symbol}\` |
| `Form1.SaveTradeChartSnapshotAsync` | `ChartSnapshots\{Symbol}\` |

Ver [`docs/LIVE_CHART_STREAMING.md`](LIVE_CHART_STREAMING.md) para el detalle de los stores relacionados al chart en vivo, y [`docs/FUNCIONALIDADES.md`](FUNCIONALIDADES.md) (§8 y §11) para el resto de la persistencia de la app.
