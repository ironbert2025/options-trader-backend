# Aspectos de Ingeniería y Arquitectura de Software

Lista de los principales conceptos, patrones y decisiones aplicados en el proyecto, separados por componente y por tipo: **Arquitectura** (decisiones estructurales — cómo se organizan y comunican los componentes) vs **Ingeniería** (técnicas y prácticas de implementación aplicadas dentro de esa estructura).

---

## Windows App (WinForms)

### Arquitectura

- **Separación de capas por responsabilidad** — la UI (`Form1.cs`) solo orquesta; la lógica de negocio de expiraciones, órdenes y símbolos OCC vive en clases dedicadas (`ExpirationDateResolver`, `OccOptionSymbol`).
- **Programación contra interfaces (Dependency Inversion)** — `IMarketDataService`, `ITradingService`, `IBrokerAuthService` desacoplan la UI de la implementación concreta de Schwab.
- **Fábrica + Strategy** — `CreateTradingService()`/`CreateMarketDataService()` resuelven la implementación según el broker activo, en vez de instanciar la clase concreta en cada punto de uso.
- **Separación UI/lógica de negocio del broker** — la WinForms nunca habla con la API propia para market data/trading, solo para persistencia (login, trades, screenshots).
- **Patrón Repository/Store local** — cada preferencia persiste en su propio store (`SchwabCredentialsStore`, `TickerSettingsStore`, `BalanceStore`, etc.), todos con la misma forma `Load()`/`Save()`, aislando el I/O a disco del resto del código. `TickerSettingsStore` centraliza config por símbolo: broker, rango, expiración, AWS/Telegram, intervalo de polling, cantidad de strikes por lado (`StrikeCount`), y qué SMAs diarias mostrar (`DailySmaLinesEnabled`).
- **Creación vs. resolución como un mismo registro mutado, no append-only** — `CtRecordStore` es la excepción deliberada al resto de los stores de la app (todos append-only): un T-Line se crea como un registro "Pendiente" y ESE MISMO registro se actualiza in-place al resolver o borrarse, en vez de agregar una fila nueva — porque la pregunta que responde ("¿se armó el análisis?" vs "¿se cumplió?") requiere que ambos momentos queden ligados al mismo evento, no a dos filas independientes.
- **Regeneración completa dirigida por evento, no incremental** — `CtLogWriter` se suscribe a un evento estático de `CtRecordStore` (`OnChanged`) y reescribe el archivo `.md` completo desde cero en cada mutación, en vez de aplicar un diff — simple y correcto dado el volumen bajo de T-Lines esperado, evita lógica de sincronización incremental innecesaria.
- **Implementación paralela deliberada, no herencia** — `SimulatedChartPanel` (Simulador) reimplementa la lógica de detección de señales de `ChartPanel` (Live Chart) como una clase completamente separada en vez de heredar o compartir código, para que ningún cambio en el modo simulado pueda afectar por accidente el comportamiento del chart en vivo.

### Ingeniería

- **Polling asíncrono no bloqueante** — `System.Windows.Forms.Timer` + `async/await` para refrescar cotizaciones cada 6s sin congelar la UI.
- **Máquina de estados simple por horario** — `MarketHours` centraliza toda la lógica de mercado abierto/cerrado, evitando `DateTime.Now` disperso por el código.
- **Manejo de tokens compartido entre instancias** — el access token se persiste a disco y se reutiliza entre ciclos de polling, evitando renovaciones redundantes.
- **Records inmutables para DTOs locales** — `SchwabCredentials`, `SchwabTokens`, `SelectedAccount`, `TradeRowTag` usan `record`/`record with` en vez de clases mutables.
- **Fail-safe explícito en operaciones críticas** — en `CloseTradeRowAsync`, si la orden real falla, el método aborta y no marca el trade como cerrado en el log (evita mentir sobre el estado real de una posición).

---

## API (ASP.NET Core)

### Arquitectura

- **Clean Architecture con dependencia unidireccional** — `Domain ← Application ← Infrastructure ← API`, cada capa en su propio `.csproj`.
- **Repository Pattern** — `ITradeRepository`, `IScreenshotRepository`, `IUserRepository`, `IBrokerSettingRepository` abstraen EF Core detrás de interfaces en Application.
- **DTOs obligatorios en el borde de la API** — nunca se expone una entidad de dominio directamente; el flujo es Entity → DTO → Swagger → NSwag → TypeScript.
- **Controladores delgados** — la lógica de negocio vive en servicios de Application (`TradeService`, `AuthService`, `ScreenshotService`), los controllers solo orquestan.
- **Adaptador de almacenamiento externo desacoplado** — `IScreenshotStorage` con implementación `S3ScreenshotStorage`, la Application no conoce AWS S3 directamente.

### Ingeniería

- **Inyección de dependencias vía contenedor nativo** — todo registrado en `Program.cs` (`AddScoped`, `AddSingleton`) en vez de instanciación manual.
- **Autenticación stateless con JWT Bearer** — validación de issuer/audience/lifetime/signing key configurada explícitamente, sin sesiones de servidor.
- **Hashing de contraseñas con BCrypt** — nunca se guarda ni compara contraseña en texto plano.
- **Migraciones EF Core versionadas** — cambios de esquema (incluyendo el patrón nullable→backfill→NOT NULL para datos de producción) aplicados vía `db.Database.Migrate()` en el arranque.
- **Configuración por entorno + secretos fuera del control de versiones** — `appsettings.json` (público) vs `appsettings.Local.json`/`appsettings.Production.json` (gitignored) para JWT key, connection string y credenciales AWS.
- **CORS explícito** — habilitado para permitir el consumo desde el frontend Angular en otro origen.
- **Autorización por identidad, no por cliente** — el `UserId` de cada trade se deriva del claim del JWT (`NameIdentifier`), nunca de un parámetro enviado por el cliente.
