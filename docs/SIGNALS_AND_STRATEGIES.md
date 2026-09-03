# Señales de Trading (Cruces, Rebotes, Zonas de Demanda, T-Line, Bollinger)

Documento de referencia de todos los detectores de señales agregados sobre el Live Chart (y su
gemelo en el Simulador) en las últimas sesiones de trabajo. Complementa a
[`LIVE_CHART_STREAMING.md`](LIVE_CHART_STREAMING.md), que documenta la infraestructura de
streaming/WebSocket — acá solo se documenta la LÓGICA DE DETECCIÓN de cada señal.

Todas estas señales viven en **`ChartPanel.cs`** (app en vivo) con una copia deliberadamente
separada (no heredada, no compartida) en **`SimulatedChartPanel.cs`** (Simulador) — ver
[`SIMULATOR_TELEGRAM_AND_LOGGING.md`](SIMULATOR_TELEGRAM_AND_LOGGING.md) para el porqué de esa
duplicación intencional.

---

## 1. Cross-SMA manual (Cruce / Rebote) — panel 1h (solo Simulador)

Removido del Live Chart en vivo (los 4 pares de toggles ↑/↓ y la lógica que los maneja se sacaron
de `ChartPanel.cs`/`MultiChartForm.cs`). La misma mecánica sigue viva únicamente en el
**Simulador** (`SimulatorForm.cs`/`SimulatedChartPanel.cs` — ver
[`SIMULATOR_TELEGRAM_AND_LOGGING.md`](SIMULATOR_TELEGRAM_AND_LOGGING.md) §2), sin equivalente en la
app en vivo. Monitores manuales activables por botón (↑/↓ × SMA 20/40/100/200). Al armar un
monitor:

- Se determina la dirección (`_crossUp`) comparando el precio actual contra la SMA elegida: si el
  precio está por debajo, se espera un cruce hacia ARRIBA; si está por encima, hacia ABAJO.
- Se pueden armar varias SMAs en secuencia — se resuelven una por una en orden ascendente de
  período (`AdvanceCrossSequence`); al resolver la última, `OnCrossSequenceFinished` limpia los
  botones.

**Fórmula de cruce genuino** (`EvaluateCrossings`, evaluada solo sobre la SMA actualmente activa
de la secuencia, en cada vela de 1h recién cerrada):

```
crossed = vela del color correcto (verde si UP, roja si DOWN)
          Y el cierre de ESTA vela ya quedó del lado cruzado de la SMA actual
          Y el cierre de la vela ANTERIOR seguía del otro lado de la SMA de la vela ANTERIOR
```

Es una comparación de **2 puntos consecutivos** (cierre anterior vs SMA anterior, cierre actual vs
SMA actual) — **no** "el open de esta vela vs la SMA de esta misma vela". Esto importa porque la
SMA se mueve entre velas: puede que ninguna vela individual tenga su open/close "montado" sobre la
SMA, pero el precio sí cruzó genuinamente comparando punto a punto. Este bug (comparar contra la
SMA equivocada) se detectó y corrigió en agosto/2026 contra datos reales de AAPL, y desde entonces
es el patrón de referencia reusado en Piso/Techo (ver §2) y en el Simulador.

**Rebote** (`bounced`, si no hubo cruce, se sigue vigilando la MISMA SMA): la vela salió a buscar
la SMA desde su lado y fue rechazada de vuelta, cerrando del lado original.
- **Caso 1** — la mecha SÍ tocó/cruzó la SMA intra-vela, pero el cierre volvió al lado original.
- **Caso 2** — la mecha se quedó corta, pero a menos del 30% (`BounceProximityRatio`) del tamaño
  del propio movimiento de rechazo — "fue a buscarla y casi la toca".

Cada resolución (Cruce o Rebote) solo escribe una línea en el log de texto del Simulador
(`LogSimEvent`) — sin Telegram ni `EventLogStore`, a diferencia de las demás señales de este
documento que sí corren en la app en vivo.

## 2. Piso / Techo auto-armado — panel 1h

