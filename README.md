# AI Content Factory

## Wizard UX (read this first — it changes how you use the app)

The app is now a seven-step wizard rather than a control panel over the
pipeline:

```
1. Mẫu        - pick a content template (+ style / voice / caption presets)
2. Ý tưởng    - title and what the video is about  -> writes the script
3. Kịch bản   - review and edit the script, optionally score it
4. Ảnh mẫu    - generate/upload/approve a Character + Environment reference,
                or "Không cần loại này" - gates the build button
5. Dựng video - choose length + clip count, see the COST AND TIME ESTIMATE,
                then start
6. Xem trước  - preview each clip, regenerate any single clip, tune captions
7. Xuất bản   - play and download the finished MP4
```

Everything the pipeline does internally is hidden here: no raw statuses, no
provider names, no generation prompts (except the per-clip prompt, shown
editable on request), no job ids, and no "register an asset by typing a file
path" form (background music is a normal file upload now).

**Asset References (step 4)** — `AssetReference` entity + `asset_references`
table (migration `AddAssetReferences`, with a backfill from the old
`:reference` Assets). Two typed slots, `Character` and `Environment`, each
`Pending → Generated → Approved | Skipped`. `POST .../asset-references/generate`
(Hangfire job, N variants via `IImageGenerationProvider` / Nano Banana),
`POST .../upload`, `PUT .../{id}/approve`, `PUT .../{type}/skip`. Approve and
skip are **atomic `ExecuteUpdate`** (no tracked round-trip) so rapid UI
clicks can't raise `DbUpdateConcurrencyException`. `SceneAssetGenerator` loads
the approved images and passes them to Veo (already), image-to-video, **and
text-to-image** (`GeminiImageProvider` now sends them as `inlineData` parts).
The old auto-generate-references-inside-the-job behaviour and the whole
`:reference` Asset convention are gone.

**The old control-panel page still exists** at `/projects/:id/advanced` —
linked from the bottom of the wizard — for diagnosing a run that went wrong.

**The old control-panel page still exists** at `/projects/:id/advanced` —
linked from the bottom of the wizard — for diagnosing a run that went wrong.

Four preset families live in code (`Application/Presets/PresetCatalog.cs`),
not the database, so adding one needs no migration:

- **templates** — content format + niche; drives the script prompt
- **styles** — visual direction; replaces what was a hardcoded
  "dark fantasy, cinematic, moody lighting" string in `PromptAgent`
- **voices** — maps a label onto a Gemini prebuilt TTS voice
- **captions** — a full caption style, snapshotted onto the project so you
  can then tune it

**Captions are now ASS/libass, not SRT.** That is what makes font, colour,
outline, position, word grouping, per-word karaoke highlighting, and entry
animation possible — SRT can express none of them. This requires fonts inside
the API container (`fonts-dejavu-core`, `fonts-noto-core` in the Dockerfile);
without them Vietnamese diacritics render as empty boxes. Re-rendering with
different captions never regenerates clips, so it costs nothing.

Per-clip regeneration (`ClipRegenerationService`) shares one code path with
the full run (`SceneAssetGenerator`), so a clip rerun on its own is produced
by the same presets, budget checks, and cost tracking as one from the batch.
The clip it replaces is marked `Superseded` rather than deleted.

### Two ways to build the video (step 4)

- **Chuẩn (Veo)** — your clip plan, one Veo text-to-video call per clip. Any
  length, any clip count.
- **Google Flow (tiết kiệm)** — `GoogleFlowAssetGenerationService`: a fixed
  ~20s / 3-scene hook script it writes itself, a free Nano Banana image per
  scene, then Veo **image-to-video** (cheaper and faster per clip, capped by a
  daily credit quota via `GoogleFlowQuotaManager`). It ignores the clip plan.
  Shown only when `Llm:VideoGeneration:GoogleFlowEnabled` is true (default).

`POST /content-projects/{id}/generate-assets?mode=standard|googleflow` routes
through `IAssetGenerationDispatcher`. Only the video provider differs per mode
(keyed DI); the image provider is already Nano Banana in both.

### "Why does my 40s video say 12 minutes?" + cost controls

The estimate panel separates two numbers that used to be conflated:
`outputVideoSeconds` (how long the finished video is) vs `TotalSeconds` (how
long you WAIT while clips generate one after another). Per-clip wait is
`Pricing:SecondsPerClipGeneration` (default 90s for veo-fast).

**Real USD, not credits.** Video generation (Veo) bills real money per second
generated (`Pricing:VideoUsdPerSecond`, default $0.40/s) — the estimate's
`TotalCostUsd` is the actual dollar cost of the run, always shown, not a
"free until you exceed a credit pool" figure. Script, TTS and render are
treated as covered by the Gemini free tier; image generation is currently
free too (`Pricing:ImageUsd`). The `Llm:VideoGeneration:GoogleFlow` credit
figures (`DailyCredits`, `CreditsPerVideoClip`, `CreditsPerImage`) are shown
in the estimate as reference info only — nothing is stopped by them anymore.
The only hard spend guard is the monthly USD ceiling, `Budget:MonthlyLimitUsd`.

