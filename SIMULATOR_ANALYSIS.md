# Simulador — Análisis Técnico

Ámbito: `OptionsTrader.WinForms/SimulatorForm.cs`, `SimulatedChartPanel.cs`, `SimTradesStore.cs`, `SimulationDataLoader.cs`, `SimEventLogMarkdownWriter.cs`.

## 1. Propósito y layout de UI

`SimulatorForm` ("Simulador", 1190x1000) es una ventana de **replay** completamente independiente del polling en vivo de `Form1` y del streaming de `MultiChartForm` (puede tenerse abierta simultáneamente con cualquiera de las dos). **No hace paper-trading contra un feed en vivo** — reproduce snapshots de la cadena de opciones grabados previamente para un símbolo+día real (capturados mientras la app corría en vivo), vía `SimulationDataLoader`.

**Flujo de uso:** elegir Symbol (`_cmbSymbol`, desde `TickerSettingsStore`) y Date (`_cmbDate`, desde `SimulationDataLoader.GetAvailableDates`), click "Cargar" (`LoadSelectedDay`) carga los `SimulationStep` de ese día + contexto de velas horarias/intradía y posiciona la vista exactamente en las 9:30:00 ET (apertura de mercado), no en el primer paso grabado — vía `FindClosestStepIndex`, el mismo helper que usa "Ir a hora". Luego se avanza:
- Manual: `◀ Atrás` / `Adelante ▶` (`Step(-1/1)`), o `+1 Min` (`StepOneMinute`, procesa PnL/auto-close en cada paso intermedio, sin saltarlos).
- Automático: Play/Pause con `Timer` a velocidad seleccionable (1/3/5/10 pasos/seg), auto-pausa al llegar al final de los datos.
- Salto directo: panel "Go to time" con botones de hora (9–15) y minuto (00/15/30/45) — salta al paso más cercano.

Cada paso ejecuta `RenderCurrentStep`: repuebla el grid de la cadena de opciones, re-renderiza los 3 charts hasta ese punto, y refresca PnL/auto-close/Min-Max de los trades demo abiertos.

**Layout:** toolbar superior (Symbol/Date/Cargar/Play-Pause/pasos); `_dgvChain` (grid de cadena, mismas 12 columnas/formato/coloreado que el grid en vivo de Form1); controles a la derecha (`_grpCounts`, `_grpContracts` — **locales, nunca tocan los stores reales**; `_grpTrade` con solo "No Trade"/"No Trade-Target", sin opciones reales de broker; `_grpSpeed`; `_pnlGoToTime`; `_pnlSmaEvents` con botones Cross-SMA 20/40/100/200 + T-Line + Clear; `_pnlDzSz`); `_chartsHost` (3 `SimulatedChartPanel`, mismo ratio 2:2:3 que `MultiChartForm`); `_dgvTrades` (grid de trades demo, 16 columnas); `_txtEventLog` (log negro/verde de eventos + aperturas/cierres manuales).

## 2. Implementación del chart vs. Live Chart

`SimulatedChartPanel` reutiliza el **mismo `chart.html` / Lightweight Charts / WebView2** que el `ChartPanel` en vivo (mismo path, mismo cache-busting, mismas funciones JS: `configureSmas`, `configureBollinger`, `loadHistory`, `markStrike`, `markPisoTechoRefLine`, `toggleTLine`, `toggleDzSz`, `markPisoTecho`, `updateFirstRebound`, `updatePuntoMedio`, `updateBollingerWidening`/`updateBollingerDelta`, `configureOvernightBands`, `resetViewForNewDay`, `configureVisibleDays`, `addMirroredZoneLine`/`removeMirroredZonePair`/`clearMirroredZoneLines`).

