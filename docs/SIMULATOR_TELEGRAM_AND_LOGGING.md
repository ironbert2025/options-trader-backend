# Simulador, Telegram y Registro de Eventos/Trades

Documento de referencia sobre 3 piezas construidas en paralelo a las señales de trading (ver
[`SIGNALS_AND_STRATEGIES.md`](SIGNALS_AND_STRATEGIES.md)): el **Simulador** (práctica offline sobre
datos ya capturados), la integración con **Telegram**, y los distintos lugares donde queda
registro de lo que pasa (CSV + notas de Obsidian). Para el detalle del streaming/WebSocket en vivo,
ver [`LIVE_CHART_STREAMING.md`](LIVE_CHART_STREAMING.md).

---

## 1. De dónde vienen los datos en vivo (resumen)

La app en vivo recibe 2 tipos de mensajes de Schwab por streaming WebSocket (detalle completo en
`LIVE_CHART_STREAMING.md` §3):

- **`CHART_EQUITY`** — una barra de 1 minuto por símbolo; define Open/High/Low y dónde empieza/
  termina cada vela (bucket de 15m o 1h).
- **`LEVEL_ONE_EQUITIES`** — el último precio operado, con mucha más frecuencia (varias veces por
  segundo); solo actualiza el `Close` de la vela EN FORMACIÓN, para que el chart siga el precio
  real sin esperar a que cierre la barra de 1 minuto.

Ambos pasan por `UpdateLivePriceFromExternalSource` / `Streamer_OnNewCandle` en `ChartPanel.cs` —
esos son también los 2 puntos donde se evalúan las señales que necesitan precio en vivo tick-a-tick
(hoy, solo "Abriendo la Volatilidad" — ver `SIGNALS_AND_STRATEGIES.md` §5). El resto de las señales
(Piso/Techo, Demand Zone, T-Line) solo necesitan el cierre de vela, así que se evalúan únicamente en
`Streamer_OnNewCandle`. (Cross-SMA ya no corre en la app en vivo — solo en el Simulador, ver §2.)

## 2. El Simulador — práctica offline, sin streaming

`SimulatorForm.cs` + `SimulatedChartPanel.cs` son una **copia deliberadamente separada** (no una
subclase, no código compartido) de `Form1`/`ChartPanel` — para que nada de lo que pase en el
Simulador pueda afectar por accidente el comportamiento de la app en vivo, aunque el código se
parezca mucho.

**Modelo de datos:** no hay conexión ni WebSocket. `SimulationDataLoader.LoadHourlyCandlesWithContext`
carga las velas ya capturadas/backfileadas de un símbolo y fecha elegidos
(`C:\OptionsData\MarketData\Candles\{Symbol}_Hourly1h.csv`, el mismo archivo que respalda el panel
1h en vivo — ver §5). Al cargar un día, la vista se posiciona exactamente en las 9:30:00 ET
(apertura de mercado) en vez del primer paso grabado, vía `FindClosestStepIndex` (compartido con
"Ir a hora"). El usuario avanza paso a paso (◀ ▶ o "Ir a hora") y en cada paso
`SimulatorForm` **recalcula y reenvía la lista completa de velas visibles hasta ese punto** —
`SimulatedChartPanel.CargarHastaPasoAsync` reemplaza toda la serie en el chart, no agrega
incrementalmente como hace el chart en vivo con su `_liveBucket`.

**Puente "reenviar todo" → "evaluar solo lo nuevo"** (`EvaluateNewlyClosedCandles`): la última vela
de la lista siempre se asume "todavía en formación" (igual que el `_liveBucket` en vivo); todo lo
anterior se trata como cerrado. Si el paso fue hacia atrás (◀) o un salto a un tiempo anterior, se
detecta porque la lista de cerradas encogió, y ahí se resetea todo el estado de las secuencias
(Cross-SMA, T-Line, Demand Zone, Piso/Techo, Bollinger) para no re-disparar eventos ya vistos.

**Qué está portado del vivo, y con qué diferencias:**

