# Options Trader

Documentación completa y detallada del proyecto. Para el detalle técnico de cada funcionalidad ver [`docs/FUNCIONALIDADES.md`](docs/FUNCIONALIDADES.md); para la guía de uso paso a paso ver [`docs/GUIA_USUARIO.md`](docs/GUIA_USUARIO.md).

---

## a. Descripción general del proyecto

Options Trader es un sistema de trading de opciones intradía compuesto por tres componentes:

- **App de escritorio WinForms** — cotizaciones de opciones en tiempo real (polling a Schwab), ejecución de órdenes (simuladas o reales) y registro de operaciones.
- **API ASP.NET Core** — capa central de negocio y datos: autenticación de usuarios, y persistencia de trades y screenshots.
- **Frontend Angular** (repo aparte `options-trader-web`) — visualización de solo lectura del historial de trades.

La WinForms habla **directamente con la API de Schwab** para datos de mercado y ejecución de órdenes; no pasa por la API propia. La API ASP.NET Core se usa solo para **login** y para **guardar trades y screenshots** en base de datos / S3.

---

## b. Stack tecnológico utilizado

| Capa | Tecnología |
|---|---|
| Escritorio | WinForms (.NET 8) |
| API | ASP.NET Core 8 |
| Base de datos | SQL Server (AWS) vía Entity Framework Core |
| Autenticación | JWT Bearer + BCrypt |
| Broker | Schwab Market Data + Trader API (OAuth2) |
| Screenshots | AWS S3 |
| Frontend | Angular (repo `options-trader-web`) |
| Sincronización de tipos | Swagger + NSwag (DTO → TypeScript) |

---

## c. Información sobre su instalación y ejecución

Requisitos: **.NET 8 SDK**, SQL Server accesible (connection string en `appsettings.json` de `OptionsTrader.API`), y credenciales de Schwab (se configuran desde la app, pestaña Settings).

```bash
# Compilar toda la solución
dotnet build OptionsTrader.slnx

# Aplicar migraciones EF Core (crea/actualiza la base de datos)
dotnet ef database update --project OptionsTrader.Infrastructure --startup-project OptionsTrader.API

# Correr la API
dotnet run --project OptionsTrader.API

# Correr la app de escritorio (requiere la API corriendo para el login)
dotnet run --project OptionsTrader.WinForms
```

Al abrir la WinForms se muestra primero una ventana de **login** (ver sección f); luego, en la pestaña **Settings**, se configuran las credenciales de Schwab, cuentas del broker, tickers y demás ajustes (ver [`docs/GUIA_USUARIO.md`](docs/GUIA_USUARIO.md)).

---

## d. Estructura del proyecto

Clean Architecture con separación estricta de capas:

```
OptionsTrader.sln
├── OptionsTrader.Domain          — Entidades puras (Trade, Screenshot, BrokerSetting, User)
├── OptionsTrader.Application     — Casos de uso, interfaces, DTOs
├── OptionsTrader.Infrastructure  — EF Core, cliente Schwab, almacenamiento S3
├── OptionsTrader.API             — Controllers ASP.NET Core, JWT, Swagger
└── OptionsTrader.WinForms        — UI de escritorio (login, polling, trades, screenshots)
```

**Dirección de dependencias:** `Domain ← Application ← Infrastructure ← API / WinForms`

---

## e. Funcionalidades principales

- **Login** con usuario/contraseña validado contra la base de datos (5 usuarios fijos, mismo rol).
- **Cotizaciones de opciones en tiempo real** (polling a Schwab), con doble grid (expiración actual y siguiente), filtros por rango/Counts/CALL-PUT.
- **Ejecución de trades**: modo simulado (`No Trade`), o real vía Schwab (`Trade` / `Trade-Target`), con confirmación obligatoria y sincronización con el precio real de llenado.
- **Trades y screenshots asociados al usuario logueado**: cada trade queda ligado al usuario que lo creó (derivado del JWT, nunca del cliente); cada usuario solo ve y puede cerrar sus propios trades.
- **Screenshots automáticos** de cada apertura/cierre de operación, subidos a S3 y asociados al trade.
- **Registro para backtesting**: CSV de cotizaciones (con griegos e IV) y snapshot diario de IV de apertura para IV Rank/Percentile propio.
- **Automatización por horario de mercado**: arranque diferido, reducción de cadencia después de las 11 AM, auto-captura de cierre a las 3:55 PM.

Ver [`docs/FUNCIONALIDADES.md`](docs/FUNCIONALIDADES.md) para el detalle completo de cada una.

---

## f. Usuario y contraseña de prueba

El sistema tiene 5 usuarios fijos sembrados en la base de datos (`user1` a `user5`), todos con el mismo rol y la misma contraseña de prueba:

| Usuario | Contraseña |
|---|---|
| `user1` | `Pass1234!` |

*(`user2`–`user5` usan la misma contraseña; sirven para distinguir sesiones simultáneas, no roles distintos.)*