Pero **deliberadamente NO es una subclase ni variante de `ChartPanel`** — el propio comentario de la clase lo dice explícitamente: "completely separate so nothing here can ever affect the live chart's behavior, even by accident." Es una implementación paralela completa (`SimulatedChartPanel : Panel`) con copias propias de la lógica de detección. Diferencias estructurales clave:
- Sin conexión de streaming ni fetch REST — solo renderiza la lista de velas que `SimulatorForm` empuja vía `CargarHastaPasoAsync(candles, visibleDays)`, con reemplazo completo (`loadHistory`) en vez de la máquina de estados incremental del `ChartPanel` en vivo.
- "Vela cerrada" se detecta artificialmente: `EvaluateNewlyClosedCandles` trata toda vela salvo la última (asumida en formación) como cerrada, comparando contra el largo de la llamada anterior para detectar pasos hacia atrás (rollback de todo el estado de watches/secuencias) o hacia adelante (evalúa cada vela nueva en orden).
- Las instancias WebView se reutilizan entre cargas de día distintas (una instancia por panel por vida del formulario), a diferencia del chart en vivo (creado una vez por sesión) — de ahí la necesidad explícita de `ResetViewForNewDayAsync()` al cargar un día, para limpiar pan/zoom residual.

## 3. Análisis automáticos: portados vs. ausentes

**Portados (presentes)**, en su mayoría copias 1:1 de la lógica de `ChartPanel`:

| Feature | Estado en Simulador | Persistencia/Telegram |
|---|---|---|
| Cross-SMA (Cruce/Rebote 20/40/100/200) | Presente (`EvaluateCrossings`/`ToggleCrossMonitor`/`AdvanceCrossSequence`), panel 1h | Solo log (`OnCrossSequenceEvent`), sin Telegram/persistencia |
| T-Line + SMA20 breakout | Presente (`EvaluateTLineSignal`), **múltiples líneas independientes en memoria** (`_tLines`, lista + `_tLineSignalFiredFor` como set), sin store — portado desde el Live Chart, ya no tiene el límite viejo de 1 sola línea | Solo log (`OnTLineSignalEvent`) |
| Demand/Supply Zone rebote (DZ/SZ) | Presente (`EvaluateDemandZoneRebounds`/`EvaluateSupplyZoneRebounds`), armado en Overnight, mirroreado a 15m RTH | **Única excepción**: SÍ escribe en `EventLogStore` (`events_log.csv`, el mismo archivo compartido con la app en vivo) — "per explicit request" |
| PM (Punto Medio) | Presente (`EvaluatePuntoMedioSlope`/`MarkPuntoMedioAsync`), 1h y 15m RTH, con coordinación cross-panel de tamaño ("grande" si ambos coinciden) igual que `MultiChartForm` | Solo evento, sin persistencia |
| BB widening + Δ | Presente (`EvaluateBollingerWideningLabel`), 1h y 15m RTH | Puramente visual, sin log |
| Piso/Techo (Cruce/Rebote, "1er Rebote", ref-line) | Presente, evaluado **una vez por carga de día** (no una vez por proceso de app como en vivo — un día simulado nuevo es el equivalente más cercano a "una sesión premarket nueva"). Ref-line ahora también termina en 16:00 ET del día simulado (`GetSessionEndFakeEpoch`, mismo cambio portado desde `ChartPanel`) en vez de correr hasta el borde del chart | Solo log (`OnPisoTechoOutcomeEvent`) — explícitamente **NO** escribe en `events_log.csv` ("per explicit request"), a diferencia de DZ/SZ |
| Abriendo la Volatilidad | Presente, armado desde `SimulatorForm` cuando el panel 1h resuelve un Piso/Techo | Solo log |
| Daily bounce ("Rebote Diario") | Presente (`SimulatorForm.EvaluateDailyBounce`), una vez por carga de día contra la última vela diaria antes de `_simDate` | Solo log |

