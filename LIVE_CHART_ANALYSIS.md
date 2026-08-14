# Live Chart — Análisis Técnico

Ámbito: `OptionsTrader.WinForms/ChartPanel.cs`, `MultiChartForm.cs`, `ChartAssets/chart.html`, y los stores de persistencia (`EventLogStore`, `TLineStore`, `VerticalArrowStore`, `RectGrisStore`, `HourlyCandleStore`).

Cada ticker corre como un proceso de WinForms independiente. `MultiChartForm` es la ventana "Live Charts — {Symbol}" y aloja 3 `ChartPanel` (uno por timeframe), cada uno con su propio WebView2 renderizando `chart.html` (Lightweight Charts). Los 3 paneles comparten el mismo `SchwabStreamerClient`/`ICandleFeed` — no abren conexiones independientes.

## 1. Paneles y sus overlays

| Panel | Modo | Velas | Sesión | Overlays automáticos propios |
|---|---|---|---|---|
| **1h** | `Hourly15` | 1h | Solo RTH (9:30–16:00 ET) | SMA 20/40/100/200, Bollinger(20,2), day dividers, Piso/Techo, PM, BB widening + Δ, prev-day Hi/Lo, "1er Rebote 90%", hint "Potencial CT al Alza/Baja" (T-Line), hint "Análisis Diario" |
| **15m RTH** | `Fifteen_RTH` | 15m | Solo RTH | Bollinger(20,2), PM, BB widening + Δ, prev-day Hi/Lo, banner "Expuesto en 3 charts", reference lines Piso/Techo (mirroreadas desde 1h) |
| **15m RTH+Overnight** | `Fifteen_Full` | 15m (toggle a 5m) | RTH + pre/after-hours | prev-day Hi/Lo, reference lines Piso/Techo (mirroreadas), zonas Demand/Supply (manuales pero evaluadas automáticamente) |

Además: botón **Daily** (1h) abre `DailyChartForm` (ventana separada, WebView2 propio) con velas diarias agregadas desde `HourlyCandleStore` (hasta 250 días, para tener suficiente historia para SMA100/200).

Debajo de los 3 charts: grid de opciones en vivo (espejo de Form1) y grid de trades (espejo de Form1, click en Strike/Close reenvía al handler real de Form1). Un `crossLog` (TextBox) muestra todos los eventos detectados y los eventos de WebSocket (connect/disconnect/reconnect).

En `Form1` (fuera del Live Chart, pero misma sesión de trabajo): click en el label de la hora ("HH:mm:ss AM/PM", junto a Start Polling/Fetch Quotes) guarda un screenshot de toda la ventana (`DrawToBitmap`, funciona minimizada) en `C:\OptionsData\ChartSnapshots\{Symbol}\{Symbol}_{timestamp}_WholeUI.png`, y loguea la ruta en el Logger de Form1.

## 2. Análisis automáticos

Todos corren en C# sobre `_closedCandles` (velas ya cerradas, recalculadas con SMA simple igual que el overlay JS) — no dependen del dibujo del chart.

