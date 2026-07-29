# Live Chart (WebView2 + Lightweight Charts + Schwab Streaming)

Documento de referencia para esta feature, desarrollada íntegramente en la rama `feature/trade-pnl-min-max`. Objetivo: que cualquiera (o yo mismo en otra sesión) pueda retomarla sin releer todo el historial de commits.

> Este documento reemplaza la versión anterior (que describía el estado inicial, sin validar contra tráfico real). Todo lo de acá abajo ya está implementado y en uso.

---

## Qué es

Un gráfico de velas **en vivo** del subyacente (spot, ej. SPY/QQQ/TSLA/AAPL/DIA/IWM), alimentado por streaming WebSocket directo a Schwab — **completamente aislado** del resto de la app (no toca el polling de Quotes, ni el trading, ni ninguna lógica existente). Se abre con el botón **"Live Chart"** en la pestaña Quotes.

## 1. Los 3 paneles

Un solo `MultiChartForm` contiene **3 `ChartPanel`** lado a lado (horizontal):

| Panel | Modo (`ChartPanelMode`) | Intervalo | Sesión |
|---|---|---|---|
| **1h** | `Hourly15` | velas de 1 hora | Regular (RTH), 9:30 AM - 4:00 PM ET |
| **15m RTH** | `Fifteen_RTH` | velas de 15 min | Regular (RTH), 9:30 AM - 4:00 PM ET |
| **15m RTH+Overnight** | `Fifteen_Full` | velas de 15 min (toggle a 5 min) | Regular + pre/after-hours |

Cada `ChartPanel` agrega los ticks a su propio bucket (1h/15m) de forma independiente, en memoria (`_liveBucket`/`_liveBucketIndex`/`_liveAnchor`), sin volver a tocar la red — un solo flujo de datos alimenta los 3.

## 2. Una sola conexión Schwab, múltiples instancias del programa

Schwab permite **una sola conexión de streaming por cuenta**, pero el operador corre **una instancia del programa por ticker** (cada una con su propio grid/chart). Esto se resuelve con un **hub local** (`OptionsTrader.WinForms/LocalCandleHub.cs`):

- La primera instancia que arranca **bindea el puerto fijo `51919`** (`CandleHubServer.TryStart`, `IPAddress.Any`) y se convierte en el **"hub"**: es la única que abre la conexión real a Schwab (`SchwabStreamerClient`), y **rebroadcastea** cada candle/tick como JSON delimitado por saltos de línea a quien esté conectado.
- Cualquier otra instancia (en la misma PC, o en **otra PC de la misma red LAN**) que no logre bindear el puerto se conecta como **cliente** (`CandleHubClient`) al hub — mismo puerto, `IPAddress.Any` acepta conexiones locales (`127.0.0.1`) y remotas (IP de la LAN) simultáneamente.
- **Acceso desde otra PC**: botón **"Hub Host"** en Quotes — guarda la IP del hub remoto (`HubHostSettingsStore` → `%AppData%\OptionsTrader\hubhost.json`). Si está configurada, esa instancia se conecta directo a esa IP como cliente, sin intentar convertirse en hub. Requiere abrir el puerto 51919 en el firewall de la PC que hace de hub.
- `ICandleFeed` (`OptionsTrader.Application/Interfaces/ICandleFeed.cs`) abstrae la fuente — `ChartPanel` no necesita saber si le llega la conexión real (`SchwabStreamerClient`) o un relay (`CandleHubClient`).
- Reconexión automática con backoff si el hub cae; los clientes reintentan cada 5s indefinidamente.

Este mecanismo se construyó en `b759645` y se extendió a LAN en `507cd5f`.

## 3. Fuentes de precio: `CHART_EQUITY` vs `LEVEL_ONE_EQUITIES`