**Ausente/no implementado en el Simulador:**
- **Prev-day High/Low (H-Lines rojas auto-dibujadas)** — `DrawPrevDayHiLoAsync`/`EvaluatePrevDayHiLoAsync`/`OnPrevDayHiLoDebugEvent`/`markPrevDayHiLo` **no tienen ninguna contraparte** en `SimulatedChartPanel` (confirmado por grep — cero referencias). Es el **único** análisis automático de la lista original que falta; todo lo demás (Piso/Techo, T-Line+SMA20, DZ/SZ rebote, PM, BB widening, daily bounce) tiene equivalente portado.
- **"BB" en premarket** — en el Live Chart, "BB" ahora se evalúa también durante el premarket real (antes de 9:30 AM ET). El Simulador no tiene un concepto de "premarket" propio (arranca directo con los pasos del día grabado), así que este cambio no aplica/no tiene contraparte aquí.

**Divergencias recientes Live Chart vs. Simulador:**
- **Panel 3 sin T-Line**: en el Live Chart, panel 3 perdió la herramienta T-Line. El Simulador solo tiene T-Line en el panel 1h de todas formas, así que este punto no genera divergencia real.
- **ATH (checkbox/línea de referencia)**: no tiene contraparte en `SimulatedChartPanel`/`SimulatorForm` (sin coincidencias de `AllTimeHigh`/`ATH`) — no portado.
- **Marcadores de borde de Bollinger**: SÍ portados (`SetBollingerEdgeMarkersVisibleAsync`, `enableBollingerEdgeMarkers()`).
- **Línea blanca de entrada/cierre de trade en panel 2 (15m RTH)**: el Live Chart la agregó recientemente a ese panel; en el Simulador, `MarkEntrySpotAsync` **solo se llama sobre `_fullChart`** (el equivalente al panel 3/Overnight) — el panel 15m RTH del Simulador **no** dibuja esta línea. Divergencia confirmada.
- **Spread ≥ 6 para deshabilitar Strike**: SÍ está replicado en el Simulador (`c9f97bd`/comentarios en Form1 confirman "misma regla en Live Chart + Simulador"), a diferencia de las divergencias de arriba.
- **Log "PM + BB alineados" (backtesting)** — `MultiChartForm.CheckPmBbAlignment` (nuevo, ver `LIVE_CHART_ANALYSIS.md`) no está portado al Simulador; `SimulatorForm` no rastrea el cruce de color entre paneles para BB, solo para PM (tamaño del label, no logging).
- **"Expuesto" (texto premarket junto a la línea azul)** — mismo motivo que "BB en premarket": no hay línea azul premarket en el Simulador.

## 4. Herramientas de dibujo manual: Simulador vs. Live Chart

El `ChartPanel` en vivo expone: Rect, Rect Gris (persistido), H-Line (**un solo botón**, sobre el panel 2, arma el modo dibujo en los 3 paneles a la vez — dibujar o borrar en cualquiera se mirrorea a los otros 2), Arrow, flecha vertical (persistida, en 1h), además de DZ/SZ y T-Line.

`SimulatedChartPanel` solo implementa **dos**:
- **T-Line** (`ToggleTLineModeAsync`/`ClearTLineAsync`) — presente, solo en memoria (sin store).
- **DZ/SZ** (`ToggleDzSzModeAsync`/`ClearDzSzAsync`) — presente, armado en el panel Overnight, mirroreado al panel 15m RTH (`AddMirroredZoneLineAsync`/`RemoveMirroredZonePairAsync`), igual patrón que la app en vivo.

**Ausentes en el Simulador:** Rect, Rect Gris, H-Line (herramienta manual — tampoco existe la variante auto-dibujada de prev-day Hi/Lo, ver sección 3), Arrow, y la herramienta de flecha vertical. No existen métodos/eventos `ToggleRect`, `ToggleRectGris`, `ToggleHLine`, `ToggleArrow` ni de flecha vertical en `SimulatedChartPanel.cs`.

## 5. Persistencia en disco