Análisis automático **una sola vez por proceso**, corrido justo antes de abrir mercado (solo si son
antes de las 9:30 AM ET), sobre el cierre de la última vela horaria ya cerrada (ayer):

```
SMA_rápida < SMA_lenta  Y  precio < SMA_rápida  →  "Techo" (tendencia bajista de corto plazo,
                                                      precio viene de abajo buscando resistencia)
SMA_rápida > SMA_lenta  Y  precio > SMA_rápida  →  "Piso"  (tendencia alcista de corto plazo,
                                                      precio viene de arriba buscando soporte)
```

Evaluado independientemente para el par (20,40) y el par (100,200) — **nunca son contrarios entre
sí dentro del mismo par** (si 20 es Techo, 40 también lo es). Cada resultado no-nulo arma **ambos
períodos del par por separado** (2 watches independientes) — dibuja la etiqueta "Piso"/"Techo" al
lado de cada SMA y queda así todo el día.

**Resolución de cada watch** (`EvaluatePisoTechoWatches`, por vela de 1h cerrada): misma fórmula de
2 puntos que Cross-SMA (§1) para Cruce, y la misma fórmula de proximidad al 30% para Rebote —
aplicada por período (20, 40, 100 o 200) de forma completamente independiente. Cada watch se
resuelve **una sola vez** (`watch.Done`) y no vuelve a evaluarse el resto del día.

- **Cruce en Techo** → el precio rompió hacia arriba una resistencia → señal alcista.
- **Rebote en Piso** → el precio rebotó hacia arriba desde un soporte → señal alcista.
- **Cruce en Piso** → el precio rompió hacia abajo un soporte → señal bajista.
- **Rebote en Techo** → el precio fue rechazado hacia abajo desde una resistencia → señal bajista.

Cada resolución dispara Telegram + `EventLogStore.Append(..., "PisoTechoCruce"/"PisoTechoRebote",
"Piso"/"Techo", ...)` y el evento C# `OnPisoTechoResolvedEvent(evento, pisoTecho)`, que
`MultiChartForm` usa como disparador de la vigilancia de Bollinger en 15m RTH (ver §5).

**Diseño explícito — la etiqueta se queda en pantalla aunque se rompa:** si el precio abre por
debajo de un "Piso" (o por encima de un "Techo"), el watch interno se invalida y se limpia
(`InvalidateIfBrokenByOpen`), pero la etiqueta visual "Piso"/"Techo" en el chart **NO se quita** —
queda visible el resto de la sesión RTH, por pedido explícito, aunque el precio ya la haya cruzado.

## 3. Rebote en Zona de Demanda — panel 15m RTH+Overnight

El usuario dibuja manualmente una Zona de Demanda con la herramienta DZ/SZ (2 clicks → 2 líneas:
verde/Proximal arriba, roja/Distal abajo). Cada par de líneas donde el precio Proximal (demanda) >
Distal (oferta) se registra como zona a vigilar (`_demandZones`).

**Resolución** (`EvaluateDemandZoneRebounds`, por vela de 15m cerrada):
1. **Entra** en la zona (`zone.Entered`) cuando el Low de la vela toca o casi toca el Proximal
   (mismo criterio de proximidad del 30% que Cross-SMA/Piso-Techo).
2. **Se invalida** (`zone.Done`, sin rebote) si el Low perfora por debajo del Distal — la zona se
   "quemó".
3. **Confirma rebote** (`zone.Done`, con evento) si, tras entrar, el Close cierra de vuelta por
   encima del Proximal.

Dispara Telegram + `EventLogStore.Append(..., "DemandZoneRebound", "Alza", ...)`.

## 4. T-Line + SMA20 breakout — paneles 1h y 15m RTH

