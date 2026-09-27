# Google Flow Runner

An **optional**, standalone Node/TypeScript + Playwright tool that automates the
Step 5 "Google Flow" video-generation workflow: it reads the ready-made prompts
this app already builds, drives [labs.google/flow](https://labs.google/flow)
(Google's free browser-based AI video tool) to generate the clips, downloads
them, and imports them back into the app via its existing Step 6 upload
endpoint.

**This tool is not required.** The manual workflow - copy the prompt from
Step 5, paste it into Google Flow yourself, download the clip, upload it back
in Step 6 - keeps working exactly as before and remains the primary/fallback
path. This package only exists to save the copy/paste/upload steps for people
who want it.

This tool never calls a paid API. It drives the free labs.google/flow web UI
using your own logged-in Google session and your own free daily credits,
exactly like a human clicking around would.

---

## ⚠️ Read this before using it

**The selectors in `src/flowSelectors.ts` are placeholders.** They were
written without any ability to browse or log in to the real
`labs.google/flow` site, so they are educated guesses about a plausible DOM
shape, not verified selectors. **This tool has not been run end-to-end
against the real Google Flow site.**

Before this tool can actually work:

1. A human with a real Google account needs to open `labs.google/flow`, log
   in, and start a generation manually while inspecting the DOM (browser
   DevTools).
2. Every selector in `src/flowSelectors.ts` needs to be replaced with a real
   one (prefer `data-testid`/`aria-label`/`role` over CSS classes, which
   Google changes often).
3. `waitForRender()` in `src/googleFlowPage.ts` needs its completion signal
   confirmed against a real run - right now it's a guess (`renderCompleteIndicator`
   / `renderFailedIndicator` in `flowSelectors.ts`).
4. The exact Flow entry URL (`FLOW_URL` in `src/googleFlowPage.ts`) may need
   to point at a specific project/workspace route rather than the bare
   marketing page.

All of the Flow-specific DOM knowledge is deliberately isolated in
`src/flowSelectors.ts` and `src/googleFlowPage.ts` - nothing else in this
package needs to change once those two files are filled in with real values.

## What's implemented and unit-tested vs. what's unverified

| Layer | Status |
|---|---|
| `queue.ts` (which scenes to generate, in what order) | Implemented + unit-tested |
| `stateMachine.ts` (run/scene state transitions) | Implemented + unit-tested |
| `download.ts` (file path + validity checks) | Implemented + unit-tested |
| `apiClient.ts` (talks to this app's own HTTP API) | Implemented, shape-verified against `client.ts`/controllers, not covered by an automated test in this pass |
| `importer.ts` / `statusStore.ts` / `runner.ts` (orchestration) | Implemented, logically sound, **not run against a real browser/site** |
| `flowSelectors.ts` / `googleFlowPage.ts` / `browserSession.ts` (Playwright + real Flow DOM) | Structurally implemented, **selectors are placeholders, entirely unverified against the real site** |

## Install

```bash
cd automation/google-flow-runner
npm install
npm run build
```

Playwright also needs its bundled Chromium binary the first time (only used if you opt into `--launch-own-browser`; the default CDP-attach mode below uses your real, already-installed Chrome instead):

```bash
npx playwright install chromium
```

## ⚠️ Before your first run: launch Chrome yourself and log in

**Do not skip this.** By default, this tool attaches to a real Chrome window
*you* start and log into - it does **not** launch its own browser. This is
not optional polish: Google's sign-in flow reliably blocks a
Playwright-launched browser ("Chrome for Testing", Playwright's bundled
Chromium build) as an untrusted automation environment, often before you can
even enter credentials. Since you launch and authenticate in a completely
ordinary Chrome window here, Google never observes an automated launch.

1. Fully close any Chrome windows using the profile you're about to open with `--remote-debugging-port` (Chrome refuses to enable remote debugging on a profile that's already running elsewhere).
2. Launch Chrome with **both** `--remote-debugging-port` **and** a dedicated, non-default `--user-data-dir` (recent Chrome versions refuse remote debugging on your regular day-to-day profile for security reasons - this is required, not optional):

   **Windows:**
   ```powershell
   "C:\Program Files\Google\Chrome\Application\chrome.exe" --remote-debugging-port=9222 --user-data-dir="%USERPROFILE%\google-flow-chrome-profile"
   ```

   **macOS:**
   ```bash
   "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" --remote-debugging-port=9222 --user-data-dir="$HOME/google-flow-chrome-profile"
   ```

   **Linux:**
   ```bash
   google-chrome --remote-debugging-port=9222 --user-data-dir="$HOME/google-flow-chrome-profile"
   ```
3. In that window, go to `labs.google/flow` and log in to your Google account **normally, yourself** - this tool never fills in credentials or touches the login form.
4. Leave that window open, then run this tool (CLI or the web app's button). It attaches to port `9222` and reuses that already-logged-in tab/profile. The session persists in `google-flow-chrome-profile` across restarts of Chrome, so you should only need to log in once.

If you'd rather have Playwright launch its own browser instead (not
recommended - see above), pass `--launch-own-browser`; see the Options table
below.

## Run

```bash
npm start -- --project <contentProjectId>
```

Or after `npm run build`, directly:

```bash
node dist/src/cli.js --project <contentProjectId>
```

### Options

| Flag | Default | Meaning |
|---|---|---|
| `--project <id>` | *(required)* | The content project's GUID |
| `--base-url <url>` | `http://localhost:5126` | API base URL. This matches the `dotnet run` dev profile in `src/AiContentFactory.Api/Properties/launchSettings.json`. If you're running the API via the repo's `docker-compose.yml` instead, pass `--base-url http://localhost:8080` (the port it publishes). |
| `--downloads-dir <dir>` | `./downloads` | Where downloaded clips + status files are written |
| `--force` | off | Re-generate/re-download/re-import even if a valid clip already exists locally or on the server |
| `--cdp-endpoint <url>` | `http://localhost:9222` | Chrome DevTools Protocol endpoint of the real Chrome window you launched and logged into yourself (see above). This is the default/recommended mode. |
| `--launch-own-browser` | off | Opt OUT of `--cdp-endpoint` and let Playwright launch its own bundled Chromium in a persistent profile instead. **Not recommended** - commonly blocked at Google sign-in. |
| `--headless` | off | Only applies with `--launch-own-browser`. Run the browser headless (not recommended - the first login needs a visible window) |
| `--help` | | Show usage |

### What it does

1. `GET /content-projects/{id}/flow-plan` - reads the plan.
2. Filters to scenes where `generationType === "AI_VIDEO" && !skipGeneration`, sorted by the plan's own `sceneNumber` (never renumbered).
3. Attaches to the Chrome window you already launched and logged into (via `--cdp-endpoint`, see above) and navigates its active tab to Google Flow - or, only if you passed `--launch-own-browser`, opens its own persistent-profile Chromium instead.
4. If not logged in, it waits (polling, never attempting to fill in credentials or bypass any challenge) for you to log in yourself in that window, up to a timeout.
5. For each queued scene, **strictly one at a time** (never concurrently): submits the prompt, waits for the render, downloads the clip to `downloads/project_{projectId}/clip_{sceneNumber}.mp4`, validates the file, and uploads it via `POST /content-projects/{id}/storyboard/scenes/{sceneId}/video` (the same endpoint Step 6 uses).
6. Before re-uploading, checks `GET /content-projects/{id}/flow-import-status` so an already-imported, still-valid clip is skipped rather than re-uploaded (idempotent), unless `--force` is passed.
7. Writes/updates `downloads/project_{projectId}/automation-status.json` after every scene, recording each scene's state (`PENDING`/`GENERATING`/`WAITING_RENDER`/`DOWNLOADING`/`VALIDATING`/`IMPORTING`/`DONE`/`SKIPPED`/`FAILED`). This file is meant as a foundation for a future UI to poll/display automation progress - no such UI wiring exists yet.
8. If one scene fails, the run logs it, records `FAILED` in the status file, and **continues to the next scene** - it does not abort the whole run. The process still exits with a non-zero code and a summary if anything failed.

### Prompts are used verbatim

`flowVideoPrompt` from the plan is pasted into Flow exactly as returned by
the backend - this tool never rewrites, regenerates, or otherwise touches
prompt text.

## Tests

```bash
npm test
```

This runs `tsc` (via the `pretest` script) then Node's built-in test runner
(`node --test`) against the compiled output in `dist/tests`. No test
framework dependency beyond `typescript`/`@types/node`/`playwright` was
added, per project policy.

Tests cover only the pure logic that can be verified without a real browser
or network:

- `tests/queue.test.ts` - scene filtering/ordering/`sceneNumber` preservation
- `tests/stateMachine.test.ts` - every documented state transition, including all 7 error states and that no path reaches `COMPLETE` without a real success transition
- `tests/download.test.ts` - deterministic per-project file paths, and that missing/zero-byte/wrong-extension files are always classified as invalid (real temp directories via `os.tmpdir()`, no mocks)
- `tests/server.test.ts` - the companion HTTP service's CORS/origin enforcement, the `X-Google-Flow-Runner-UI` header requirement, the `/run` `409` duplicate-in-flight guard, and `/status` reflecting `idle`/`completed` runState + `RunnerResult`, all against a real `http.Server` on an OS-picked ephemeral port with `run()` stubbed via the injectable `runImpl`

The Playwright-driving code (`googleFlowPage.ts`, `browserSession.ts`,
`runner.ts`) is **not** covered by these tests, since exercising it
meaningfully requires a real browser session against the real site.

## Companion HTTP service (for the web app's automation button)

In addition to the CLI, this package can run as a small local HTTP service so
a button in the web app can trigger the same automation instead of the user
typing a CLI command. This is purely an additional entry point (`src/server.ts`)
- the CLI (`npm start`) keeps working exactly as before and does not depend
on the service running.

### Start it

```bash
npm run serve
```

This builds (`tsc`) then runs `node dist/src/server.js`. It listens on
`127.0.0.1:4545` by default. Starting this service is a **prerequisite** for
the web app's automation button - the frontend calls it directly over HTTP
and has nothing to fall back to if it isn't running (the manual copy/paste +
upload workflow, and the CLI, remain unaffected either way).