**"Google Flow" is Veo.** labs.google/flow has no API; this app's "Google
Flow" path calls the same Gemini Veo models, image-to-video, and just tracks
Google's Flow-tier credit allowance as reference info. There is no separate free
non-Veo video API. The genuinely-$0 route for a scene is the per-clip
**"Tải clip có sẵn"** upload — make it yourself in labs.google/flow and drop
the MP4 in (`POST .../storyboard/scenes/{id}/video`).

Generation is never stopped by the daily credit pool - Veo bills real USD per
second generated, so the only hard spend guard is the monthly USD ceiling,
`Budget:MonthlyLimitUsd` — not fixed at $50.

**Google Flow now honours your clip plan** (per-clip editor works in that mode
too); it only auto-writes its own 3-scene hook script when there is no plan or
you tick "⚡ Tạo nhanh" (`?autoHook=true`).

**Per-clip video vs still.** Each scene is `AiVideo` (a Veo clip, real cost —
`Pricing:VideoUsdPerSecond` × clip length) or `AiImage` (an AI still held with
a Ken-Burns zoom at render time, currently ~$0). The clip-plan step offers
three strategies: `AllVideo` / `CostOptimized` (video only on the hook and the
climax, stills for the rest — cheapest option that still uses video) /
`AllImages`. Each clip can then be flipped individually, and its generation
prompt is shown editable with a one-click "Tạo gợi ý" suggestion
(`POST .../storyboard/scenes/{id}/prompt/suggest`, `PUT .../prompt`,
`PUT .../visual-type`). The renderer detects a scene's still vs clip from
which scene-scoped asset is newer.

**This needs a migration** (`AddPresetsCaptionsAndProgress`): preset ids plus
two jsonb columns on `content_projects` for caption settings and job progress.
The API applies it on startup, so `docker compose up --build` is enough.

---

## Pipeline redesign (read this first if you built earlier phases)

The original AI-driven storyboard (LLM freely choosing scene count/duration/
visual-type mix) has been replaced with a simpler, user-driven flow closer
to how Google Flow/Veo actually works:

```
1. Generate Script       - Gemini writes the full narration (unchanged)
2. Run QA                - cheap script-only check, BEFORE any Veo spend
                            (moved earlier - scoring a finished render can't
                            undo already-spent generation cost)
3. Generate Reference     - 1-3 character/style images (Nano Banana), reused
   Images                  across every clip for consistency
4. Set Clip Plan          - YOU choose total duration + clip length (Veo
                            generates in ~8s units) - script auto-splits
                            evenly across that many clips; edit any clip's
                            narration by hand
5. Generate Clips         - every scene is now always a Veo clip (no more
                            image/motion-graphic fallback branching) using
                            the shared reference images
6. Render Video           - concatenates clips into one finished MP4 with
                            styled captions burned in. Re-runnable without
                            regenerating clips.
7. Approve / Reject
```

**Superseded by the wizard above** for day-to-day use: these are still the
underlying steps and still what the Advanced page exposes, but steps 2 and 3
are no longer things you trigger by hand (QA is a button on the script step;
reference images happen automatically before clip generation).

**No new database migration needed for this redesign** - it's a logic/
workflow change, not a schema change (the QA score table keeps its original
columns; unused dimensions just mirror the Overall score now rather than
being deleted, to avoid more migration churn).

**Removed:** the Storyboard Agent (AI no longer decides scene breakdown),
per-scene image/motion-graphic generation (every scene is video), and
post-render QA (moved before video generation instead).

**On CapCut captions/voice (came up in discussion):** CapCut has no public
automation API - only background removal, upscaling, and template search,
none of which cover editing/captions/rendering. So that isn't wired in as
an automatable step; the app's own Gemini TTS + FFmpeg caption pipeline is
what actually runs, now driven by caption presets (see the wizard section).

---

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

## 5. What's implemented so far (Phase 1 through 6)

**Phase 1 - Foundation:** create/list/update a `ContentProject`, guarded status
transitions, structured logging, health checks, Hangfire dashboard, Postgres
schema for all entities.

**Phase 2 - Content Management:** full CRUD for Script, Storyboard/Scenes,
and Assets (manual metadata registration).

**Phase 3 - LLM Content Pipeline:** `POST /content-projects/{id}/generate` -
Script Agent -> Storyboard Agent -> Prompt Agent, via Gemini.

**Phase 4 - Asset Pipeline:** `POST /content-projects/{id}/generate-assets`
walks every scene and generates a real visual asset (Veo 3.1 video for
`aiVideo` scenes, Gemini "Nano Banana" image for everything else - a
deliberate MVP simplification, see code comments in `AssetGenerationService`)
plus a TTS voice-over of the narration, saving everything to local disk via
`IFileStorage` and tracking estimated cost via `IAiUsageTracker`. Generation
stops (project -> Failed) if the monthly budget (`Budget:MonthlyLimitUsd`,
default $50) is exceeded partway through.