**`SimTradesStore.cs`** — logger CSV mínimo, de solo-append, explícitamente documentado como "completamente separado de `OpenTradesStore`/la API real de Trades... nunca se lee de vuelta para restaurar estado":
- **Ruta:** `C:\OptionsData\Simulator\Trades\{Symbol}\{Symbol}_{yyyyMMdd}.csv` — **un archivo por símbolo y por día simulado**.
- **Formato:** `Symbol,SimDate,OptionType,StrikePrice,Contracts,EntryStepTime,EntryPrice,ExitStepTime,ExitPrice,PnL,PnLPercent`. Header solo si el archivo es nuevo; se añade una fila al cerrar cada trade (manual o auto-close por target).
- **No se limpia por fecha** en el sentido de borrar archivos previos — es solo-append, acumula entre sesiones por símbolo/día. Sin embargo, el grid `_dgvTrades` y `_openSimTrades` en memoria **sí** se limpian en cada `LoadSelectedDay` — el grid nunca se restaura desde el CSV, es un log de "revisar después", de solo escritura.
- Envuelto en try/catch — "best-effort logging, nunca debe romper el simulador."

**Otro estado del Simulador (no persistido):**
- Selecciones de Counts/Contracts — campos locales de sesión, nunca se escriben en `CountsSettingsStore`/`ContractsSettingsStore`.
- T-Line — solo en memoria, sin store equivalente a `TLineStore`.
- Zonas DZ/SZ — sin store equivalente a `RectGrisStore`; solo listas en memoria (`_demandZones`/`_supplyZones`), limpiadas en `ClearDzSzAsync`.
- Texto del event log (`_txtEventLog`) — se limpia y repuebla en cada `LoadSelectedDay`; en pantalla no sobrevive el cierre de la ventana, pero cada línea sí queda persistida en el `.md` de `SimEventLogMarkdownWriter` (y los 2 eventos DZ/SZ además van a `EventLogStore`, ver sección 3/7).
- `_forcedStrikes` — se limpia en cada carga de día.

## 6. Fuente de datos — sin conexión Schwab en vivo

El Simulador **no** se conecta a ningún feed en vivo. Trabaja exclusivamente con datos históricos pre-grabados vía `SimulationDataLoader`:
- `GetAvailableDates(symbol)` — enumera qué días tienen datos grabados.
- `LoadDay(symbol, date)` — carga los `SimulationStep` del día (snapshots de cadena de opciones tal como los grabó la app en vivo originalmente).
- `LoadHourlyCandlesWithContext(symbol, date)` — velas horarias con 7 días de contexto previo (igual que el default de `ChartPanel.LoadHistoryAsync` para el panel 1h).
- `LoadUnderlyingCandlesWithContext(symbol, date, contextDays: 3)` — velas intradía con 3 días de contexto, compartidas por los paneles 15m RTH y RTH+Overnight (agregadas al vuelo vía `CandleAggregation`).

No hay ninguna llamada a `SchwabClient`, streaming o polling REST en ninguno de los dos archivos. El comentario de clase de `SimulatedChartPanel` lo confirma: "NO streaming connection and NO REST history fetch." Las únicas lecturas externas son configuración local de solo lectura: `TickerSettingsStore`, `BalanceStore`, `PositionSizeSettingsStore`, `TargetSettingsStore`.

## 7. Telegram / EventLogStore / SimEventLogMarkdownWriter