### Piso/Techo (panel 1h, una vez por sesión de proceso)
- `EvaluatePisoTechoOnce`: se calcula **una sola vez por instancia de app** (estático, `s_pisoTechoAnalyzed`), solo si el chart se abre **antes de las 9:30 AM ET**. Evalúa los pares (20,40) y (100,200) de forma independiente por SMA (no por par): en alineación bajista (fast<slow) cada SMA es "Techo" solo si el precio sigue por debajo de ESA SMA; en alineación alcista, "Piso" solo si sigue por encima.
- Cada SMA que resuelve Piso/Techo arma un **watch** (`s_pisoTechoWatches`) — se evalúa en cada vela 1h cerrada (`EvaluatePisoTechoWatches`), y también en vivo ante un gap-cross (`EvaluatePisoTechoGapLive`, usando el SMA recalculado con el precio en vivo).
- Resolución: **Cruce** (precio cruza y cierra del otro lado — por cierre, o por gap en el open) o **Rebote** (se acerca, no cruza — o se acerca al menos un 30% del movimiento de rechazo — y cierra rechazado). Cada watch resuelve una sola vez (`Done`) y no se re-arma en el día.
- `ValidatePisoTechoAgainstOpen` (una vez, al abrir RTH) y `ValidatePisoTechoAgainstLivePrice` (continuo en premarket) invalidan una SMA cuyo nivel ya fue roto por el open/gap antes de que el watch llegara a evaluarse — se quita el label y el watch.
- Las reference-lines punteadas (15m RTH/Overnight) ahora terminan en el cierre de la sesión RTH de hoy (16:00 ET) en vez de extenderse hasta el borde derecho del chart — `markPisoTechoRefLine`/`GetTodaySessionEndFakeEpoch` (`MultiChartForm`) / `GetSessionEndFakeEpoch` (`SimulatorForm`).
- La última vela 1h del día (15:00–16:00) nunca recibe el "siguiente bucket" que cierra las demás — `EvaluateLastHourCandleBeforeCloseIfNeeded` la fuerza a las 15:59 para no perder un Cruce/Rebote genuino de la última hora.
- Persistencia: **solo en memoria estática** (variables `s_pisoTecho*`), sobrevive cerrar/reabrir la ventana Live Chart el mismo día de proceso, pero se pierde al reiniciar la app o al día siguiente (no hay store en disco).
- Log: cada resolución escribe en `EventLogStore` (`Hora`, `PisoTechoCruce`/`PisoTechoRebote`) y dispara `OnPisoTechoResolvedEvent`, que MultiChartForm reenvía a Telegram (snapshot combinado de los 3 charts) y al `crossLog`.
- **"1er Rebote: 90%"** (label amarillo, 1h, esquina inferior derecha): visible mientras SMA20 y SMA40 son AMBAS "Techo", el watch de SMA20 no resolvió aún, y ninguna vela tocó SMA20 desde que se armó (`s_sma20TechoTouched`).

### "Abriendo la Volatilidad" (panel 15m RTH)
- Se arma por default en el primer tick RTH del día (`ArmVolatilityOpeningWatchDefault`, ambos lados) y adicionalmente cuando el panel 1h resuelve un Piso/Techo (Cruce en Techo o Rebote en Piso → alcista; Cruce en Piso o Rebote en Techo → bajista).
- Evaluado en cada tick en vivo: dispara cuando las Bandas de Bollinger(20,2) están más anchas que hace 3 velas Y la SMA20 (banda media) está inclinada en la dirección armada. Dispara una sola vez por sesión.
- Log en `EventLogStore` (`15Min`, `VolatilityOpening`) + Telegram (single-panel, vía `SendChartToTelegramAsync`).
- Evento informativo aparte, `OnVolatilityAlreadyOpenEvent`: si al armar el watch las bandas YA estaban ensanchándose, solo log en `crossLog` (sin Telegram/EventLogStore).

### Demand/Supply Zone rebote (panel 15m RTH+Overnight)
- El usuario dibuja pares de líneas con la herramienta **DZ/SZ** (1ª línea = verde/Proximal, 2ª = roja/Distal). Geometría decide el tipo: Proximal > Distal → Demand Zone (debajo del precio); Proximal < Distal → Supply Zone (arriba).
- `EvaluateDemandZoneRebounds`/`EvaluateSupplyZoneRebounds`, por cada vela 15m cerrada: **Entrada** cuando el Low/High toca la zona (o se acerca dentro del 30% del movimiento de rechazo); **Rota** (invalidada para siempre) si el Low/High rompe la línea Distal; **Rebote confirmado** cuando, estando Entered y no Rota, el Close vuelve a cerrar fuera de la línea Proximal (puede confirmar en la misma vela que entra).
- Al confirmar, arma `_autoZonePushArmed`: desde ahí, **cada vela 15m cerrada** dispara un push automático del snapshot combinado a Telegram hasta que se presiona **"Stop Push"** (o hasta que otra zona reconfirme).
- Log en `EventLogStore` (`15Min`, `DemandZoneRebound`/`SupplyZoneRebound`) + Telegram (self-contained, panel individual) + Telegram combinado adicional vía auto-push.

