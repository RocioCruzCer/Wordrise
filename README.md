# Wordrise

**One word a day. Rise every day.**

Wordrise is a responsive web app that turns English vocabulary practice into a short daily challenge.
Every day, all learners at the same CEFR level (A2, B1 or B2) get the same mystery word. They have
6 attempts to guess it, and each attempt unlocks progressive hints (part of speech, definition,
example sentence, first letter). When the round ends, the app reveals the definition, an example,
the Spanish translation and the pronunciation (Web Speech API). Missed words come back later through
spaced repetition.

## Tech stack

| Layer | Technology |
|---|---|
| Frontend | Razor views (ASP.NET Core MVC), HTML, CSS, vanilla JavaScript (`fetch`), installable PWA |
| Backend | C# / ASP.NET Core MVC on .NET 10 |
| Database | PostgreSQL with Entity Framework Core 10 (Npgsql) |
| Third-party integration | Web Speech API (`SpeechSynthesis`, built into the browser) |
| Deployment | Docker on Render (web service + managed PostgreSQL) |

## Project structure

```
Wordrise.slnx
src/Wordrise.Web/
  Controllers/        MVC controllers + JSON API for rounds (/api/rounds)
  Data/               DbContext, seeder, seed files (word bank, valid guesses)
  Domain/             Entities and pure game rules (evaluation, hints, score, camps, spaced repetition)
  Infrastructure/     Clock/time zone, options, connection string resolution
  Services/           Game, stats and account services
  ViewModels/  Views/  wwwroot/
tests/Wordrise.Tests/ Unit tests (xUnit)
docs/                 Spec, architecture, ADRs and the original project documents
Dockerfile  render.yaml  docker-compose.yml
```

## Getting started (local, Windows / PowerShell)

Requirements: .NET SDK 10, PostgreSQL 15+ (or Docker Desktop).

```powershell
# 1. Database (option A: Docker)
docker compose up -d db

# 2. Connection string (kept out of source control)
cd src/Wordrise.Web
dotnet user-secrets set "ConnectionStrings:Default" "Host=127.0.0.1;Port=55432;Database=wordrise;Username=wordrise;Password=wordrise_dev"
cd ../..

# 3. Restore tools and create the initial migration (first time only)
dotnet tool restore
dotnet ef migrations add InitialCreate --project src/Wordrise.Web --output-dir Data/Migrations

# 4. Run (migrations and word bank seed are applied on startup)
dotnet run --project src/Wordrise.Web
```

Open http://localhost:5180, create an account and play today's word.

### Tests

```powershell
dotnet test
```

## Progressive Web App

Wordrise can be installed from the browser (Chrome/Edge: install icon in the address bar or **Install app** in
Profile; iPhone/iPad: Safari → Share → Add to Home Screen). The service worker caches only static assets and an
offline page; pages with user data and the `/api` endpoints are never cached, so playing needs a connection.
Installation requires HTTPS (Render) or `localhost`. When `sw.js` precache or policy changes, bump `CACHE_VERSION`.

## Configuration

| Key | Default | Description |
|---|---|---|
| `ConnectionStrings:Default` | – | Npgsql connection string |
| `DATABASE_URL` | – | Used when no connection string is set (Render format `postgres://...`) |
| `Database:MigrateOnStartup` | `true` | Apply EF Core migrations and seed the word bank on startup |
| `Game:TimeZone` | `America/Mexico_City` | Time zone that defines when the daily word changes |
| `Game:ReviewIntervalsInDays` | `1,3,7,14,30` | Spaced repetition intervals |
| `Game:Camps` | Base 0, Slope 10, Ridge 30, Summit 100 | Camp thresholds in words learned |

## Deploying to Render

1. Push the repository to GitHub (the migration from step 3 must be committed).
2. In Render choose **New → Blueprint** and select the repository. `render.yaml` creates the
   Docker web service and the PostgreSQL database and injects `DATABASE_URL`.
3. Health check: `GET /health`.

## Game modes

| Mode | How often | Notes |
|---|---|---|
| Daily challenge | Once a day | Same word for everyone at a level; counts for the streak |
| Free practice | Unlimited | Random word from the chosen level |
| Practice past words | Unlimited | Words from your earlier daily challenges |
| Listening challenge | Unlimited | Hear the word, then spell it (not counted in accuracy) |
| Review | When due | Spaced repetition of missed words |
| Sprint | Unlimited | 60 seconds: unscramble as many words as possible |

### Engagement

- **XP and weekly leagues** (Monday–Sunday): one board per CEFR level and private groups joined with a 6-character code.
- **Daily goal** ring, **achievements**, **streak shields** (earned every 7-day streak, max 2), training **combos**.
- **Picture hints** (emoji) for concrete words, **sound effects** (Web Audio, can be turned off) and confetti.

## Game rules

- 6 attempts per round; the grid length equals the word length.
- Hints unlock automatically: part of speech (attempt 1), definition (2), example sentence (4), first letter (5).
  "Use a hint" unlocks the next one early for −10 points.
- Score: 100 − 10 per extra attempt − 10 per early hint (minimum 10 when won, 0 when lost).
- A missed word is scheduled for review the next day; each successful review pushes it further out.

## License and data

Code: to be defined by the team. Word list attribution: see `src/Wordrise.Web/Data/Seed/NOTICE.md`.