El Simulador está casi completamente aislado/local, con **dos excepciones deliberadas**:
- **Sin integración de Telegram en ningún lado** — cada análisis automático (Cross-SMA, T-Line, PM, BB widening, Piso/Techo, Abriendo la Volatilidad, daily bounce) es explícitamente "solo log" ("no Telegram, no persistence, per request (\"es un simulador\")").
- **Todo lo que pasa por `LogSimEvent`** (T-Line, Cross-SMA, DZ/SZ, Piso/Techo, Abriendo la Volatilidad, Daily Bounce, aperturas/cierres manuales de trade) **se persiste vía `SimEventLogMarkdownWriter.AppendEvent`**, un archivo `.md` por corrida — no se pierde al cerrar la ventana del Simulador. Ruta: `C:\ObsidianVault\RobertVault\0010-Options\12-DailyTrades\{runDate}_{MachineName}_{Symbol}_Sim_{dataDate}_EventLogs.md`, donde `runDate` es hoy (cuándo se corrió el replay) y `dataDate` es el día histórico replayado — un mismo símbolo/día puede replayarse varias veces en fechas distintas, y cada corrida deja su propio archivo.
- **`EventLogStore.Append` SÍ se llama** además, pero únicamente para los 2 eventos de Demand/Supply Zone rebote:
```csharp
_fullChart.OnDemandZoneReboundEvent += (caption, price, proximal, distal) =>
{
    LogSimEvent(caption);
    EventLogStore.Append(_symbol, "15Min", "DemandZoneRebound", "Alza", caption, price,
        $"Proximal={proximal:F2};Distal={distal:F2}");
};
```
  (y simétrico para SupplyZoneRebound/"Baja") — escribe en el **mismo `events_log.csv` persistido** que usa la app en vivo, marcado explícitamente como excepción "per explicit request", además del `.md` de `SimEventLogMarkdownWriter` que ya recibe todo evento por igual.
- Fuera de eso, aislamiento total: stores separados (`SimTradesStore` vs `OpenTradesStore`), configuración separada (campos locales vs. los stores reales), sin estado compartido en tiempo real con `Form1`/`MultiChartForm` más allá de lecturas de configuración de solo lectura.

## 8. Diferencias clave con el Live Chart

| Aspecto | Live Chart (`ChartPanel`) | Simulador (`SimulatedChartPanel`) |
|---|---|---|
| Fuente de datos | Schwab en vivo (streaming + REST history) | Snapshots grabados (`SimulationDataLoader`), sin red |
| Relación de clases | — | Implementación paralela, NO hereda de `ChartPanel` (aislamiento intencional) |
| Piso/Techo — cuándo se evalúa | Una vez por proceso de app (premarket, antes de 9:30) | Una vez por carga de día simulado |
| Piso/Techo — persiste en `events_log.csv` | Sí | **No** (explícitamente excluido) |
| Demand/Supply Zone rebote — persiste en `events_log.csv` | Sí | **Sí** (única excepción portada tal cual) |
| Prev-day High/Low auto-dibujado | Sí | **No existe** |
| "BB" en premarket / línea azul "Expuesto" | Sí | **No existe** (sin concepto de premarket en el replay) |
| Log "PM + BB alineados" (backtesting) | Sí (`crossLog`, una línea por transición) | **No portado** |
| Piso/Techo ref-line — límite de sesión | Termina en 16:00 ET de hoy | Termina en 16:00 ET del día simulado (portado igual) |
| Herramientas manuales | T-Line, H-Line (botón único, 3 paneles), Rect, Rect Gris, DZ/SZ, Arrow, Flecha Verde/Roja | Solo T-Line y DZ/SZ |
| Persistencia de T-Line/Arrows/Rect Gris | Sí (`TLineStore`/`VerticalArrowStore`/`RectGrisStore`) | No — todo en memoria, se pierde al cambiar de día/cerrar |
| Trades — dónde se guardan | Vía `Form1` → API ASP.NET Core → SQL Server (RDS) | `SimTradesStore` — CSV local por símbolo/día, **nunca se lee de vuelta** |
| Telegram | Sí, en casi todos los análisis (Cruce/Rebote, T-Line+SMA20, DZ/SZ, Abriendo la Volatilidad) | **No, en ningún caso** |
| Grid de opciones/counts/contracts | Conectado a los stores reales (`CountsSettingsStore`, etc.) | Selecciones locales, nunca tocan los stores reales |
| Múltiples instancias | Un proceso de WinForms por ticker, un solo Live Chart activo por símbolo | Una sola ventana de Simulador, independiente de cuántos Live Charts estén abiertos |