Al validar contra tráfico real se detectó que el chart no coincidía exactamente con ThinkorSwim. Investigando (comparando `TickPriceStore` contra el spot casi-continuo del chain de opciones) se confirmó: diferencia absoluta promedio ~$0.32, máxima ~$0.83 sobre SPY (~$740) — un desvío estructural, no un bug.

**Causa**: `CHART_EQUITY` solo empuja **una barra de 1 minuto** por símbolo — el `Close` que llega en un momento dado puede reflejar un precio de varios segundos atrás dentro de ese minuto, no el último trade real.

**Solución** (`9666309`): se suscribe también a `LEVEL_ONE_EQUITIES` (última cotización real, mucho mayor frecuencia — varias veces por segundo):

- `CHART_EQUITY` sigue siendo dueño de **Open/High/Low y los límites de cada vela** (dónde empieza/termina un bucket).
- `LEVEL_ONE_EQUITIES` solo actualiza el **`Close` de la vela EN FORMACIÓN** (`Streamer_OnLevelOneTick` en `ChartPanel.cs`), así el precio mostrado sigue al último trade real sin esperar el cierre de la barra de 1 minuto.
- Campos usados (asumidos según la documentación pública de Schwab, **no confirmados contra tráfico real todavía** — a diferencia de `CHART_EQUITY`, que sí se validó con `ws_raw.log`): `"3"` = Last Price, `"35"` = Trade Time. Si el precio se ve en 0 o extraño en el chart, revisar `ws_raw.log` — el código ya protege contra precios ≤ 0 (nunca llegan al chart, pero sí se guardan en el archivo crudo).
- **Se guarda la data cruda de ambas fuentes por separado**, justo para poder comparar mañana cuál sigue mejor al precio real:
  - `TickPriceStore` (existente) — 1 fila/minuto, derivada de `CHART_EQUITY`. `C:\OptionsData\MarketData\Ticks\{Symbol}\{Symbol}_Ticks_{yyyyMMdd}.csv`.
  - `LevelOneTickStore` (nuevo) — cada tick de `LEVEL_ONE_EQUITIES`, milisegundos. `C:\OptionsData\MarketData\TicksLevelOne\{Symbol}\{Symbol}_L1Ticks_{yyyyMMdd}.csv`.
- Relayado por el hub local (`CandleHubServer.BroadcastLevelOne` / `CandleHubClient.OnLevelOneTick`) — todas las instancias se benefician, no solo la que tiene la conexión real.

## 4. Manejo de zona horaria (resuelto, no tocar sin razón)

Lightweight Charts muestra el timestamp Unix que le pasás como **dígitos UTC literales** — no convierte a la zona horaria local del navegador. Solución: `CandleData.Time` se guarda siempre en **UTC real**; justo antes de mandarlo al JS (`ChartPanel.FakeUtcEpochSeconds` / `ToChartJson`), se convierte a hora de **Nueva York (Eastern)** vía `TimeZoneInfo`, y ese valor se "disfraza" de UTC para que el gráfico lo muestre tal cual — así siempre se ve en hora de NY, sin importar en qué timezone esté configurada la PC. Cualquier tiempo nuevo que se le pase a `chart.html` (líneas, marcas, etc.) debe pasar por este mismo truco.

## 5. Agregación de velas

Schwab's `pricehistory` (REST, historial) solo devuelve velas de 1 minuto — `ChartPanel.AggregateToInterval` las agrupa en buckets de 15 o 60 minutos del lado del cliente (C#), anclados a las 9:30 AM ET para RTH, o a medianoche ET para el panel full-day. La agregación en vivo (`Streamer_OnNewCandle`) usa la misma lógica de bucket para que los límites siempre coincidan entre historial y velas en vivo.

**Vista Daily** (panel 1h, botón "Daily"): agrega hasta ~200 días de velas horarias en velas diarias (`HourlyCandleStore.MaxCandles = 1500` ≈ 200 días × 7 velas/día), recalculando las 4 SMA sobre los cierres diarios. Historial horario respaldado en `C:\OptionsData\MarketData\Candles\{Symbol}_Hourly1h.csv`, backfileado inicialmente desde Yahoo Finance (script `backfill_hourly.js`, no versionado en el repo — vivió en el scratchpad de la sesión) para superar el límite de 10 días de Schwab.

