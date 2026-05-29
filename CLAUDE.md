# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Options trading system with three components: a **WinForms desktop app** that polls Schwab API for real-time quotes and executes trades, an **ASP.NET Core Web API** as the central business/data layer, and a separate **Angular frontend** (in `options-trader-web` repo) for read-only trade history visualization.

## Commands

Once the solution is scaffolded, use these commands:

```bash
# Build the entire solution
dotnet build OptionsTrader.sln

# Run the API
dotnet run --project OptionsTrader.API

# Run the WinForms app
dotnet run --project OptionsTrader.WinForms

# Run all tests
dotnet test

# Run a single test project
dotnet test OptionsTrader.Tests --filter "FullyQualifiedName~TradeServiceTests"

# Apply EF Core migrations
dotnet ef database update --project OptionsTrader.Infrastructure --startup-project OptionsTrader.API

# Add a new migration
dotnet ef migrations add <MigrationName> --project OptionsTrader.Infrastructure --startup-project OptionsTrader.API

# Regenerate TypeScript interfaces from Swagger (run after DTO changes)
nswag run nswag.json
```

## Architecture

Clean Architecture with strict layer separation. Each layer is its own `.csproj`:

```
OptionsTrader.sln
├── OptionsTrader.Domain          — Pure entities, no project references
├── OptionsTrader.Application     — Use cases, interfaces, DTOs
├── OptionsTrader.Infrastructure  — EF Core, Schwab API client, S3, RDS
├── OptionsTrader.API             — ASP.NET Core controllers, middleware, Program.cs
└── OptionsTrader.WinForms        — Windows Forms UI, polling loop, screenshot capture
```

**Dependency direction:** Domain ← Application ← Infrastructure ← API/WinForms

Both `API` and `WinForms` depend on `Application` + `Infrastructure`. `Domain` has zero external dependencies.

## Key Conventions

**Namespaces:** `OptionsTrader.[Layer].[Subfolder]` — e.g., `OptionsTrader.Application.DTOs.Trades`, `OptionsTrader.Infrastructure.Persistence`.

**DTOs are mandatory:** Never expose domain entities through the API. The type flow is:
```
Domain Entity → DTO (Application layer) → API → Swagger → NSwag → TypeScript Interface + Angular Service
```

**Layer responsibilities:**
- `Domain`: entities only (`Trade`, `Screenshot`, `BrokerSetting`) — no EF, no HTTP, no business logic
- `Application`: interfaces (`ITradeRepository`, `IScreenshotStorage`), DTOs, use case services
- `Infrastructure`: implements Application interfaces — EF Core `DbContext`, Schwab HTTP client, S3 upload
- `API`: thin controllers that call Application services; no business logic

## Domain Entities

**Trade:** Id, Symbol (SPY/QQQ/TSLA/AAPL), OptionType (Call/Put), StrikePrice, SpotPrice, ExpirationDate, EntryPrice (Ask), ExitPrice (Bid), TradeDate, Broker, Screenshots

**Screenshot:** Id, TradeId, S3Url, CapturedAt, Symbol

**BrokerSetting:** Id, BrokerName, ApiKey, ApiSecret, IsActive

## Business Rules

- Maximum **one trade per day**
- Maximum **3 screenshots per trade**
- Only **one active broker** at a time (from `BrokerSetting`)
- Supported brokers: Schwab (active), IBKR, ETrade (future)
- Trades are triggered by clicking a StrikePrice row in a WinForms `DataGridView`
- Screenshots are captured by fixed screen coordinates, per symbol (SPY, QQQ, TSLA, AAPL each have their own coordinates)
- **5 fixed users**, all with the same role — no admin role
- The Angular frontend is **read-only** (no trade execution)
- JWT Bearer tokens for authentication

## Infrastructure

- **Database:** SQL Server on AWS RDS — use EF Core migrations
- **Screenshots:** AWS S3
- **Real-time quotes:** Polling the Schwab API (WebSockets deferred)
- Develop locally first, then deploy API to AWS EC2

## Development Notes

- The developer is beginner-level in Angular/TypeScript — keep frontend explanations step-by-step and relate concepts to C# equivalents where helpful
- WinForms is preferred over WPF; Angular is preferred over React
- NSwag auto-generates TypeScript interfaces and Angular services from the Swagger spec after any DTO change — always run `nswag run nswag.json` after modifying DTOs
