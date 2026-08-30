# AI Content Factory — Phase 1

This is the Phase 1 foundation: a runnable .NET solution (API + Postgres + EF Core +
Hangfire + Serilog) and a React/Vite frontend that can create and list content
projects. No AI providers are wired yet — that's Phase 3+.

## 0. One important caveat

This project was written and type-checked, but **the .NET side has not been
compiled** in the environment that built it (that sandbox blocks access to
nuget.org, so `dotnet restore` couldn't run there). The frontend *was* built
and verified end-to-end (`tsc` + `vite build` both pass). So: treat the C#
code as carefully written but not yet build-verified — the very first thing
to do below is `dotnet restore` / `dotnet build` locally, which will catch
anything that needs a small fix.

## 1. Prerequisites

Install these first:

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 20+](https://nodejs.org/) (includes npm)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for Postgres, or run Postgres locally)
- [VS Code](https://code.visualstudio.com/) with the **C# Dev Kit** extension (for the API) — the frontend needs no special extension

Check versions in a VS Code terminal (`` Ctrl+` `` / `` Cmd+` ``):

```bash
dotnet --version   # should print 8.x
node --version     # should print v20.x or newer
docker --version
```

## 2. First-time setup

Open the project root folder (`AiContentFactory/`) in VS Code, then in the
integrated terminal:

```bash
# 1. Copy the env template and adjust if needed (defaults work for local dev)
cp .env.example .env

# 2. Restore .NET packages — this is the step that will surface any issue,
#    since it wasn't run when the code was generated
dotnet restore

# 3. Build to confirm everything compiles
dotnet build
```

If `dotnet build` reports errors, they'll almost certainly be minor (a missing
`using`, a package version mismatch) — paste the error back to me and I'll fix it.

### Create the database migration (one-time)

No EF Core migration exists yet — this also needs a real `dotnet restore`, so it
had to wait until now:

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate -p src/AiContentFactory.Infrastructure -s src/AiContentFactory.Api
```

This generates the `Migrations/` folder inside `AiContentFactory.Infrastructure`.
The API applies migrations automatically on startup (`Database.MigrateAsync()`
in `Program.cs`), so you don't need to run `dotnet ef database update` manually
— just make sure Postgres is running first (next step).

## 3. Run everything

**Option A — Docker Compose (API + Postgres together, easiest):**

```bash
docker compose up --build
```

- API: http://localhost:8080
- Swagger UI: http://localhost:8080/swagger
- Hangfire dashboard: http://localhost:8080/jobs
- Health check: http://localhost:8080/health

**Option B — Postgres in Docker, API from VS Code (better for debugging/breakpoints):**

```bash
docker compose up postgres -d
```

Then in VS Code: open `src/AiContentFactory.Api/Program.cs`, press **F5** (or
Run and Debug ▸ `.NET Core Launch`). VS Code will prompt to generate
`launch.json`/`tasks.json` the first time — accept the defaults.

Or from the terminal instead of F5:

```bash
dotnet run --project src/AiContentFactory.Api
```

### Frontend

In a second terminal:

```bash
cd frontend/ai-content-factory-web
npm install
cp .env.example .env
npm run dev
```

Open http://localhost:5173 — you should see the "AI Content Factory" page.
Create a content project through the form; it should appear in the table below
and be visible via `GET http://localhost:8080/content-projects` too.

## 4. Where things are

```
src/AiContentFactory.Domain/          entities, status-transition rules — no external dependencies
src/AiContentFactory.Application/     use cases (ContentProjectService), DTOs, repository interfaces
src/AiContentFactory.Infrastructure/  EF Core DbContext, Postgres config, Hangfire wiring
src/AiContentFactory.Api/             controllers, Program.cs, Dockerfile, appsettings
frontend/ai-content-factory-web/      Vite + React + TypeScript
docker-compose.yml                    Postgres + API for local dev
.env.example                          copy to .env — never commit .env
```

## 5. What's implemented so far (Phase 1 + 2 + 3)

**Phase 1 - Foundation:** create/list/update a `ContentProject`, guarded status
transitions, structured logging, health checks, Hangfire dashboard, Postgres
schema for all five entities.

**Phase 2 - Content Management:** full CRUD for Script (one per project,
upsert), Storyboard/Scenes (add/edit/remove), and Assets (manual metadata
registration - e.g. you generated a clip in Google Flow and want to track
it here; real provider-driven generation is Phase 4). New endpoints:

```
GET/PUT   /content-projects/{id}/script
GET       /content-projects/{id}/storyboard
POST      /content-projects/{id}/storyboard/scenes
PUT/DELETE /content-projects/{id}/storyboard/scenes/{sceneId}
GET/POST  /content-projects/{id}/assets
DELETE    /content-projects/{id}/assets/{assetId}
```

The frontend now has a Content Detail page (click a project title in the
list) with editable Script/Storyboard/Assets sections.

**Phase 3 - LLM Content Pipeline:** `POST /content-projects/{id}/generate`
enqueues a Hangfire job that runs, in order: Script Agent -> Storyboard
Agent -> Prompt Agent (per scene), calling the Gemini API and persisting
after each step so a failure partway through doesn't lose prior work. The
"Generate with AI" button on the detail page triggers this and polls until
it finishes.

**This needs a Gemini API key to actually run.** Get one at
https://aistudio.google.com/apikey, then either:
- Docker: put it in `.env` as `GEMINI_API_KEY=...`
- Local `dotnet run`: set the env var before running, e.g. (PowerShell)
  `$env:Llm__Gemini__ApiKey="..."` or add it to `appsettings.Development.json`
  under `Llm:Gemini:ApiKey` (don't commit a real key if you do that).

Verify the current model name/pricing/commercial-use terms at
https://ai.google.dev/gemini-api/docs before relying on this for real
content - `Llm:Gemini:Model` in appsettings defaults to `gemini-3.6-flash`
but this is exactly the kind of thing that moves independently of this
codebase.

Not yet: real asset generation (Veo/image providers), video rendering, QA
scoring, publishing. Those are Phases 4-7.

## 6. Trying the AI pipeline

1. Set `GEMINI_API_KEY` (see above) and start everything.
2. Create a content project (or use an existing one) with a real topic - the
   more specific, the better the script.
3. Open its detail page, click **Generate with AI**.
4. Watch the Hangfire dashboard (http://localhost:8080/jobs) - you'll see
   the job run through its steps. The page polls automatically and stops
   once status hits `StoryboardReady` or `Failed`.
5. If it fails, check the API container logs (Serilog prints the error) -
   almost always either a missing/invalid API key or a Gemini API error
   message that gets logged directly.

## 7. If something doesn't compile

Most likely spots, in order of likelihood:
1. A NuGet package version in one of the `.csproj` files doesn't exist or
   conflicts — `dotnet restore` will name the exact package.
2. `Microsoft.Extensions.Configuration`/`DependencyInjection` abstractions
   not resolving in `AiContentFactory.Infrastructure` — if so, add explicit
   `dotnet add src/AiContentFactory.Infrastructure package Microsoft.Extensions.Configuration.Abstractions`.

Paste any error here and I'll patch the file directly.
