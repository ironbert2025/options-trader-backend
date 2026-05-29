# OptionsTrader — Project Context

## Description
Options trading system with three components:
- **Windows UI** to request quotes and execute trades
- **API** as the central business and data access layer
- **Angular Frontend** to visualize trade history and screenshots

---

## Technology Stack

| Layer | Technology |
|---|---|
| Windows UI | WinForms + Clean Architecture (.NET 8) |
| API | ASP.NET Core 8 Web API |
| Frontend | Angular 17 + TypeScript |
| Database | SQL Server on AWS RDS |
| ORM | Entity Framework Core |
| Authentication | JWT Bearer Tokens |
| Screenshots | AWS S3 |
| Primary Broker | Schwab API (credentials available) |
| Real-time Prices | Polling (WebSockets in the future with AI) |
| TS Type Generation | NSwag (C# DTO → Swagger → TypeScript interfaces + Angular services) |

---

## Repositories (two separate repos)

```
GitHub
├── options-trader-backend     ← All C# (Visual Studio 2022)
│   ├── OptionsTrader.Domain
│   ├── OptionsTrader.Application
│   ├── OptionsTrader.Infrastructure
│   ├── OptionsTrader.API
│   ├── OptionsTrader.WinForms
│   └── OptionsTrader.sln
│
└── options-trader-web         ← Angular (VS Code)
    ├── src/
    ├── angular.json
    └── package.json
```

---

## Backend Solution Structure

```
OptionsTrader.sln
├── OptionsTrader.Domain          ← Pure entities, no dependencies
├── OptionsTrader.Application     ← Use cases, interfaces, DTOs
├── OptionsTrader.Infrastructure  ← EF Core, Schwab API, S3, RDS
├── OptionsTrader.API             ← ASP.NET Core Web API controllers
└── OptionsTrader.WinForms        ← Windows Forms UI
```

### Project References
```
Domain         ← no references
Application    ← Domain
Infrastructure ← Application + Domain
API            ← Application + Infrastructure
WinForms       ← Application + Infrastructure
```

---

## Domain Entities (detail definition pending)

### Trade
- Id
- Symbol (SPY, QQQ, TSLA, AAPL)
- OptionType (Call / Put)
- StrikePrice
- SpotPrice
- ExpirationDate
- EntryPrice (Ask)
- ExitPrice (Bid)
- TradeDate
- Broker
- Screenshots (list)

### Screenshot
- Id
- TradeId
- S3Url
- CapturedAt
- Symbol

### BrokerSetting
- Id
- BrokerName
- ApiKey
- ApiSecret
- IsActive

---

## Business Rules

- Only one active broker at a time (defined in Settings)
- Supported brokers: Schwab (active), IBKR, ETrade (future)
- Maximum one trade per day
- Maximum 3 screenshots per trade
- Screenshots taken from specific coordinates per symbol:
  - SPY, QQQ, TSLA, AAPL → each has its own coordinates (provided by the user)
- Trades are executed by clicking the StrikePrice inside a DataGridView
- 5 users total, same role (no administrator)
- Frontend is read-only (view history and screenshots)

---

## AWS Infrastructure

| Service | Usage |
|---|---|
| RDS | SQL Server — primary database |
| S3 | Screenshot storage |
| EC2 | Optional for API deployment (to be defined) |

**Strategy:** develop everything locally first, then move API and frontend to AWS.

---

## Data Flow

```
WinForms (your PC)
  └── Polling → Schwab API → Real-time quotes
  └── Click on DataGridView → Execute trade
  └── Capture screenshot by coordinates → send to API

ASP.NET Core API
  └── Receives trades → saves to RDS
  └── Receives screenshots → uploads to S3
  └── Exposes history → Angular frontend

Angular Frontend
  └── Simple login (5 users, JWT)
  └── Dashboard with trade history
  └── View screenshots associated with each trade
```

---

## Type Flow Between Backend and Frontend

```
Domain Entity → DTO (Application) → API → Swagger → NSwag → TypeScript Interface + Angular Service
```

**Rule:** never expose domain entities directly. Always use DTOs.

---

## Project Phases

| Phase | Description | Status |
|---|---|---|
| 1 | Base solution structure + domain entities | ⏳ Pending |
| 2 | Schwab API connection + real-time quotes | ⏳ Pending |
| 3 | WinForms UI + DataGridView + trade execution | ⏳ Pending |
| 4 | Screenshots by coordinates | ⏳ Pending |
| 5 | ASP.NET Core API + RDS + S3 | ⏳ Pending |
| 6 | Angular frontend + history + visualization | ⏳ Pending |

---

## Developer Context

- Uses **Visual Studio 2022** for C# and **VS Code** for Angular
- Beginner in Angular and TypeScript — needs step-by-step explanations
- Has experience with AWS (EC2, RDS)
- Has active **Schwab API** credentials
- Personal project to learn full software development with AI
- Prefers WinForms over WPF
- Prefers Angular over React

---

## Important Notes

- Namespaces follow the pattern: `OptionsTrader.[Layer].[Subfolder]`
- Always strictly follow Clean Architecture
- Each layer in its own Visual Studio project
- Never expose domain entities in the API, always use DTOs
- NSwag for automatic type synchronization between C# and TypeScript