| Señal | Portada al Simulador | Diferencia clave |
|---|---|---|
| Cross-SMA (Cruce/Rebote manual) | Solo acá | Ya no existe en el Live Chart en vivo (removido) — el Simulador es la única implementación restante |
| Piso/Techo | Sí | Se recalcula **una vez por día simulado cargado** (`SetPisoTechoResultsAsync`), no una vez por proceso — un día simulado es el equivalente más cercano a "una nueva sesión pre-market" |
| Demand Zone rebote | Sí | Igual |
| T-Line + SMA20 breakout | Sí | Múltiples líneas independientes, en memoria (no hay `TLineStore` — nada de una T-Line de práctica debe sobrevivir a cerrar el Simulador) |
| Abriendo la Volatilidad (Bollinger) | Sí | Se evalúa contra el **Close de cada vela revelada** (no hay tick en vivo continuo en el Simulador) en vez de un precio continuo |
| Daily bounce | No portado | — |

**`WatchStartDate`** (Piso/Techo): sin este gate, cargar un día simulado evaluaría de golpe TODO el
backlog de contexto (hasta ~200 días de historial precargado) como "recién cerrado", disparando
eventos contra velas de meses atrás en el instante de cargar el día. Se fija a la fecha del día
simulado — solo velas en o después de esa fecha pueden resolver un watch.

**Sin Telegram, con registro permanente en disco:** el Simulador nunca envía Telegram, pero cada
línea que `LogSimEvent` escribe en el log de texto en pantalla (T-Line, Cross-SMA, DZ/SZ,
Piso/Techo, Abriendo la Volatilidad, Daily Bounce, aperturas/cierres manuales) también se persiste
vía `SimEventLogMarkdownWriter.AppendEvent` — un archivo `.md` por corrida de replay (ver §4). Además,
Demand Zone Rebound se persiste también en `events_log.csv` (mismo `EventLogStore` que usa la app en
vivo) por pedido explícito — el resto de las señales del Simulador no tocan ese CSV en particular.

## 3. Telegram

**Un solo canal, 3 tipos de push, todos "best-effort"** (un fallo nunca debe afectar el flujo que
lo originó — trade, señal, etc.):

| Tipo de push | Disparador | Imagen adjunta | Dónde vive el código |
|---|---|---|---|
| Cierre de trade | `Form1.CloseTradeRowAsync` (todo cierre, demo o real, manual o automático) | El snapshot "_Close" de los 3 charts ya capturado al cerrar | `Form1.SendTradeCloseTelegramPushAsync` |
| Señal de un panel individual | Demand Zone, Piso/Techo, Abriendo la Volatilidad — 1 solo panel (Cross-SMA ya no aplica en vivo, ver §2) | Captura del panel que disparó (`CoreWebView2.CapturePreviewAsync`) | `ChartPanel.SendChartToTelegramAsync` (único punto de convergencia de estos 3) |
| T-Line + SMA20 breakout | 1 panel (1h o 15m RTH), pero push con los 3 charts | Combinado de los 3 paneles lado a lado | `MultiChartForm.SendTLineSignalTelegramPushAsync` |
| Auto-push tras rebote DZ/SZ | Armado tras confirmar un rebote de Zona de Demanda/Oferta en el panel 15m RTH+Overnight (`ChartPanel.OnAutoZonePushTickEvent`) | Snapshot combinado de los 3 charts, reenviado en cada vela de 15m cerrada hasta pulsar "Stop Push" | `MultiChartForm.SendAutoZonePushAsync` |

**Credenciales:** `TelegramSettingsStore` (`%AppData%\OptionsTrader\telegram.json`) — bot token +
chat ID, configurables desde la UI.

**`TelegramNotifier.cs`** (ported del proyecto `TradeSignal`, ya probado en producción ahí):
`SendAsync` (texto), `SendPhotoAsync` (imagen + caption opcional), `DeleteMessageAsync`. Cada envío
de texto también queda guardado localmente (`C:\OptionsTraderPush\{Symbol}_{timestamp}.txt`),
independiente de si el push a Telegram tuvo éxito o no.

**`TelegramPushStore`** — registro de cada push exitoso (message ID, chat, símbolo, tipo, hora) —
permite luego borrar mensajes puntuales si hace falta (`DeleteMessageAsync`).

## 4. Registro persistente — 3 lugares distintos, cada uno con su propósito

