# Narration Script — TFM General Video

Narration script in English for the general presentation video of the system, intended for evaluation as a **Master's Thesis (TFM)** by an instructor from a programming school. The focus is technical/academic (architecture, use value, real-world operation), not commercial.

Based on 6 topical videos already recorded. They are listed here in the order they are used within the overall sequence (not the order in which they were recorded) — each one validates an independent functional unit of the complete system:

| # | Unit validated | Duration | Link |
|---|---|---|---|
| 1 | Problem (mobile app latency) | 3:02 | [View on Google Drive](https://drive.google.com/file/d/1nJOHh_ZM6y3UUC_i5ikqjJbbLqsSNDmv/view?usp=drive_link) |
| 2 | Architecture (EC2, API, DB, Frontend) | 3:35 | [View on Google Drive](https://drive.google.com/file/d/18NINnvj5l4r6Ng3eLJrGT7lyHDyf8PP1/view?usp=drive_link) |
| 3 | Initial configuration | 10:30 | [View on Google Drive](https://drive.google.com/file/d/1UfKnCQHXm6t8L5TMtxPKqsDTK2qp-dVp/view?usp=drive_link) |
| 4 | Demo Trade (manual + Target) | 11:00 | [View on Google Drive](https://drive.google.com/file/d/1h6SgEcapmUeXq-Pj9rqyZHXs5XLwC_rQ/view?usp=drive_link) |
| 5 | Real Trade (manual + Target) | 26:50 | [View on Google Drive](https://drive.google.com/file/d/1YYPI0IUAyQmQBwjAmUzbsx_zLg1j6YIH/view?usp=drive_link) |
| 6 | Frontend / history | 2:21 | [View on Google Drive](https://drive.google.com/file/d/1chPR6KzGCo6LwpNI0QiIeOuSonI22ot8/view?usp=drive_link) |
| **Total** | | **57:18** | |

---

## Suggested sequence

1. **Problem** (trimmed mobile clip — row 1 of the table) — states the measurable problem that motivated the project.
2. **System architecture** (row 2) — full technical scope: Clean Architecture, EC2, API, DB, Frontend.
3. **Initial configuration** (row 3) — evidence of configurable design.
4. **Demo trade** (row 4) — solution to the stated problem, with both exit types.
5. **Real trade** (row 5) — proof of operation with real money.
6. **Frontend / history** (row 6) — complete traceability of every operation.
7. **Closing** — technical summary, no video.

---

## Script

### Intro

"This project was born from a real problem I observed at an options trading academy in Miami, where students send their orders to the broker from their phone."

### 1. Problem

"This is the current flow: login, symbol search, strike selection, order assembly, review, and submission. From the moment the decision is made until the order reaches the market, no less than twenty seconds go by — enough time for the price to move significantly."

📹 [View on Google Drive](https://drive.google.com/file/d/1nJOHh_ZM6y3UUC_i5ikqjJbbLqsSNDmv/view?usp=drive_link)

### 2. System architecture — EC2/API/DB/Frontend video

"To solve this, I developed a complete platform using Clean Architecture in .NET 8: four layers with unidirectional dependency — Domain, Application, Infrastructure, and the presentation layers, API and WinForms.

The backend is deployed on an AWS EC2 instance: the ASP.NET Core API runs on one port, the Angular frontend on another, both served independently. Persistence uses SQL Server with Entity Framework Core and versioned migrations, and screenshots of each operation are stored in S3.

Authentication is JWT Bearer, with five fixed users seeded in the database. The desktop app talks directly to the Schwab API for market data and order execution — the in-house API is only used for login and for persisting trades and screenshots."

📹 [View on Google Drive](https://drive.google.com/file/d/18NINnvj5l4r6Ng3eLJrGT7lyHDyf8PP1/view?usp=drive_link)

### 3. Initial configuration — Program setup video

"The application is configurable per user: Schwab credentials, broker accounts, the list of tickers with their ranges and expiration, position size, and exit target percentage — all of it is persisted locally and restored when the app is reopened."

📹 [View on Google Drive](https://drive.google.com/file/d/1UfKnCQHXm6t8L5TMtxPKqsDTK2qp-dVp/view?usp=drive_link)

### 4. Demo trade — Simulated video (manual exit + automatic Target)

"Here I execute a trade in simulated mode. A single click on the strike row in the grid triggers the order — the entire process that took twenty seconds on mobile is completed here in under three.

I can close the position manually at any time, or use Trade-Target, which automatically places a limit exit order at the predefined profit percentage and executes it without intervention."

📹 [View on Google Drive](https://drive.google.com/file/d/1h6SgEcapmUeXq-Pj9rqyZHXs5XLwC_rQ/view?usp=drive_link)

### 5. Real trade — Real video (manual exit + automatic Target)

"The same flow, now with a real trade against the broker: the order is submitted, confirmed via HTTP, and the fill price is synced in real time. I close one position manually and another via Trade-Target, to demonstrate that both exit mechanisms work the same way with real money as in simulated mode."

📹 [View on Google Drive](https://drive.google.com/file/d/1YYPI0IUAyQmQBwjAmUzbsx_zLg1j6YIH/view?usp=drive_link) (26:50)

**Key moments (to jump directly without watching the full video):**
- **SPY** — entry: `11:20` · manual exit: `20:18`
- **QQQ** — entry: `16:46` · automatic exit via Trade-Target (target % crossed): `23:24`

### 6. Frontend — Trade history video

"Each trade is associated with the user who executed it and persisted in the database along with its screenshots. The Angular frontend exposes this information in read-only mode: entry and exit price, result, date, and the images automatically captured at the moment of opening and closing the trade."

📹 [View on Google Drive](https://drive.google.com/file/d/1chPR6KzGCo6LwpNI0QiIeOuSonI22ot8/view?usp=drive_link)

### Closing

"Stack: .NET 8, ASP.NET Core, SQL Server, Angular, deployed on AWS EC2 with S3 for storage. The result is a platform that solves a measurable operational latency problem, with complete traceability of every trade and an architecture ready to scale to other brokers. Thank you."

---

