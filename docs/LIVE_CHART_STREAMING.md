# Live Chart (WebView2 + Lightweight Charts + Schwab Streaming) — contexto de desarrollo

Documento de contexto para esta feature en desarrollo, en la rama `feature/trade-pnl-min-max`. Objetivo: que cualquiera (o yo mismo en otra sesión) pueda retomarlo sin releer todo el historial de commits.

---

## Qué es

Un gráfico de velas **en vivo** del subyacente (spot, ej. SPY/QQQ), alimentado por streaming WebSocket directo a Schwab — **completamente aislado** del resto de la app (no toca el polling de Quotes, ni el trading, ni ninguna lógica existente). Se abre con el botón **"Live Chart"** en la pestaña Quotes.

## Decisión de diseño: 3 paneles en una sola ventana, una sola conexión

Un solo `MultiChartForm` contiene **3 `ChartPanel`** lado a lado (horizontal):

| Panel | Intervalo | Sesión |
|---|---|---|
| **1h** | velas de 1 hora | Regular (RTH), 9:30 AM - 4:00 PM ET |
| **15m RTH** | velas de 15 min | Regular (RTH), 9:30 AM - 4:00 PM ET |
| **15m RTH+Overnight** | velas de 15 min | Regular + pre/after-hours (lo que Schwab devuelva) |

**Los 3 paneles comparten una sola conexión de streaming** (`SchwabStreamerClient`), no una por panel. Schwab's `CHART_EQUITY` siempre manda velas de **1 minuto** sin importar qué intervalo mostrás — así que un solo suscriptor alcanza, y cada `ChartPanel` agrega esos mismos ticks a su propio bucket (1h/15m/RTH/full-day) de forma independiente, en memoria (`_liveBucket`/`_liveBucketIndex`), sin volver a tocar la red. `MultiChartForm` es quien conecta y suscribe **una sola vez** (`OnLoad`), después de que los 3 paneles ya registraron sus handlers de `OnNewCandle` en sus constructores; también es quien dispone la conexión al cerrar la ventana.

Por ahora, mientras se valida el streaming en vivo, cada panel arranca con un **historial precargado del viernes más reciente** (no "hoy") — esto es temporal, para poder ver algo mientras se depura la conexión en vivo. La última vela del historial se usa para "sembrar" el agregador en vivo, así el primer tick real extiende esa vela en vez de arrancar una nueva de la nada.

## Archivos involucrados

- **`OptionsTrader.Application/DTOs/Streaming/CandleData.cs`** — DTO `{Time (UTC), Open, High, Low, Close}`.
- **`OptionsTrader.Infrastructure/Schwab/SchwabStreamerClient.cs`** — cliente WebSocket hecho a mano (no hay SDK oficial de Schwab para .NET). Reusa `SchwabAuthService` para el token. Responsabilidades:
  - `GetTodaysHistoricalCandlesAsync` — REST `pricehistory`, velas de 1 minuto del día.
  - `ConnectAsync` / `LoginAsync` / `SubscribeChartEquity` — WebSocket, login, suscripción al servicio `CHART_EQUITY`.
  - Reconexión con backoff si se cae la conexión.
  - **⚠️ Sin validar contra tráfico real todavía** — el formato exacto de los mensajes (LOGIN, ADD, respuesta de `CHART_EQUITY`) se reconstruyó a partir de la documentación pública de Schwab, no se probó en vivo. Primera prueba real pendiente.
- **`OptionsTrader.WinForms/ChartPanel.cs`** — `Panel` embebible con el WebView2, la lógica de carga de historial, agregación de velas (histórica y en vivo, incremental por tick), y el wiring a los eventos de `SchwabStreamerClient`. No conecta ni suscribe la conexión — solo la escucha.
- **`OptionsTrader.WinForms/MultiChartForm.cs`** — ventana contenedora, arma los 3 `ChartPanel` en un `TableLayoutPanel` de 3 columnas, todos compartiendo la misma instancia de `SchwabStreamerClient`. Conecta+suscribe una sola vez en `OnLoad`, y dispone la conexión al cerrar.
- **`OptionsTrader.WinForms/ChartAssets/`** — `lightweight-charts.js` (v4.1.3, Apache 2.0, descargado local — **sin CDN**) + `chart.html` (tema oscuro, candlestick, funciones JS `cargarHistorial()`/`actualizarUltimaVela()`).
- **`Form1.cs` / `Form1.Designer.cs`** — botón `btnLiveChart`, handler `BtnLiveChart_Click`, factory `CreateSchwabStreamerClient()`.

## Manejo de zona horaria (importante, ya resuelto)

Lightweight Charts muestra el timestamp Unix que le pasás como **dígitos UTC literales** — no convierte a la zona horaria local del navegador. Solución aplicada: `CandleData.Time` se guarda siempre en **UTC real**; justo antes de mandarlo al JS (`ChartPanel.ToChartJson`), se convierte a hora de **Nueva York (Eastern)** vía `TimeZoneInfo`, y ese valor se "disfraza" de UTC para que el gráfico lo muestre tal cual — así siempre se ve en hora de NY, sin importar en qué timezone esté configurada la PC.

## Agregación de velas (por qué existe)

Schwab's `pricehistory` no ofrece un `frequencyType` de 60 minutos directamente — solo devuelve velas de 1 minuto (`frequencyType=minute&frequency=1`). `ChartPanel.AggregateToInterval` agrupa esas velas de 1 minuto en buckets de 15 o 60 minutos del lado del cliente (C#), anclados a las 9:30 AM ET para RTH, o a medianoche ET para el panel full-day.

## Pendiente / próximos pasos

1. **Lunes**: probar el streaming en vivo real por primera vez — muy probable que haya que ajustar el parseo de mensajes de `SchwabStreamerClient` contra el tráfico real (login, formato de `CHART_EQUITY`, heartbeats).
2. Una vez confirmado el streaming, quitar el filtro "viernes más reciente" y usar directamente "hoy" / la sesión en curso.
3. **Fase 2** (confirmada, no implementada todavía): agregar 4 SMAs (20/40/100/200) calculadas en JS, más Bollinger Bands (20 períodos, 2 desviaciones) — Lightweight Charts no calcula indicadores nativamente, hay que calcularlos y agregarlos como series de línea aparte.
4. Relleno gris entre las bandas de Bollinger: Lightweight Charts no tiene un tipo de serie nativo para "banda entre dos líneas" — se necesita la API de Plugins/Primitives (v4) para dibujarlo correctamente, o un truco más simple con Area series semitransparentes.

## Qué NO toca esta feature

- El polling de 6s de la pestaña Quotes, `PopulateQuotesGrid`, `FetchAndUpdateQuotesAsync`.
- Cualquier lógica de trading (`PlaceRealTradeAsync`, `CloseTradeRowAsync`, etc.).
- La autenticación OAuth2 existente (`SchwabAuthService`) — se reusa tal cual, sin cambios.