### T-Line + SMA20 breakout (panel 1h)
- Solo se permite **1 T-Line a la vez** por símbolo (enforced al dibujar). Al cerrar una vela 1h: dispara si abrió de un lado de la T-Line, el High/Low cruzó T-Line Y SMA20 durante la vela, y el cierre quedó del otro lado de ambas. Dispara una sola vez por T-Line (`_tLineSignalFired`, se resetea si se borra/redibuja la línea).
- Log en `EventLogStore` (`Hora`, `TLineBreakout`) + Telegram combinado (`SendTLineSignalTelegramPushAsync`, en MultiChartForm).
- El hint "Potencial CT al Alza/Baja" se decide por convención técnica al dibujar la línea (descendente → alza; ascendente → baja) — puramente visual, overlay dentro del chart.

### PM (Punto Medio) — pendiente SMA20
- Continuo (cada tick, **premarket y RTH por igual**), en paneles 1h y 15m RTH: verde si SMA20 sube vs. hace 3 velas, rojo si baja. No es "una vez" — se redibuja constantemente.
- `MultiChartForm` cruza la dirección de AMBOS paneles: si coinciden (ambos verdes o ambos rojos), dibuja el label "PM" en tamaño grande en los dos; si no coinciden, tamaño normal. Decisión cross-panel — ningún panel la sabe por sí solo.

### BB (Bollinger widening) + Δ (panel 1h y 15m RTH)
- Puramente visual/continuo (sin estado armado/disparado): label "BB" mientras las bandas propias de ESE panel se están ensanchando (mismo criterio que "Abriendo la Volatilidad" pero sin exigir dirección armada), coloreado igual que PM.
- "Δ": distancia del precio en vivo a la banda más cercana, solo mientras el precio sigue ENTRE ambas bandas (se oculta si ya rompió una banda).
- **Ahora también evaluado en premarket** (`EvaluateBollingerWideningLabel` se llama desde la rama premarket de `Streamer_OnNewCandle`/`UpdateLivePriceFromExternalSource`, igual que PM) — antes solo corría una vez arrancaba la sesión RTH; ahora "BB" ya puede verse junto a "PM" antes de las 9:30.

### PM + BB alineados en color (log para backtesting)
- `MultiChartForm` rastrea, además del cruce de PM entre paneles, si "BB" también coincide en color (verde/rojo) entre 1h y 15m RTH — y si PM y BB coinciden entre sí. Cuando las 4 condiciones se cumplen a la vez (PM 1h == PM 15m RTH == BB 1h == BB 15m RTH, todas mostrando la misma dirección), se loguea **una sola línea** en `crossLog` con la hora exacta (`HH:mm:ss  PM + BB alineados en Alza (verde)/Baja (rojo) (1h y 15m RTH)`) — solo en la transición hacia el estado alineado (no repite en cada tick mientras se mantiene). `ChartPanel.OnBollingerWideningLevelEvent` es el evento nuevo que hace esto posible (mismo patrón que `OnPuntoMedioLevelEvent`).

### "Expuesto" (texto junto a la línea azul premarket, panel 1h y 15m RTH)
- En cada tick premarket, compara el precio en vivo contra las bandas de Bollinger(20,2) propias de ESE panel (`GetBollingerDirection`) — "Expuesto" arriba de la línea si el precio ya rompió la banda superior, debajo si rompió la inferior, oculto si sigue dentro.
- El texto sigue el punto de anclaje real de la línea (recomputado en cada frame según tiempo/precio) en vez de quedar fijo en el centro del canvas — corregido porque antes se veía "pegado en pantalla" al hacer zoom/pan.
- Congelado (junto con la línea azul) al llegar las 9:30 — sigue mostrándose durante toda la sesión RTH con el valor que tenía al momento de la apertura (`s_preMarketLineState`, ver sección 4), no desaparece al abrir el mercado.