El usuario dibuja una T-Line (línea de tendencia, 2 clicks). **Ambos paneles (1h y 15m RTH) persisten
por símbolo en su propio archivo `TLineStore`** (tag "1h" y tag "RTH" respectivamente) — antes solo el
panel 1h persistía. Se pueden dibujar **varias T-Lines a la vez** en el mismo panel; cada una se
evalúa de forma independiente y dispara su propia señal como máximo una vez. Dibujar o borrar una
T-Line **ya no se mirrorea** entre los 2 paneles (cambio reciente — antes se mirroreaba a los 3
paneles del Live Chart, incluyendo el panel 3, que ahora perdió la herramienta T-Line por completo).
`TLineValueAt` extrapola el valor de la línea a cualquier tiempo (no solo entre sus 2 puntos ancla),
usando la pendiente entre ambos.

**Resolución** (`EvaluateTLineSignal`, por vela cerrada, una sola vez por T-Line dibujada):
```
Breakout alcista = open < T-Line
                    Y high  > T-Line  Y high  > SMA20
                    Y close > T-Line  Y close > SMA20

Breakout bajista = open > T-Line
                    Y low   < T-Line  Y low   < SMA20
                    Y close < T-Line  Y close < SMA20
```
Es decir: la vela abrió de un lado, y tanto su mecha como su cierre terminaron confirmados del otro
lado de AMBAS referencias (T-Line y SMA20) — un cruce simultáneo y limpio de las dos.

En el panel 1h dispara Telegram con la imagen combinada de los 3 charts
(`MultiChartForm.SendTLineSignalTelegramPushAsync`, no el panel individual) + `EventLogStore`. En
15m RTH es solo un evento en pantalla (`OnTLineSignalEvent`), sin push propio.

**Registro de creación vs. resolución (`CtRecordStore`/`CtLogWriter`):** además de `EventLogStore`,
cada T-Line dibujada (en cualquiera de sus 3 fuentes — panel 1h, panel 15m RTH, o el Daily popup,
ver abajo) crea un registro "Pendiente" en `CtRecordStore` en el momento en que se dibuja, que luego
se actualiza EN EL MISMO REGISTRO (no se agrega uno nuevo) cuando resuelve — a "Alza"/"Baja" si
rompe, o a "EliminadoSinResolver" si se borra antes de resolver. Es un único archivo JSON global por
PC (`C:\OptionsData\EventLog\ct_records_{MachineName}.json`, no rotado por día ni símbolo), del cual
`CtLogWriter` regenera automáticamente una nota `.md` completa cada vez que cambia algo.

**Tercera fuente de T-Line — Daily popup:** `DailyChartForm` (ver
[`LIVE_CHART_STREAMING.md`](LIVE_CHART_STREAMING.md)) también tiene su propia herramienta T-Line en
sus tabs "Hora"/"15 Min" (tags "DailyHora"/"Daily15Min" en `TLineStore`), que se mirrorea
automáticamente hacia el panel 1h/15m RTH del Live Chart correspondiente — dibujarla o borrarla ahí
también dibuja/borra la copia en el Live Chart, y viceversa no aplica (es de una sola vía, Daily →
Live Chart).

**Charts tab embebido — panel 2 independiente:** el Charts tab (`TwoPanelChartsControl`, ver
[`LIVE_CHART_ANALYSIS.md`](../LIVE_CHART_ANALYSIS.md)) también evalúa T-Line + SMA20 en su propio
panel 2 (15m RTH), de forma independiente a su panel 1 (1h) — mismo detector, misma fórmula, mismo
registro en `CtRecordStore`/Telegram, cada panel con sus propias líneas.

## 5. "Abriendo la Volatilidad" (Bollinger Bands) — panel 15m RTH

**Idea:** una vez que Piso/Techo (§2) confirma que el precio ya rompió o rebotó en una SMA de
referencia (1h), se busca el momento exacto de entrada mirando las Bandas de Bollinger del panel de
15m RTH — cuando se están "abriendo" (ensanchando) Y el precio en vivo alcanza la banda del lado
correcto, ese es el punto de entrada.

**Se arma** (`ArmVolatilityOpeningWatch(bullish)`) desde `MultiChartForm`, suscrito al
`OnPisoTechoResolvedEvent` del panel 1h — las 4 combinaciones posibles de Piso/Techo apuntan a una
dirección concreta:

