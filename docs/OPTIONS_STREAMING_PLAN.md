# Plan: opciones en vivo por WebSocket (`LEVELONE_OPTIONS`)

Investigación + plan para agregar un modo "WebSocket" al grid de OptionsChain, como alternativa al polling REST actual (intervalo configurable por símbolo, 6s por defecto — ver `TickerSettingsStore.PollingIntervalSeconds`). El número de strikes por lado (`strikeCount`) también es configurable por símbolo (`TickerSettingsStore.StrikeCount`, default 40) — cualquier tamaño de fallback REST que se planee para este modo WebSocket debe tomar ese valor en vez de un número fijo. **No implementado todavía** — este documento es la referencia para cuando se arranque.

---

## Investigación (confirmada contra código real de dos SDKs distintos)

### Nombre del servicio
**`LEVELONE_OPTIONS`** — sin guion bajo entre LEVEL y ONE (mismo tipo de error que tuvimos con `LEVEL_ONE_EQUITIES` vs `LEVELONE_EQUITIES`, ver [`docs/LIVE_CHART_STREAMING.md`](LIVE_CHART_STREAMING.md) — confirmar de nuevo contra `ws_raw.log` cuando se implemente, no asumir que este nombre está 100% verificado en producción todavía).

### Formato del símbolo de suscripción (distinto al ticker normal)
No es "SPY" — es un identificador de contrato específico con padding fijo:
```
{Underlying, padded a 6 chars con espacios}{YYMMDD}{C|P}{Strike a 8 dígitos}
```
Ejemplos reales confirmados en código: `"GOOGL 240712C00200000"`, `"AAPL 240517P00190000"` (200.00 → `00200000`).

### Mapeo de campos (LEVEL_ONE_OPTION)
| Campo | Índice | Campo | Índice |
|---|---|---|---|
| symbol | 0 | strikePrice | 24 |
| description | 1 | contractType (C/P) | 25 |
| bidPrice | 2 | underlying | 26 |
| askPrice | 3 | expirationMonth | 27 |
| lastPrice | 4 | timeValue (≈ExtrinsicValue) | 29 |
| totalVolume | 8 | expirationDay | 30 |
| openInterest | 9 | dte (días a vto.) | 31 |
| volatility (IV) | 10 | **delta** | 32 |
| quoteTime | 11 | **gamma** | 33 |
| tradeTime | 12 | **theta** | 34 |
| intrinsicValue | 13 | **vega** | 35 |
| bidSize/askSize | 20/21 | rho | 36 |
| netChange | 23 | underlyingPrice | 39 |

Cubre 1 a 1 todos los campos que ya usa `OptionQuoteDto` (Bid, Ask, SpotPrice, IntrinsicValue, ExtrinsicValue, Delta, Gamma, Theta, Vega, IV).