Environment variables:

| Variable | Default | Meaning |
|---|---|---|
| `GFR_SERVE_PORT` | `4545` | Port to listen on |
| `GFR_UI_ORIGIN` | `http://localhost:5173` | The single browser origin allowed to call `/run` and `/status` (Vite's default dev port for `frontend/ai-content-factory-web`) |

### Endpoints

#### `GET /health`

No CORS/origin restriction (harmless reachability check). Always:

```
200 { "ok": true }
```

#### `POST /run`

Headers required: `Content-Type: application/json`, `Origin: <GFR_UI_ORIGIN>`,
and `X-Google-Flow-Runner-UI: 1` (any value; presence is what's checked).

Body:

```json
{
  "projectId": "string (required)",
  "baseUrl": "string (optional, default http://localhost:5126)",
  "downloadsDir": "string (optional, default ./downloads)",
  "force": "boolean (optional, default false)",
  "cdpEndpoint": "string (optional, default http://localhost:9222 - see the 'launch Chrome yourself' section above)",
  "launchOwnBrowser": "boolean (optional, default false - opt into the legacy, not-recommended Playwright-launches-its-own-Chromium mode)"
}
```

Responses:

- `400` - `projectId` missing/not a string, missing `X-Google-Flow-Runner-UI` header, or invalid JSON body.
- `403` - `Origin` header missing or not an exact match for `GFR_UI_ORIGIN`.
- `409` - `{ "status": "already_running", "projectId": "<id>" }` - a run for this exact `projectId` is already in flight; two different `projectId`s may run concurrently.
- `202` - `{ "status": "started", "projectId": "<id>" }`, returned **immediately**, before the run finishes. The actual automation runs in the background; poll `GET /status` for progress/outcome.

#### `GET /status?project=<projectId>`

Requires the `Origin` header to exactly match `GFR_UI_ORIGIN` (`403` otherwise). `400` if `project` is missing.

```json
{
  "runState": "idle" | "running" | "completed" | "failed",
  "startedAt": "<ISO string or null>",
  "updatedAt": "<ISO string or null>",
  "scenes": {
    "<sceneNumber as string>": { "state": "PENDING|GENERATING|WAITING_RENDER|DOWNLOADING|VALIDATING|IMPORTING|DONE|SKIPPED|FAILED", "message": "<string, optional>", "updatedAt": "<ISO string>" }
  },
  "result": { "completed": 0, "skipped": 0, "failed": 0, "failedScenes": [] } | null,
  "error": "<string, only present when runState is \"failed\" due to a top-level error before any scene was reached>"
}
```

Note the `scenes` map is keyed by **`sceneNumber` as a string**, not `sceneId`
- this differs from the internal shape of `automation-status.json`
(`statusStore.ts` keys by `sceneId`) because the frontend only has
`sceneNumber` readily available (it's what's already in the
`data-scene-index` DOM attributes). If a project has never had a run started
(no in-memory record and no `automation-status.json` on disk yet),
`runState` is `"idle"` with `startedAt`/`updatedAt` both `null` and an empty
`scenes` object.

### Security model (why, not just what)

This is a **localhost dev tool**, not a hardened service - there is no
token/auth system and no HTTPS, on purpose, to avoid over-engineering
something that only ever listens on `127.0.0.1` for a single local user. The
mitigations that do exist are lightweight defenses against a *different*
local page/process silently triggering a (potentially slow) automation run
without the user's knowledge:

1. **Bind to `127.0.0.1` only**, never `0.0.0.0` - never reachable from the network.
2. **Strict single-origin CORS allowlist** (`GFR_UI_ORIGIN`) on `/run` and `/status`. The `Access-Control-Allow-Origin` response header is only ever set to the exact configured origin, and only when the incoming `Origin` header matches it exactly; otherwise the response (and, for `OPTIONS` preflights, the preflight response) omits it entirely, which the browser treats as a hard block for cross-origin callers.
3. **`POST /run` additionally requires a custom header** (`X-Google-Flow-Runner-UI`). A plain cross-origin HTML form POST - the classic CSRF vector - cannot set custom headers, so it can never satisfy this even if it somehow got the origin right. A `fetch` attempting to set that header cross-origin triggers a CORS preflight, which is gated by the same exact-origin check. Together this means: same-origin-or-bust *and* a custom-header requirement, which is a real (if intentionally lightweight) barrier against another local page/process opportunistically POSTing here - not a substitute for real authentication, which this tool's threat model (single local user, no remote exposure) doesn't call for.

`GET /health` has no such restriction, since it reveals nothing sensitive
and exists purely so the UI can check reachability before showing the
automation button as available.

## Safety notes

- Never attempts to bypass Google login, CAPTCHA, or any anti-bot challenge. Attaching to a real Chrome window you launched and logged into yourself (the default mode) is not a bypass technique - it's simply choosing a browser Google doesn't flag as automated, since you did the actual logging in yourself in an ordinary window. If stuck, it surfaces `AUTH_REQUIRED`/`FLOW_UNAVAILABLE`/a clear `CdpConnectionError` (Chrome's debug port unreachable - almost always means step 1-2 above wasn't done) and stops/waits for a human.
- Scenes are processed strictly sequentially, never concurrently, so one failure can't corrupt another scene's download/import.
- A file is only ever considered "downloaded successfully" if it exists, is a `.mp4`, and is non-zero bytes. A missing or empty file is always a failure, never silently treated as success.
- Re-running the tool is safe: it will not re-upload a clip the server already has a valid import for (unless `--force`), and it will not silently overwrite an existing valid local clip (unless `--force`).