### "Expuesto en 3 charts" (banner amarillo, 15m RTH — solo premarket)
- En cada tick premarket del panel 1h (`OnPreMarketPriceUpdated`), MultiChartForm compara la dirección Bollinger(20,2) de Daily (agregado en memoria desde `HourlyCandleStore`), 1h y 15m RTH. Si las 3 coinciden (todas Above o todas Below) → banner. Se re-evalúa en cada tick, nada queda "pegado" — desaparece apenas una deja de coincidir.

### Prev-day High/Low (los 3 paneles, auto-dibujado)
- Se dibuja **una vez por apertura de chart** (`_drewPrevDayHiLo`), como H-Lines rojas, solo el lado que el precio de referencia NO rompió ya (evita dibujar el High si hubo gap-up por encima de él, por ejemplo). En `Fifteen_Full` y después de las 9:30 se dibuja inmediatamente al cargar historial; en `Hourly15`/`Fifteen_RTH` antes de las 9:30 se difiere al primer tick premarket (mismo momento en que aparece la línea azul premarket).

### Daily bounce (panel 1h, informativo, una vez por sesión)
- `EvaluateDailyBounce`, justo tras cargar el historial: agrega velas 1h a diarias, toma la última vela diaria YA CERRADA (ayer) y aplica la misma fórmula caso-1/caso-2 de rebote contra la SMA20 diaria. Solo detecta Rebote (no Cruce). Puramente informativo: log en `crossLog` + overlay "Análisis Diario" dentro del chart + `EventLogStore` (`Diario`, `DailyBounce`) — **sin Telegram**.

### Day dividers (panel 1h)
- Líneas verticales punteadas separando los últimos 5 días de velas 1h (últimas 4 líneas). Toggle manual (checkbox "Día", activado por default), no es un análisis, es puramente decorativo.

## 3. Dibujos automáticos vs. manuales

| Elemento | Origen | Panel(es) | Persiste en disco | Se borra con | Mirror entre paneles |
|---|---|---|---|---|---|
| Velas + SMA + Bollinger | Automático | Todos (SMA solo 1h) | No (se recalcula siempre) | — | No aplica |
| Piso/Techo labels + ref-lines | Automático | 1h (labels), 15m RTH/Overnight (ref-lines) | No — solo memoria estática | Invalidación por open/gap, o nueva sesión de proceso | Sí (ref-line mirroreada a los otros 2) |
| Prev-day Hi/Lo (H-Line roja) | Automático | Todos | No | Delete manual (click + Delete) — dispara `OnHLineDeletedEvent` | Sí, a los otros 2 paneles |
| PM / BB / Δ / "1er Rebote" | Automático | 1h / 15m RTH | No | — (se recalcula en cada tick) | PM sí (tamaño compartido); BB/Δ no se mirrorean visualmente, pero su alineación cross-panel SÍ se rastrea para el log de backtesting (ver sección 2) |
| Banner "Expuesto en 3 charts" | Automático | 15m RTH | No | — (re-evaluado en cada tick) | No |
| **T-Line** | Manual (toolbar) | 1h, 15m RTH, 15m Overnight | **Sí** — `TLineStore` (solo aplica persistencia real en 1h; en RTH/Overnight se dibuja pero sin store propio) | Click + Delete (`tline_delete`) | Sí — dibujar/borrar en cualquiera de los 3 se mirrorea a los otros 2 (`AddMirroredTLineAsync`/`RemoveMirroredTLineAsync`) |
| **H-Line** | Manual (**un solo botón**, sobre el panel 2 — arma el modo dibujo en los 3 paneles a la vez) | 1h, 15m RTH, 15m Overnight | No (no hay HLineStore) | Click + Delete | Sí — dibujar (`hline_add`/`addMirroredHLine`) y borrar en cualquiera de los 3 se mirrorea a los otros 2 |
| **Rect** (celeste) | Manual (toolbar) | 15m RTH+Overnight | No | Solo vía Clear (no individual) | No |
| **Rect Gris** | Manual (toolbar) | 1h | **Sí** — `RectGrisStore` | Click borde + Delete | No |
| **DZ/SZ** (Demand/Supply) | Manual (toolbar) | 15m RTH+Overnight | No (solo memoria: `_demandZones`/`_supplyZones`) | Solo vía Clear | No |
| **Arrow** (flecha diagonal) | Manual (toolbar) | 15m RTH+Overnight | No | Solo vía Clear | No |
| **Flecha Verde/Roja** (vertical) | Manual (toolbar) | 1h | **Sí** — `VerticalArrowStore` (incluye drag/move) | Click shaft + Delete | No |
| **Stk (verde, "Stk=xxx")** | Automático al abrir trade | Todos (o solo Overnight según llamada) | No | Click + Delete (`strike_delete`) | Sí, a los otros 2 |
| **ΔS (Delta-S)** | Automático al cerrar trade | Overnight (llamada explícita `MarkDeltaSOnOvernightChartAsync`) | No | Se borra junto con su Stk line (commit reciente) | — |