**Fuentes:**
- [Schwabdev stream.py — GitHub](https://github.com/tylerebowers/Schwabdev/blob/main/schwabdev/stream.py)
- [Schwabdev stream_demo.py — GitHub](https://github.com/tylerebowers/Schwabdev/blob/main/docs/examples/stream_demo.py)
- [schwab-py streaming docs](https://schwab-py.readthedocs.io/en/latest/streaming.html)
- [schwab-td-ameritrade-streamer index.d.ts — GitHub](https://github.com/allensarkisyan/schwab-td-ameritrade-streamer/blob/main/index.d.ts)

---

## El problema de fondo

Con equities (`LEVELONE_EQUITIES`) nos suscribimos a un puñado fijo de símbolos (SPY, QQQ...) que nunca cambian. Con opciones, **el set de contratos que interesa cambia todo el tiempo** — a medida que el spot se mueve, entran y salen strikes del rango OTM mostrado en el grid. El WebSocket no entrega "la cadena completa" de una vez, solo empuja updates de los contratos puntuales ya suscritos.

**Conclusión: seguimos necesitando REST periódicamente** — para descubrir qué strikes existen/entran en rango. El WebSocket reemplaza el refresco de precio de los contratos ya conocidos, no el descubrimiento de la cadena.

---

## Plan de implementación

### 1. UI — nuevo radiobutton
Pestaña Quotes, cerca de "Start Polling"/"Fetch Quotes": radiobuttons **"Polling"** / **"WebSocket"**. Selección persistida (nuevo `QuoteSourceSettingsStore`, mismo patrón que el resto de los settings locales en `%AppData%\OptionsTrader\`).

### 2. Capa de streaming nueva (`SchwabStreamerClient`)
- `SubscribeLevelOneOptions(IEnumerable<string> occKeys)` / `UnsubscribeLevelOneOptions(...)` — mismo patrón que `SubscribeLevelOneEquity`.
- Nuevo helper `BuildOptionStreamKey(symbol, strike, expDate, isCall)` — arma el string con el padding exacto de arriba.
- Parseo en `HandleMessage` para `serviceName == "LEVELONE_OPTIONS"`, mapeando los campos de la tabla a un nuevo evento `OnOptionTick(string occKey, OptionQuoteDto quote)`.

### 3. Descubrimiento de la cadena (REST, mucho menos frecuente)
- Al entrar en modo WebSocket: un fetch REST inicial (reusa `GetOptionsChainAsync`, ya existente) para conocer los strikes disponibles de la expiración actual (y next, si "Hide Next ExpDate" no está marcado).
- Refresco periódico (ej. cada 30-60s, no cada 6s) solo para: (a) detectar strikes nuevos que entraron en rango si el spot se movió, y (b) rearmar la suscripción (UNSUBS de los que salieron de rango + ADD de los nuevos).

### 4. Actualización del grid
- Modo Polling: sin cambios, rebuild completo cada 6s vía `PopulateQuotesGrid`.
- Modo WebSocket: cada `OnOptionTick` actualiza **solo la fila de ese contrato** en `dgvQuotes` (bid/ask/griegos), sin rehacer el grid entero — necesita un índice `occKey → DataGridViewRow`.

### 5. Multi-instancia / hub compartido
Comparte la MISMA conexión Schwab (una por cuenta) que ya usan velas y L1 de equities. Hay que extender otra vez el relay del hub local (`CandleHubServer`/`CandleHubClient`, ver [`docs/LIVE_CHART_STREAMING.md`](LIVE_CHART_STREAMING.md)) para relayear también ticks de opciones — cada instancia necesita recibir solo los ticks de SUS contratos suscritos.

### 6. Riesgos / cosas a verificar en vivo antes de confiar en esto
- Confirmar que Schwab empuja **griegos** (delta/gamma/etc.) con la misma frecuencia que bid/ask, o si los recalcula con menor frecuencia.
- Confirmar el nombre exacto del servicio contra tráfico real (`ws_raw.log`) — mismo tipo de sorpresa que `LEVEL_ONE_EQUITIES` vs `LEVELONE_EQUITIES`.
- Manejo de "churn" de suscripciones: si el spot se mueve rápido, entrar/salir de rango constantemente podría generar muchos ADD/UNSUBS — decidir un buffer (suscribirse a un rango un poco más ancho que el mostrado) para no estar resuscribiendo todo el tiempo.
- Volumen de suscripciones: ~6-10 strikes por lado (call/put) más "Next ExpDate" ≈ 20-40 contratos simultáneos por instancia — confirmar que Schwab no tenga un límite bajo de símbolos por conexión para `LEVELONE_OPTIONS`.

### Orden sugerido de construcción (cuando se arranque)
1. `SubscribeLevelOneOptions`/parseo en `SchwabStreamerClient`, validado a mano contra `ws_raw.log` con 1-2 contratos antes de nada más (mismo protocolo que se usó para depurar `LEVELONE_EQUITIES`).
2. `BuildOptionStreamKey` + pruebas unitarias/manuales del formato exacto.
3. UI del radiobutton + `QuoteSourceSettingsStore`.
4. Lógica de descubrimiento de cadena + rearmado de suscripción (churn).
5. Actualización incremental del grid.
6. Extender el hub local para multi-instancia.