## 6. Indicadores

- **SMA 20/40/100/200** — panel 1h, calculadas en JS (`configurarSMAs`). Sin marcadores de hover (`crosshairMarkerVisible: false`).
- **Bollinger Bands (20, 2 std devs)** — panel 15m RTH (`configurarBollinger`).
- **Monitores Cross-SMA** — panel 1h, 8 toggles (↑/↓ × 20/40/100/200). Al armarse, cada cruce genuino de una vela cerrada dispara un push a Telegram con el chart (`SendChartToTelegramAsync`).

## 7. Herramientas de dibujo

Todas implementadas como *Series Primitives* de Lightweight Charts v4 en `chart.html` (no hay tipo de serie nativo para esto):

| Herramienta | Panel(es) | Notas |
|---|---|---|
| T-Line | 1h, 15m RTH | Persistida por símbolo en 1h (`TLineStore`); no persistida en 15m RTH |
| H-Line | 1h, 15m RTH | Línea roja hasta el borde derecho; misma herramienta reusada en ambos paneles |
| Rect (azul) | 15m RTH+Overnight | Rectángulo por 2 clicks |
| Rect (gris) | 1h | Para marcar lateralidad |
| DZ/SZ | 15m RTH+Overnight | Zonas de demanda/oferta, relleno entre pares |
| Arrow (diagonal) | 15m RTH+Overnight | Rojo si el 1er click es más alto que el 2do, verde si no |
| Flechas verticales (↑/↓) | 1h | Punta en el punto de click; arrastrables; persistidas por símbolo (`VerticalArrowStore`) |
| Piso / Techo (texto) | 1h | Etiqueta naranja en el punto de click |

**Patrón seleccionable/borrable** (gris, azul, T-Line, flechas verticales): click cerca del borde/línea selecciona (contorno amarillo), tecla `Delete` borra el seleccionado. `Clear` (por panel) borra todo lo dibujado en ese panel — y en el 1h también limpia los stores persistidos.