**Clear** (botón por columna) borra todo lo dibujado en ese panel y, en 1h, además limpia `TLineStore`, `VerticalArrowStore` y `RectGrisStore` en disco (borra el archivo).

## 4. Qué persiste y qué no

| Store | Archivo | Contenido | Por símbolo | Se limpia diario |
|---|---|---|---|---|
| `TLineStore` | `C:\OptionsData\ChartDrawings\{Symbol}\{Symbol}_TLines.csv` | T-Lines del panel 1h (t1,p1,t2,p2 — epoch "ET disfrazado de UTC") | Sí | No — persiste hasta borrado manual (Delete o Clear) |
| `VerticalArrowStore` | `C:\OptionsData\ChartDrawings\{Symbol}\{Symbol}_Arrows.csv` | Flechas verticales del panel 1h (time, price, up) | Sí | No |
| `RectGrisStore` | `C:\OptionsData\ChartDrawings\{Symbol}\{Symbol}_RectGris.csv` | Rectángulos grises del panel 1h (t1,p1,t2,p2) | Sí | No |
| `HourlyCandleStore` | `C:\OptionsData\MarketData\Candles\{Symbol}_Hourly1h.csv` | Velas 1h acumuladas entre sesiones (hasta 1500, ~200 días hábiles) para SMA100/200 y vista Daily | Sí | No — crece día a día |
| `EventLogStore` | `C:\OptionsData\EventLog\events_log.csv` | Log CSV acumulativo de TODOS los eventos de análisis (todos los símbolos, un solo archivo compartido entre procesos, con reintentos por lock) | No (columna Symbol dentro del CSV) | No |
| **No persiste** | — | Piso/Techo (estático en memoria, `s_pisoTecho*`), línea azul premarket (`s_preMarketLineState`, en memoria), H-Lines manuales, Rect celeste, DZ/SZ, Arrow diagonal, estado de "Abriendo la Volatilidad"/PM/BB (todo se recalcula desde `_closedCandles` en cada apertura) | — | — |

Nota: `s_preMarketLineState` (línea azul premarket + exposición Bollinger congelada) es estático en memoria, keyed por `{symbol}_{mode}` — sobrevive cerrar/reabrir el Live Chart el mismo día de proceso pero se pierde al reiniciar la app.

## 5. Eventos y trades guardados en disco

