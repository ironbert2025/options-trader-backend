# Secuencia de envío de orden desde la app móvil (thinkorswim)

Este documento muestra, paso a paso, el flujo real que sigue un trader al enviar una orden de opciones desde la app móvil del broker (thinkorswim by Schwab). Es el mismo flujo descrito en la sección **"Origen y motivación del proyecto"** del [`README.md`](../README.md): desde que el trader decide entrar hasta que la orden efectivamente se envía al mercado pasan **no menos de 20 segundos**, tiempo suficiente para que el precio se mueva de forma significativa respecto al observado al momento de la decisión.

Esta es precisamente la razón de ser de la **Windows App** de este proyecto: automatizar y comprimir este mismo flujo a **menos de 3 segundos**.

📹 **Video de la secuencia completa (~20 segundos):** [Ver en Google Drive](https://drive.google.com/file/d/1nJOHh_ZM6y3UUC_i5ikqjJbbLqsSNDmv/view?usp=drive_link)

---

## Paso a paso

### 1. Pantalla de inicio del móvil
Localizar el ícono de la app thinkorswim entre las demás apps del teléfono.

<img src="images/mobile/01-home-screen.png" alt="Home screen" width="280">

### 2. Login en la app
Autenticación con Login ID y Face ID.

<img src="images/mobile/02-login.png" alt="Login" width="280">

### 3. Selección de cuenta
Elegir la cuenta sobre la que se operará (paperMoney o Live Trading), entre las cuentas vinculadas.

<img src="images/mobile/03-select-account.png" alt="Selección de cuenta" width="280">

### 4. Overview de la cuenta
Revisar el estado de la cuenta (posiciones, Net Liq) antes de operar.

<img src="images/mobile/04-account-overview.png" alt="Overview de cuenta" width="280">

### 5. Búsqueda del símbolo
Buscar el ticker a operar (por ejemplo SPY) en el buscador de símbolos.

<img src="images/mobile/05-symbol-search.png" alt="Búsqueda de símbolo" width="280">

### 6. Detalle del símbolo y cadena de opciones
Ver el precio spot, Bid/Ask e IV del subyacente, y las fechas de expiración disponibles.

<img src="images/mobile/06-spy-detail.png" alt="Detalle SPY" width="280">

### 7. Selección del strike
Elegir el strike y tipo de opción (Call/Put) dentro de la cadena de opciones (option chain).

<img src="images/mobile/07-option-chain.png" alt="Cadena de opciones" width="280">

### 8. Armado de la orden
Configurar tipo de orden (Limit/Market), cantidad y revisar el costo estimado del trade.

<img src="images/mobile/08-order-entry.png" alt="Armado de orden" width="280">

### 9. Ajuste final antes de revisar
Confirmar cantidad, tipo de orden a mercado y cuenta antes de pasar a revisión.

<img src="images/mobile/09-order-entry-market.png" alt="Orden a mercado" width="280">

### 10. Confirmación de la orden simulada
Pantalla de confirmación con el detalle completo: costo del trade, break-even, máxima ganancia/pérdida y efecto en el buying power.

<img src="images/mobile/10-order-confirmation.png" alt="Confirmación de orden" width="280">

### 11. Orden ejecutada (Filled)
Verificar en el historial de órdenes del día que la orden fue ejecutada (Filled) y a qué precio.

<img src="images/mobile/11-order-filled.png" alt="Orden ejecutada" width="280">

### 12. Resumen de cuenta post-operación
Revisar el resumen de la cuenta (P/L del día, posiciones activas, órdenes) luego de operar.

<img src="images/mobile/12-account-summary.png" alt="Resumen de cuenta" width="280">

---

## Comparación con la Windows App

| | App móvil (thinkorswim) | Windows App (este proyecto) |
|---|---|---|
| Pasos manuales | Login → cuenta → búsqueda de símbolo → cadena de opciones → armado de orden → revisión → envío → verificación | Un clic sobre la fila del strike en el grid de cotizaciones |
| Tiempo aproximado | ~20 segundos | < 3 segundos |
| Riesgo de slippage | Alto — el precio puede moverse considerablemente durante el proceso manual | Mínimo — la orden se envía y confirma casi instantáneamente |

Ver también [`docs/FUNCIONALIDADES.md`](FUNCIONALIDADES.md) y [`docs/GUIA_USUARIO.md`](GUIA_USUARIO.md) para el detalle del flujo equivalente en la Windows App.
