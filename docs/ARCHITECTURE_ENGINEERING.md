# Software Engineering and Architecture Aspects

List of the main concepts, patterns, and decisions applied in the project, separated by component and by type: **Architecture** (structural decisions — how components are organized and communicate) vs **Engineering** (implementation techniques and practices applied within that structure).

---

## Windows App (WinForms)

### Architecture

- **Separation of layers by responsibility** — the UI (`Form1.cs`) only orchestrates; the business logic for expirations, orders, and OCC symbols lives in dedicated classes (`ExpirationDateResolver`, `OccOptionSymbol`).
- **Programming against interfaces (Dependency Inversion)** — `IMarketDataService`, `ITradingService`, `IBrokerAuthService` decouple the UI from the concrete Schwab implementation.
- **Factory + Strategy** — `CreateTradingService()`/`CreateMarketDataService()` resolve the implementation based on the active broker, instead of instantiating the concrete class at every point of use.
- **UI/broker business logic separation** — the WinForms app never talks to the own API for market data/trading, only for persistence (login, trades, screenshots).
- **Local Repository/Store pattern** — each preference persists in its own store (`SchwabCredentialsStore`, `TickerSettingsStore`, `BalanceStore`, etc.), all with the same `Load()`/`Save()` shape, isolating disk I/O from the rest of the code. `TickerSettingsStore` centralizes per-symbol config: broker, range, expiration, AWS/Telegram, polling interval, number of strikes per side (`StrikeCount`), and which daily SMAs to display (`DailySmaLinesEnabled`).
- **Creation vs. resolution as the same mutated record, not append-only** — `CtRecordStore` is the deliberate exception to the rest of the app's stores (all append-only): a T-Line is created as a "Pendiente" (Pending) record and THAT SAME record is updated in place upon resolution or deletion, instead of appending a new row — because the question it answers ("was the analysis set up?" vs. "was it fulfilled?") requires both moments to remain tied to the same event, not to two independent rows.
- **Event-driven full regeneration, not incremental** — `CtLogWriter` subscribes to a static event on `CtRecordStore` (`OnChanged`) and rewrites the entire `.md` file from scratch on every mutation, instead of applying a diff — simple and correct given the low expected volume of T-Lines, avoiding unnecessary incremental sync logic.
- **Deliberate parallel implementation, not inheritance** — `SimulatedChartPanel` (Simulator) reimplements `ChartPanel`'s (Live Chart) signal-detection logic as a completely separate class instead of inheriting or sharing code, so that no change in simulated mode can accidentally affect the live chart's behavior.

### Engineering

- **Non-blocking asynchronous polling** — `System.Windows.Forms.Timer` + `async/await` to refresh quotes every 6s without freezing the UI.
- **Simple time-based state machine** — `MarketHours` centralizes all market open/closed logic, avoiding scattered `DateTime.Now` calls throughout the code.
- **Token handling shared across instances** — the access token is persisted to disk and reused across polling cycles, avoiding redundant renewals.
- **Immutable records for local DTOs** — `SchwabCredentials`, `SchwabTokens`, `SelectedAccount`, `TradeRowTag` use `record`/`record with` instead of mutable classes.
- **Explicit fail-safe in critical operations** — in `CloseTradeRowAsync`, if the real order fails, the method aborts and does not mark the trade as closed in the log (avoids lying about a position's real state).

---

## API (ASP.NET Core)

### Architecture

- **Clean Architecture with unidirectional dependency** — `Domain ← Application ← Infrastructure ← API`, each layer in its own `.csproj`.
- **Repository Pattern** — `ITradeRepository`, `IScreenshotRepository`, `IUserRepository`, `IBrokerSettingRepository` abstract EF Core behind interfaces in Application.
- **Mandatory DTOs at the API boundary** — a domain entity is never exposed directly; the flow is Entity → DTO → Swagger → NSwag → TypeScript.
- **Thin controllers** — business logic lives in Application services (`TradeService`, `AuthService`, `ScreenshotService`), controllers only orchestrate.
- **Decoupled external storage adapter** — `IScreenshotStorage` with the `S3ScreenshotStorage` implementation, Application does not know about AWS S3 directly.

### Engineering

- **Dependency injection via native container** — everything registered in `Program.cs` (`AddScoped`, `AddSingleton`) instead of manual instantiation.
- **Stateless authentication with JWT Bearer** — issuer/audience/lifetime/signing key validation configured explicitly, with no server sessions.
- **Password hashing with BCrypt** — a password is never stored or compared in plain text.
- **Versioned EF Core migrations** — schema changes (including the nullable→backfill→NOT NULL pattern for production data) applied via `db.Database.Migrate()` at startup.
- **Per-environment configuration + secrets kept out of version control** — `appsettings.json` (public) vs. `appsettings.Local.json`/`appsettings.Production.json` (gitignored) for the JWT key, connection string, and AWS credentials.
- **Explicit CORS** — enabled to allow consumption from the Angular frontend on a different origin.
- **Authorization by identity, not by client** — each trade's `UserId` is derived from the JWT claim (`NameIdentifier`), never from a parameter sent by the client.