- **`EventLogStore`** (`C:\OptionsData\EventLog\events_log.csv`): una fila por evento detectado, columnas `Date,Time,Symbol,Timeframe,EventType,Direction,Description,Price,Reference`. Eventos que escriben ahí: `DailyBounce`, `TLineBreakout`, `DemandZoneRebound`, `SupplyZoneRebound`, `PisoTechoCruce`, `PisoTechoRebote`, `VolatilityOpening`. Archivo único compartido entre los procesos de todos los tickers (lock exclusivo con reintento, igual patrón que `OpenTradesStore`).
- **`EventLogMarkdownWriter`**: se invoca cada vez que un push a Telegram tiene éxito (`AppendEvent(symbol, caption, screenshotPath)`) — registro paralelo en Markdown de cada push exitoso con su caption y ruta del PNG (no confirmado el path exacto sin leer el archivo, pero se dispara desde `SendChartToTelegramAsync`, `SendTLineSignalTelegramPushAsync`, `SendPisoTechoTelegramPushAsync` y `SendAutoZonePushAsync`).
- **Trades reales**: el Live Chart NO los guarda directamente — los trades (reales o demo) se abren/cierran a través de `Form1`, que llama a la API ASP.NET Core (`OptionsTrader.API`) para persistirlos en la base de datos SQL Server (RDS), como indica la arquitectura Clean Architecture del proyecto (`Domain.Trade` → DTO → API). El Live Chart solo REACCIONA a esos trades (dibuja Stk/ΔS, refresca el grid espejo) — no escribe la tabla de trades. Esto lo distingue claramente del Simulador, que persiste sus propios trades en un CSV local (`SimTradesStore`), sin pasar por la API/DB.
- **Screenshots de Telegram**: PNGs guardados en `C:\OptionsTraderPush\{Symbol}_{Tipo}_{yyyyMMdd_HHmmss}.png`, capturados vía `CoreWebView2.CapturePreviewAsync` (no captura de pantalla — funciona aunque la ventana esté minimizada/oculta).

## 6. Checklist cronológico de pruebas — Premarket → Cierre RTH

**Antes de abrir la app (una sola vez al día, antes de las 9:30 AM ET):**
- [ ] Verificar que `C:\OptionsData\ChartDrawings\{Symbol}\*` y `C:\OptionsData\MarketData\Candles\{Symbol}_Hourly1h.csv` existen de sesiones previas (si aplica) — confirmar que T-Lines/Arrows/RectGris se recargan al abrir el chart.

**Premarket (antes de 9:30 AM ET):**
- [ ] Abrir "Live Charts — {Symbol}" ANTES de las 9:30 — confirmar que se ejecuta `EvaluatePisoTechoOnce` (revisar `crossLog` o el label Piso/Techo en el panel 1h).
- [ ] Confirmar que aparece la línea azul premarket + valor de precio en el panel correspondiente.
- [ ] Verificar que las ref-lines punteadas Piso/Techo aparecen mirroreadas en los paneles 15m RTH y 15m Overnight.
- [ ] Si el precio se mueve premarket, confirmar invalidación en vivo de Piso/Techo si rompe el nivel (`ValidatePisoTechoAgainstLivePrice`) — el label debe desaparecer de los 3 paneles.
- [ ] Confirmar banner "Expuesto en 3 charts" aparece/desaparece correctamente según Bollinger Daily+1h+15m coincidan.
- [ ] Confirmar prev-day Hi/Lo se dibuja en los 3 paneles en el primer tick premarket (paneles 1h/15m RTH) o de inmediato (panel Overnight).
- [ ] Confirmar que "BB" (junto a "PM") ya aparece/actualiza en premarket, no solo después de las 9:30.
- [ ] Confirmar que el texto "Expuesto" sigue el punto de anclaje de la línea azul al hacer zoom/pan (no debe quedar fijo en el centro del canvas).

**Apertura RTH (9:30 AM ET):**
- [ ] Confirmar `ValidatePisoTechoAgainstOpen` corre una sola vez — un gap de apertura que rompe un nivel debe quitar su label/ref-line en los 3 paneles.
- [ ] Confirmar `ArmVolatilityOpeningWatchDefault` arma ambos lados en el panel 15m RTH en el primer tick RTH.
- [ ] Confirmar que el day divider de "hoy" aparece a la derecha del último divisor en el panel 1h.

