# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# First-time setup
cp ".env example" .env              # fill in values, gitignored
docker compose up -d                # PostgreSQL x7, Redis, RabbitMQ
dotnet restore StockTracker.slnx

# Build / test (solution-wide)
dotnet build StockTracker.slnx --configuration Release
dotnet test StockTracker.slnx --configuration Release
# Test projects live under tests/ (xUnit + Moq + FluentAssertions 7.x, e.g. tests/StockTracker.SearchOrchestrator.Tests).

# Run a single service (each on its own fixed port)
dotnet run --project StockTracker.Gateway            # :8000
dotnet run --project StockTracker.Identity           # :5001
dotnet run --project StockTracker.Product             # :5002
dotnet run --project StockTracker.BrandDetection      # :5003
dotnet run --project StockTracker.StoreReference      # :5004
dotnet run --project StockTracker.SearchOrchestrator  # :5005
dotnet run --project StockTracker.Subscription        # :5006 (planned)
dotnet run --project StockTracker.Billing             # :5007 (planned)
dotnet run --project StockTracker.Notification        # :5008 (planned)
dotnet run --project StockTracker.BershkaScraper      # :5009 (RabbitMQ consumer; real Bershka API not wired yet — see .claude/ARCHITECTURE.md)

# When working on one service: bring up infra only, run/debug that service from the IDE
docker compose up -d

# EF Core migrations (run from the service's own project directory)
dotnet ef migrations add <Name>
# Migrations apply automatically at startup via db.Database.MigrateAsync() — no manual `dotnet ef database update` needed

# Health checks
curl http://localhost:8000/health/gateway
for port in 5001 5002 5003 5004 5005 5006 5007 5008 5009; do curl -s http://localhost:$port/health; done