| Archivo | Contiene | Formato | Alcance |
|---|---|---|---|
| `C:\OptionsData\EventLog\events_log.csv` | Cada señal resuelta (Cross-SMA, Piso/Techo, Demand Zone, Volatilidad) de **todos** los símbolos | CSV acumulativo, 1 fila por evento | App en vivo (+ Demand Zone del Simulador, ver §2) |
| `C:\ObsidianVault\RobertVault\0010-Options\12-DailyTrades\{yyyy_MM_dd}_{PC}_Trades.md` | Cada trade cerrado (demo o real) — imágenes Open/Close/TradeLog subidas a S3 | Markdown, 1 archivo por día por PC | Solo app en vivo (`DailyTradeLogWriter`) |
| `C:\ObsidianVault\RobertVault\0010-Options\12-DailyTrades\{yyyy_MM_dd}_{PC}_EventLogs.md` | Cada notificación de evento efectivamente empujada a Telegram, texto + imagen | Markdown, 1 archivo por día por PC | Solo app en vivo (`EventLogMarkdownWriter`) |
| `C:\ObsidianVault\RobertVault\0010-Options\12-DailyTrades\{runDate}_{PC}_{Symbol}_Sim_{dataDate}_EventLogs.md` | Cada línea que aparece en el log de texto del Simulador (T-Line, Cross-SMA, DZ/SZ, Piso/Techo, Volatilidad, Daily Bounce, aperturas/cierres manuales) | Markdown, 1 archivo por corrida de replay (símbolo + fecha de datos + fecha en que se corrió) | Solo Simulador (`SimEventLogMarkdownWriter`) |

**Por qué 3 y no 1 solo:** `events_log.csv` es para análisis offline en Excel (una fila estructurada
por evento, todos los símbolos juntos, útil para estadísticas). Las 2 notas de Obsidian son para
lectura humana del día, y se separan entre sí porque trades y eventos son cosas distintas (un
trade abierto no implica necesariamente una señal resuelta, y viceversa) — mezclarlos en el mismo
archivo haría más difícil escanear cualquiera de las dos cosas por separado.

**Un archivo por PC** (`{PC}` = `Environment.MachineName`) en los 2 archivos de Obsidian: como
puede haber más de una instancia corriendo en distintas máquinas de la misma red al mismo tiempo
(ver "Hub Host" en `LIVE_CHART_STREAMING.md` §2), esto evita que dos procesos compitan por escribir
la misma línea en el mismo archivo el mismo día.

**`EventLogMarkdownWriter`** (nuevo, agosto/2026): escribe un bloque `### {Símbolo} — {hora}` +
el caption exacto que se mandó a Telegram + la imagen referenciada con `file://` (la misma PNG que
ya se guardó localmente para el push — no hay upload/copia extra). Solo se llama si el push a
Telegram tuvo éxito (`ok == true`), desde los 2 puntos de convergencia de Telegram en la app en
vivo (`ChartPanel.SendChartToTelegramAsync` y `MultiChartForm.SendTLineSignalTelegramPushAsync`).

## Archivos involucrados

- **`OptionsTrader.WinForms/SimulatorForm.cs`** / **`SimulatedChartPanel.cs`** — Simulador individual completo.
- **`OptionsTrader.WinForms/FourEtfSimulatorForm.cs`** — "Sim 4 ETF", segunda ventana de simulador (SPY/QQQ/IWM/DIA en grid 2x2, repetición offline desde disco, ver `LIVE_CHART_STREAMING.md` §10).
- **`OptionsTrader.WinForms/SimulationDataLoader.cs`** — carga de velas históricas para el Simulador.
- **`OptionsTrader.WinForms/SimEventLogMarkdownWriter.cs`** — nota permanente por corrida de replay del Simulador (Obsidian).
- **`OptionsTrader.WinForms/TelegramNotifier.cs`** / **`TelegramSettingsStore.cs`** / **`TelegramPushStore.cs`** — integración con Telegram.
- **`OptionsTrader.WinForms/EventLogStore.cs`** — CSV acumulativo de eventos.
- **`OptionsTrader.WinForms/DailyTradeLogWriter.cs`** — nota diaria de trades (Obsidian).
- **`OptionsTrader.WinForms/EventLogMarkdownWriter.cs`** — nota diaria de eventos (Obsidian).