**Durante la sesión RTH:**
- [ ] Dibujar una H-Line (con el botón único sobre el panel 2) en cualquiera de los 3 paneles — confirmar que aparece en los otros 2, y que borrarla (Delete) en cualquiera la borra en los 3.
- [ ] Confirmar que la línea azul premarket + "Expuesto" quedan congeladas y visibles toda la sesión RTH (no desaparecen al abrir el mercado).
- [ ] Provocar (o esperar) que PM y BB coincidan en color en ambos paneles (1h y 15m RTH) — confirmar UNA sola línea en `crossLog` con la hora exacta, sin repetirse mientras se mantiene la alineación.
- [ ] Confirmar que las reference-lines punteadas Piso/Techo (15m RTH/Overnight) terminan en el cierre de sesión (16:00 ET), no se extienden hasta el borde del chart.
- [ ] Dibujar una T-Line en el panel 1h — confirmar que se mirrorea a los otros 2 paneles y que intentar dibujar una segunda muestra el MessageBox de bloqueo.
- [ ] Provocar (o simular) un cruce/rebote de T-Line+SMA20 en 1h — confirmar log en `crossLog`, fila en `events_log.csv`, y push combinado a Telegram.
- [ ] Dibujar un par DZ (verde arriba/rojo abajo) en el panel Overnight — llevar el precio a tocar la zona y confirmar Entrada → Rebote → auto-push armado en cada vela 15m subsecuente hasta "Stop Push".
- [ ] Repetir con un par SZ (Supply).
- [ ] Confirmar que un Piso/Techo resuelto en 1h arma correctamente "Abriendo la Volatilidad" en el panel 15m RTH con la dirección correcta (Cruce en Techo/Rebote en Piso → alcista; lo inverso → bajista).
- [ ] Confirmar labels PM/BB/Δ se actualizan en vivo y que el tamaño de PM crece cuando 1h y 15m RTH coinciden en dirección.
- [ ] Abrir un trade (demo o real) desde el grid de opciones — confirmar línea Stk verde en los 3 paneles; cerrarlo — confirmar label ΔS en el panel correspondiente.
- [ ] Borrar una línea Stk en un panel — confirmar que desaparece en los otros 2.
- [ ] Cerrar y reabrir la ventana "Live Charts" el mismo día — confirmar que T-Line/Arrows/RectGris (1h) se recargan desde disco, y que Piso/Techo/línea premarket se redibujan desde el estado estático en memoria (sin re-analizar).
- [ ] Forzar una desconexión/reconexión de WebSocket — confirmar que aparece en `crossLog` vía `LogWebSocketEvent`.

**Cierre de la última vela 1h (15:59–16:00 ET):**
- [ ] Confirmar que `EvaluateLastHourCandleBeforeCloseIfNeeded` evalúa la vela de 15:00-16:00 aunque no llegue un tick de la siguiente hora (revisar `events_log.csv` por un evento Piso/Techo con timestamp ~15:59).

**Post-cierre / verificación de persistencia:**
- [ ] Revisar `C:\OptionsData\EventLog\events_log.csv` — confirmar todas las filas del día con Symbol/EventType/Direction correctos.
- [ ] Revisar `C:\OptionsData\ChartDrawings\{Symbol}\*.csv` — confirmar que las T-Lines/Arrows/RectGris dibujadas hoy están guardadas.
- [ ] Revisar `C:\OptionsData\MarketData\Candles\{Symbol}_Hourly1h.csv` — confirmar que las velas de hoy se agregaron (una fila más por vela 1h RTH del día).
- [ ] Confirmar que Piso/Techo NO sobrevive a un reinicio de la app (es memoria estática, no hay store) — al reabrir mañana debe re-analizarse desde cero en premarket.
- [ ] Si se usó el screenshot manual (click en el label de hora de Form1), confirmar el PNG en `C:\OptionsData\ChartSnapshots\{Symbol}\*_WholeUI.png` y la línea correspondiente en el Logger de Form1.