**Persistencia** (`TLineStore`, `VerticalArrowStore`, ambos en `OptionsTrader.WinForms`): CSV simple por símbolo en `C:\OptionsData\ChartDrawings\{Symbol}\`, sin base de datos. Comunicación chart→C# vía `window.chrome.webview.postMessage` → `CoreWebView2.WebMessageReceived` en `ChartPanel.cs`.

## 8. Línea azul de pre-market (panel 15m RTH)

Al abrir "Live Chart" **antes de las 9:30 AM ET**, arranca una línea azul en el momento del click, siguiendo el precio en vivo (`iniciarPreMarketLine` / `actualizarPreMarketLine` en `chart.html`) hasta que el mercado abre — ahí C# simplemente deja de mandar actualizaciones, así que se congela sola, sin lógica extra de "freeze". **No se persiste a disco** — cerrar y reabrir el chart (ese día o al siguiente) reinicia todo el proceso desde cero. Si se abre después de las 9:30, no aparece nada.

## 9. Snapshot local de los 3 charts por trade

Al registrar un trade (demo o real, punto único de convergencia: `Form1.RecordEntryAsync`), si hay un `MultiChartForm` abierto para ese símbolo, se capturan los 3 paneles vía `CoreWebView2.CapturePreviewAsync` (renderiza el chart real, **no** una captura de pantalla — funciona aunque la ventana esté minimizada u ocluida), se combinan lado a lado en el mismo orden que se ven en pantalla, y se guardan en `C:\OptionsData\ChartSnapshots\{Symbol}\{Symbol}_{timestamp}_trade{tradeId}.png`. Solo local — no sube a S3 ni toca la base de datos. Best-effort: nunca bloquea el flujo del trade.

## 10. Otras ventanas relacionadas

- **"Block Mov"** (`FourEtfChartsForm.cs`): ventana con 4 charts de 1h (SPY, QQQ, DIA, IWM) lado a lado, sin toolbar — para ver el movimiento del mercado en conjunto. DIA/IWM se agregan a mano a la lista de suscripción (`Form1.SetUpLiveFeedAsync`), pendiente de que salga de la tabla de Tickers como los demás.

## 11. Archivos involucrados

- **`OptionsTrader.Application/DTOs/Streaming/CandleData.cs`** — DTO `{Time (UTC), Open, High, Low, Close}`.
- **`OptionsTrader.Application/Interfaces/ICandleFeed.cs`** — `OnNewCandle`, `OnLevelOneTick`, `OnDisconnected`.
- **`OptionsTrader.Infrastructure/Schwab/SchwabStreamerClient.cs`** — cliente WebSocket hecho a mano. `ConnectAsync`/`LoginAsync`/`SubscribeChartEquity`/`SubscribeLevelOneEquity`, parseo de mensajes, reconexión con backoff. `LogRawMessage` vuelca todo el tráfico crudo a `C:\OptionsTraderPush\ws_raw.log` para validar el formato contra Schwab real.
- **`OptionsTrader.Infrastructure/Schwab/TickPriceStore.cs`** / **`LevelOneTickStore.cs`** — captura de ticks crudos (ver §3).
- **`OptionsTrader.WinForms/ChartPanel.cs`** — `Panel` embebible con el WebView2: carga de historial, agregación de velas (histórica y en vivo), indicadores, dibujo, captura de imagen (`CaptureImageAsync`).
- **`OptionsTrader.WinForms/MultiChartForm.cs`** — ventana contenedora, arma los 3 `ChartPanel`, toolbar por columna, captura combinada (`CaptureCombinedChartImageAsync`).
- **`OptionsTrader.WinForms/LocalCandleHub.cs`** — `CandleHubServer`/`CandleHubClient` (ver §2 y §3).
- **`OptionsTrader.WinForms/HubHostSettingsStore.cs`**, **`TLineStore.cs`**, **`VerticalArrowStore.cs`**, **`HourlyCandleStore.cs`** — persistencia local (ver secciones correspondientes).
- **`OptionsTrader.WinForms/FourEtfChartsForm.cs`** — ventana "Block Mov".
- **`OptionsTrader.WinForms/ChartAssets/`** — `lightweight-charts.js` (v4.1.3, local, sin CDN) + `chart.html` (todo el JS del chart: indicadores, dibujo, líneas, vista Daily).
- **`Form1.cs` / `Form1.Designer.cs`** — botones `btnLiveChart`, `btnFourEtfCharts`, `btnHubHost`; `SetUpLiveFeedAsync` (elección de hub/cliente, suscripciones); `RecordEntryAsync` (snapshot de trade).

## 12. Qué NO toca esta feature

- El polling de 6s de la pestaña Quotes, `PopulateQuotesGrid`, `FetchAndUpdateQuotesAsync`.
- Cualquier lógica de trading (`PlaceRealTradeAsync`, `CloseTradeRowAsync`, etc.) — el snapshot de charts se agrega *después* de que el trade ya se guardó, sin alterar su flujo.
- La autenticación OAuth2 existente (`SchwabAuthService`) — se reusa tal cual, sin cambios.

## 13. Pendiente / próximas ideas

1. Confirmar los números de campo de `LEVEL_ONE_EQUITIES` (`3`, `35`) contra `ws_raw.log` con tráfico real — comparar `TickPriceStore` vs `LevelOneTickStore` vs precio real (ej. ThinkorSwim) para decidir si el `Close` en vivo debería basarse 100% en L1.
2. Mover DIA/IWM de "agregados a mano" a la tabla de Tickers real.
3. Simulador offline (fase 2) sobre los ticks capturados — todavía no empezado.