# DB / cache / queue inspection
docker exec -it stocktracker-postgres psql -U stocktracker -l
docker exec -it stocktracker-postgres psql -U stocktracker -d identity_db
docker exec -it stocktracker-redis redis-cli keys "product:*"
# RabbitMQ management UI: http://localhost:15672
```

## Architecture

Database-per-service microservices on .NET 10 Minimal API. Client traffic enters through the YARP Gateway (`:8000`); service-to-service calls go **direct HTTP, bypassing the gateway** (lower latency, gateway outage doesn't break internal calls, internal endpoints stay off the public surface — see `.claude/ARCHITECTURE.md`). The only internal call implemented so far is `BrandDetection → Product` via a typed `ProductServiceClient`.

Gateway routes strip the `/api/{service}/` prefix before forwarding, e.g. `/api/product/lookup/123` → `/lookup/123`. Route table is in `.claude/ARCHITECTURE.md` / `README.md`. Note: YARP's `PathPattern` and `PathRemovePrefix` transforms cannot share a transform block — keep them in separate blocks.

Per-service layout (see `StockTracker.Product` or `StockTracker.BrandDetection` for the canonical shape): `Entities/` (EF Core models), `DTOs/` (records — never leak entities as API responses), `Data/` (DbContext), `Services/` (business logic), `Endpoints/` (Minimal API route groups via `MapGroup`), `Migrations/`, `Program.cs`.

**Brand resolution flow** (core domain logic, spans two services): a product code lookup first checks `product_db.ProductBrandMaps` (Redis-cached, 24h TTL, cache-aside). On a miss, BrandDetection Service matches the code against regex `BrandCodeSignatures` (per-brand patterns with a confidence level: Low/Medium/High). A single high-confidence match is written back to Product Service automatically; multiple candidates or low confidence surface a candidate list for the user to resolve manually via `POST /resolve/manual`. Successful resolutions land in `ProductBrandMaps` so later lookups hit cache directly.

**Cross-service data**: no foreign keys across databases. `BrandId` in `product_db` is a convention-based reference, not an FK constraint — consistency is maintained via API calls or (planned) RabbitMQ events, never direct cross-DB queries.

**Secrets**: every service reads `Environment.GetEnvironmentVariable("KEY") ?? configuration["Section:Key"] ?? throw ...` — env var first, `appsettings.json` fallback only for non-secret config. `appsettings.json` files use the literal placeholder `"REPLACE_WITH_ENV"` for anything sensitive; real values only ever live in the gitignored `.env`.

**Async/eventing (planned, Faz 2+)**: stock-check and result flow through RabbitMQ (`CheckStockCommand` → scraper → `StockResultEvent` → Notification Service), one queue per brand (`stock.check.{brandName}`), with Polly retry/circuit-breaker so one brand's scraper being blocked doesn't affect others.

**Mandatory Test Policy:** For every new feature, refactor, or bug fix, you MUST write corresponding unit or integration tests. A task is not considered complete without tests.

**Testing Tech Stack:** Use xUnit, Moq (or NSubstitute), and FluentAssertions for unit testing.

**Coverage:** Ensure test coverage includes both happy paths and edge cases (e.g., validation failures, exceptions) for Application Handlers (CQRS), Domain Models, and core business logic inside the `tests/` directory.

**Quiet Execution:** When verifying tests via `dotnet test`, summarize and report ONLY failed test outputs to keep token usage low.

## Git

Never run git commands automatically (no `git add`, `git commit`, `git push`, `git checkout`, etc.) unless the user explicitly asks for that specific action in the current message. This includes read-only-adjacent but stateful commands too — always ask first. Only inspect state (e.g. `git log`, `git diff`, `git status`) freely when needed for context.

## Project-specific conventions

Full detail lives in `.claude/` — read the relevant file before touching that area:
- `.claude/ARCHITECTURE.md` — service responsibilities, inter-service comms, known architectural decisions/risks
- `.claude/DATABASE.md` — per-service schema, seed data, migration notes
- `.claude/API_CONVENTIONS.md` — response envelope (`{success, data/error, meta}`), status codes, error code prefixing (`{SERVICE}_REASON`), pagination, idempotency
- `.claude/ENVIRONMENT_SETUP.md` — env var reference, Mac/Windows parity notes (LF line endings for `.sh` scripts, exec-bit loss on Mac→Docker)
- `.claude/SECURITY.md` — auth/token lifecycle, scraping legal risk, payment/webhook security notes
- `.claude/ROADMAP.md` — phase-by-phase status; check before starting new work to see what's actually done vs. planned
- `.claude/PENDING_INPUTS.md` — checklist of real-world credentials/accounts/decisions the user still needs to provide (Apple/Google/SendGrid/Firebase accounts, legal review, etc.) — every service was built against real APIs with graceful placeholder fallbacks; this tracks what's still a placeholder. Add a new item here whenever a phase needs a real external account/credential that isn't available yet.
- `.claude/CONTRIBUTING.md` — commit format (Conventional Commits), new-service checklist, new-scraper checklist

Key standards from `.claude/CONTRIBUTING.md`:
- Nullable reference types enabled everywhere
- Minimal API + `MapGroup`, not controllers
- DTOs (`record`) separate from entities; entities never returned directly from endpoints
- Async/await for all I/O
- **After finishing a phase or significant change, update**: `README.md` (status table/service list), `.claude/ROADMAP.md` (checklist), `.claude/DATABASE.md` (new schema), `.claude/ARCHITECTURE.md` (service status), `.claude/ENVIRONMENT_SETUP.md` (new env vars) — this is an explicit project rule, not optional cleanup
- New service checklist includes: add DB to `docker/postgres-init/01-init-databases.sql`, add YARP route (separate transform blocks), add `GET /health`, wire `db.Database.MigrateAsync()` on startup, bind `0.0.0.0:{port}`

## Rules & Constraints

**Language Preference:** Always communicate and provide code explanations in Turkish. Keep code comments, commit messages, and technical terms in English where standard, but write all responses and explanations in Turkish.