**Phase 5 - Video Rendering:** `POST /content-projects/{id}/render` uses
ffmpeg to scale/pad each scene to 9:16, mux in its voice-over, concatenate
scenes in order, burn in subtitles from the narration, and mix in
background music if you registered one as a project-level Asset. Produces
one finished MP4, saved as a project-level Video asset.

**Phase 6 - QA + Approval:** `POST /content-projects/{id}/run-qa` scores the
script + storyboard against the QA dimensions from the spec (Hook, Story,
Pacing, Visual/Audio/Subtitle Quality, Consistency, Factual Accuracy,
Platform Suitability). Scores >= the threshold (`Qa:MinimumOverallScoreToProceed`,
default 6.0) move the project to `AwaitingApproval`; lower scores send it
back to `Editing`. From `AwaitingApproval`, Approve/Reject reuse the
existing generic `POST /content-projects/{id}/status` endpoint.

The Content Detail page now has a 4-button pipeline (Generate with AI ->
Generate Assets -> Render Video -> Run QA), a video player for the final
render, a QA score panel, and Approve/Reject buttons.

**Known limitations, stated plainly:**
- **QA Agent doesn't watch the actual video.** It scores the script and
  storyboard *text*. True visual/audio quality scoring would need
  multimodal analysis of the rendered MP4 - a real capability of the Gemini
  API, but out of scope for this pass. `VisualQuality`/`AudioQuality`/
  `SubtitleQuality` scores are the model's best guess from descriptions
  alone, and the agent is prompted to say so in its notes.
- **The Veo response-parsing code (`VeoVideoProvider.GetVideoBytesFromOperationAsync`)
  is the single least-verified piece of this codebase.** The request/poll
  pattern is confirmed against current docs, but the exact JSON shape of a
  *completed* operation wasn't independently tested against a live call -
  the code searches defensively for common field names rather than
  assuming one exact path. If it throws "could not locate video data",
  that method is where to look.
- **Cost estimates in `AssetGenerationService` are rough**, not pulled from
  a live pricing API - verify against https://ai.google.dev/gemini-api/docs/pricing
  before trusting the budget numbers for real spend decisions.
- Every scene visual gets generated fresh each run (no dedup/idempotency
  check) - re-running Phase 4 on the same project creates new Video/Image/
  Voice assets rather than reusing prior ones. Clean up manually via the
  Assets section if needed.

Not yet: Phase 7 (Publishing to YouTube/TikTok/Instagram), Phase 8
(Analytics), Phase 9 (Automation).

## 6. New setup step for Phase 4-6: another migration

Phase 4 and 6 added two new tables (`ai_usage_records`, `qa_scores`) that
didn't exist in your earlier migration. Generate a second migration:

```bash
dotnet ef migrations add AddUsageTrackingAndQa -p src/AiContentFactory.Infrastructure -s src/AiContentFactory.Api
```

It applies automatically on next API startup, same as before.

## 7. ffmpeg requirement

The Docker image now installs `ffmpeg` automatically (added to the
Dockerfile). If you run the API with plain `dotnet run` (not Docker), you
need `ffmpeg` and `ffprobe` on your PATH yourself - rendering will fail
with a clear "ffmpeg exited with code..." error otherwise. On Windows,
download a build from https://www.gyan.dev/ffmpeg/builds/ and add its
`bin` folder to PATH; on Mac, `brew install ffmpeg`.

## 8. Trying the full pipeline

1. Set `GEMINI_API_KEY`. Get one free at https://aistudio.google.com/apikey.
   Docker: put it in `.env`. Local `dotnet run`: set the env var before
   running, e.g. (PowerShell) `$env:Llm__Gemini__ApiKey="..."`.
   Video generation additionally needs **billing enabled** on that key's
   Google project - Veo has no free tier at all, unlike text/image/TTS.
   Check current pricing/availability at https://ai.google.dev/gemini-api/docs/veo.
2. Create a content project, open its detail page.
3. Click through in order: **1. Generate Script** -> **2. Run QA** (cheap,
   catches a weak script before any Veo spend) -> **3. Generate Reference
   Images** -> set your total duration / clip length in the **Clip Plan**
   section and click **Generate Clip Plan** -> **4. Generate Clips** (the
   slow one - Veo takes 1-3 min per clip, sequentially) -> **Render Video**
   (toggle captions on/off first) -> **Approve**/**Reject**.
4. Watch http://localhost:8080/jobs for job status, and check API logs if
   something fails - Serilog prints the real exception.
5. If captions look bad on render, uncheck "Burn in captions" and
   re-render - it reuses the existing clips, no new Veo spend.

## 9. If something doesn't compile

Most likely spots, in order of likelihood:
1. A NuGet package version in one of the `.csproj` files doesn't exist or
   conflicts — `dotnet restore` will name the exact package.
2. `Microsoft.Extensions.Configuration`/`DependencyInjection` abstractions
   not resolving in `AiContentFactory.Infrastructure` — if so, add explicit
   `dotnet add src/AiContentFactory.Infrastructure package Microsoft.Extensions.Configuration.Abstractions`.

Paste any error here and I'll patch the file directly.