| Resolución en 1h | Dirección | Banda vigilada |
|---|---|---|
| Cruce en Techo | Alcista (CALL) | Superior |
| Rebote en Piso | Alcista (CALL) | Superior |
| Cruce en Piso | Bajista (PUT) | Inferior |
| Rebote en Techo | Bajista (PUT) | Inferior |

Una vez armado, **sin límite de tiempo** (válido el resto de la sesión) hasta que dispare una vez
(`_volatilityOpeningFired`, no se re-arma después de disparar).

**Bandas de Bollinger** (`BollingerBandsAt`, calculadas en C# **solo para esta detección** — es una
copia independiente del cálculo que ya existe en `chart.html` para dibujar, período 20, 2
desviaciones estándar sobre los cierres de las velas de 15m ya cerradas).

**Evaluación** (`EvaluateVolatilityOpening`, en **cada tick en vivo** — no en el cierre de vela, a
pedido explícito para capturar el momento exacto — vía `UpdateLivePriceFromExternalSource` y
también en cada vela de 1 min cerrada como respaldo):

```
ancho_actual  = BandaSuperior(ahora) - BandaInferior(ahora)
ancho_previo  = BandaSuperior(hace 3 velas) - BandaInferior(hace 3 velas)
abriendo      = ancho_actual > ancho_previo   (las bandas se están ensanchando)

alcista: dispara si abriendo Y precio_en_vivo >= BandaSuperior(ahora)
bajista: dispara si abriendo Y precio_en_vivo <= BandaInferior(ahora)
```

Dispara Telegram + `EventLogStore.Append(..., "VolatilityOpening", "Alza"/"Baja", ...)`.

## 6. Rebote de vela diaria contra SMA20 diaria — panel 1h

Puramente informativo, evaluado una sola vez por instancia al cargar el historial
(`EvaluateDailyBounce`): agrega las velas horarias a diarias, y si la vela diaria de AYER (la
última ya cerrada, hoy nunca cuenta) rebotó contra la SMA20 diaria (misma fórmula de proximidad del
30%, solo Rebote — no hay detección de Cruce diario), se muestra un hint en el chart. **No** manda
Telegram ni se registra en `EventLogStore` — es solo una pista visual al abrir.

---

## Resumen de constantes compartidas

| Constante | Valor | Uso |
|---|---|---|
| `BounceProximityRatio` | 30% | Umbral de "casi tocó" para todo Rebote (Cross-SMA, Piso/Techo, Demand Zone, Daily Bounce) |
| Bollinger | período 20, 2 std dev | "Abriendo la Volatilidad", y el dibujo en `chart.html` (cálculos independientes) |
| `VolatilityWidthLookback` | 3 velas | Cuántas velas de 15m atrás se compara el ancho de banda para confirmar que se está abriendo |

## Archivos involucrados

- **`OptionsTrader.WinForms/ChartPanel.cs`** — todas las detecciones §1-§6 (app en vivo).
- **`OptionsTrader.WinForms/SimulatedChartPanel.cs`** — copia ported de §1, §2, §3, §4 y §5 (sin
  Telegram, log-only) para el Simulador — ver
  [`SIMULATOR_TELEGRAM_AND_LOGGING.md`](SIMULATOR_TELEGRAM_AND_LOGGING.md).
- **`OptionsTrader.WinForms/MultiChartForm.cs`** — orquesta el puente entre paneles (Piso/Techo en
  1h → arma Bollinger en 15m RTH) y el push de T-Line con el snapshot combinado.
- **`OptionsTrader.WinForms/TwoPanelChartsControl.cs`** — Charts tab embebido, evalúa T-Line + SMA20
  en su propio panel 2 (15m RTH) de forma independiente.
- **`OptionsTrader.WinForms/EventLogStore.cs`** — CSV acumulativo de todos los eventos
  (`C:\OptionsData\EventLog\events_log.csv`).
- **`OptionsTrader.WinForms/CtRecordStore.cs`** / **`CtLogWriter.cs`** — registro global de creación
  vs. resolución de cada T-Line (JSON + nota `.md` regenerada).
