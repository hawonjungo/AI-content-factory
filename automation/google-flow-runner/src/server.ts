#!/usr/bin/env node
/**
 * Optional local HTTP "companion service". Lets a button in the web app
 * trigger the same automation this package's CLI (cli.ts/runner.ts) runs,
 * without the user typing a command themselves. This is purely an additional
 * entry point: it reuses `run()` from runner.ts unchanged and does not alter
 * anything about how the CLI behaves.
 *
 * Uses only Node's built-in `node:http` - no Express/web-framework
 * dependency, consistent with this package's existing avoidance of
 * unnecessary dependencies (no CLI-arg-parser, no extra test framework).
 *
 * ---------------------------------------------------------------------------
 * SECURITY MODEL (read before changing anything here)
 * ---------------------------------------------------------------------------
 * This is a localhost dev tool, not a hardened service. There is no
 * token/auth system and no HTTPS - deliberately, to avoid over-engineering a
 * tool that only ever listens on 127.0.0.1 for a single local user. The
 * mitigations below are lightweight defenses against a *different* local
 * page/process silently triggering a (potentially slow, resource-heavy)
 * automation run without the user's knowledge - not a general auth system:
 *
 *   1. Bind to 127.0.0.1 only, never 0.0.0.0 - unreachable from the network.
 *   2. Strict single-origin CORS allowlist (GFR_UI_ORIGIN, default the app's
 *      known Vite dev origin). Any cross-origin `fetch` from another page
 *      that doesn't exactly match this origin is blocked by the browser
 *      itself once we omit/refuse the Access-Control-Allow-Origin header.
 *   3. POST /run additionally requires a custom header
 *      (X-Google-Flow-Runner-UI). A plain cross-origin HTML form POST (the
 *      classic CSRF vector) cannot set custom headers, so it can't satisfy
 *      this even if it guessed the origin check somehow. A `fetch` that
 *      tries to set the custom header cross-origin triggers a CORS
 *      preflight (OPTIONS), which is again gated by the exact-origin check
 *      above. Combined, this means: same-origin-or-bust AND
 *      custom-header-required, which is a real (if lightweight) barrier
 *      against another local page/process opportunistically POSTing here -
 *      not a substitute for real auth, which this tool deliberately doesn't
 *      need given its threat model (single local user, no remote exposure).
 */
import * as http from "node:http";
import { run, type RunnerConfig, type RunnerResult } from "./runner";
import { loadStatus, statusFilePath, type SceneStatusEntry } from "./statusStore";
import { DEFAULT_BASE_URL, DEFAULT_DOWNLOADS_DIR, DEFAULT_SERVE_PORT, DEFAULT_UI_ORIGIN } from "./config";
import * as fs from "node:fs";

export type ProjectRunState = "idle" | "running" | "completed" | "failed";

type RunFn = typeof run;

export interface ServerDeps {
  /** Defaults to the real `run` from runner.ts. Injectable for tests. */
  runImpl?: RunFn;
  /** Defaults to GFR_UI_ORIGIN env var, then DEFAULT_UI_ORIGIN. */
  uiOrigin?: string;
}

const REQUIRED_RUN_HEADER = "x-google-flow-runner-ui";

interface RunBody {
  projectId?: unknown;
  baseUrl?: unknown;
  downloadsDir?: unknown;
  force?: unknown;
  cdpEndpoint?: unknown;
  launchOwnBrowser?: unknown;
}

/**
 * Builds the request listener plus its in-memory state, isolated per call so
 * tests can create independent instances instead of sharing module-level
 * state with each other (and with a real running instance).
 */
export function createRequestListener(deps: ServerDeps = {}): http.RequestListener {
  const runImpl: RunFn = deps.runImpl ?? run;
  const uiOrigin = deps.uiOrigin ?? process.env.GFR_UI_ORIGIN ?? DEFAULT_UI_ORIGIN;

  // Module-scoped-per-listener in-memory tracking. Reset on process restart;
  // statusStore's on-disk automation-status.json remains the durable,
  // per-scene record across restarts.
  const runStates = new Map<string, ProjectRunState>();
  const runResults = new Map<string, RunnerResult>();
  const runErrors = new Map<string, string>();
  const runDownloadsDirs = new Map<string, string>();

  function originAllowed(origin: string | undefined): boolean {
    return origin !== undefined && origin === uiOrigin;
  }

  function sendJson(res: http.ServerResponse, status: number, body: unknown, extraHeaders?: Record<string, string>): void {
    const payload = JSON.stringify(body);
    res.writeHead(status, {
      "Content-Type": "application/json",
      ...extraHeaders,
    });
    res.end(payload);
  }

  function corsHeadersFor(origin: string): Record<string, string> {
    return {
      "Access-Control-Allow-Origin": origin,
      Vary: "Origin",
    };
  }

  function handlePreflight(req: http.IncomingMessage, res: http.ServerResponse): void {
    const origin = req.headers.origin;
    if (!originAllowed(origin)) {
      // Deliberately omit Access-Control-Allow-Origin so the browser blocks
      // the real request that would follow this preflight.
      res.writeHead(403);
      res.end();
      return;
    }
    res.writeHead(204, {
      ...corsHeadersFor(origin as string),
      "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
      "Access-Control-Allow-Headers": "Content-Type, X-Google-Flow-Runner-UI",
      "Access-Control-Max-Age": "600",
    });
    res.end();
  }

  function readJsonBody(req: http.IncomingMessage): Promise<unknown> {
    return new Promise((resolve, reject) => {
      const chunks: Buffer[] = [];
      req.on("data", (chunk: Buffer) => chunks.push(chunk));
      req.on("end", () => {
        const raw = Buffer.concat(chunks).toString("utf-8");
        if (!raw) {
          resolve({});
          return;
        }
        try {
          resolve(JSON.parse(raw));
        } catch (err) {
          reject(err);
        }
      });
      req.on("error", reject);
    });
  }

  async function handleRun(req: http.IncomingMessage, res: http.ServerResponse, origin: string): Promise<void> {
    const headerValue = req.headers[REQUIRED_RUN_HEADER];
    if (headerValue === undefined) {
      sendJson(res, 400, { error: "Missing required X-Google-Flow-Runner-UI header." }, corsHeadersFor(origin));
      return;
    }

    let body: RunBody;
    try {
      body = (await readJsonBody(req)) as RunBody;
    } catch {
      sendJson(res, 400, { error: "Invalid JSON body." }, corsHeadersFor(origin));
      return;
    }

    if (typeof body.projectId !== "string" || body.projectId.length === 0) {
      sendJson(res, 400, { error: "projectId is required and must be a string." }, corsHeadersFor(origin));
      return;
    }
    const projectId = body.projectId;

    if (runStates.get(projectId) === "running") {
      sendJson(res, 409, { status: "already_running", projectId }, corsHeadersFor(origin));
      return;
    }

    const config: RunnerConfig = {
      projectId,
      baseUrl: typeof body.baseUrl === "string" ? body.baseUrl : DEFAULT_BASE_URL,
      downloadsDir: typeof body.downloadsDir === "string" ? body.downloadsDir : DEFAULT_DOWNLOADS_DIR,
      force: Boolean(body.force),
      // Defaults to CDP-attach (see RunnerConfig/browserSession.ts) - the UI
      // doesn't need to send these unless a user wants to opt into the
      // legacy launch-own-browser mode.
      cdpEndpoint: typeof body.cdpEndpoint === "string" ? body.cdpEndpoint : undefined,
      launchOwnBrowser: Boolean(body.launchOwnBrowser),
    };

    runStates.set(projectId, "running");
    runResults.delete(projectId);
    runErrors.delete(projectId);
    runDownloadsDirs.set(projectId, config.downloadsDir);

    sendJson(res, 202, { status: "started", projectId }, corsHeadersFor(origin));

    // Fire-and-forget: the HTTP response above has already been sent. The
    // caller is expected to poll GET /status for progress/outcome.
    runImpl(config)
      .then((result) => {
        runStates.set(projectId, "completed");
        runResults.set(projectId, result);
      })
      .catch((err: unknown) => {
        runStates.set(projectId, "failed");
        runErrors.set(projectId, err instanceof Error ? err.message : String(err));
      });
  }

  function handleStatus(req: http.IncomingMessage, res: http.ServerResponse, origin: string): void {
    const url = new URL(req.url ?? "/", "http://127.0.0.1");
    const projectId = url.searchParams.get("project");
    if (!projectId) {
      sendJson(res, 400, { error: "project query parameter is required." }, corsHeadersFor(origin));
      return;
    }

    const runState = runStates.get(projectId) ?? "idle";
    const downloadsDir = runDownloadsDirs.get(projectId) ?? DEFAULT_DOWNLOADS_DIR;

    let startedAt: string | null = null;
    let updatedAt: string | null = null;
    const scenesByNumber: Record<string, Omit<SceneStatusEntry, "sceneId" | "sceneNumber">> = {};

    if (fs.existsSync(statusFilePath(downloadsDir, projectId))) {
      const status = loadStatus(downloadsDir, projectId);
      startedAt = status.startedAt;
      updatedAt = status.updatedAt;
      for (const entry of Object.values(status.scenes)) {
        scenesByNumber[String(entry.sceneNumber)] = {
          state: entry.state,
          message: entry.message,
          updatedAt: entry.updatedAt,
        };
      }
    }

    const result = runState === "completed" ? runResults.get(projectId) ?? null : null;

    const responseBody: Record<string, unknown> = {
      runState,
      startedAt,
      updatedAt,
      scenes: scenesByNumber,
      result,
    };
    if (runState === "failed" && runErrors.has(projectId)) {
      responseBody.error = runErrors.get(projectId);
    }

    sendJson(res, 200, responseBody, corsHeadersFor(origin));
  }

  return function requestListener(req, res) {
    const url = new URL(req.url ?? "/", "http://127.0.0.1");
    const pathname = url.pathname;

    if (pathname === "/health" && req.method === "GET") {
      // No CORS/origin restriction - harmless reachability check the UI
      // polls before showing the automation button as available.
      sendJson(res, 200, { ok: true }, { "Access-Control-Allow-Origin": "*" });
      return;
    }

    if (pathname === "/run") {
      if (req.method === "OPTIONS") {
        handlePreflight(req, res);
        return;
      }
      if (req.method !== "POST") {
        res.writeHead(405);
        res.end();
        return;
      }
      const origin = req.headers.origin;
      if (!originAllowed(origin)) {
        sendJson(res, 403, { error: "Forbidden origin." });
        return;
      }
      handleRun(req, res, origin as string).catch((err) => {
        sendJson(res, 500, { error: err instanceof Error ? err.message : String(err) }, corsHeadersFor(origin as string));
      });
      return;
    }

    if (pathname === "/status") {
      if (req.method === "OPTIONS") {
        handlePreflight(req, res);
        return;
      }
      if (req.method !== "GET") {
        res.writeHead(405);
        res.end();
        return;
      }
      const origin = req.headers.origin;
      if (!originAllowed(origin)) {
        sendJson(res, 403, { error: "Forbidden origin." });
        return;
      }
      handleStatus(req, res, origin as string);
      return;
    }

    res.writeHead(404);
    res.end();
  };
}

export function createServer(deps?: ServerDeps): http.Server {
  return http.createServer(createRequestListener(deps));
}

function main(): void {
  const port = process.env.GFR_SERVE_PORT ? Number(process.env.GFR_SERVE_PORT) : DEFAULT_SERVE_PORT;
  const server = createServer();
  server.listen(port, "127.0.0.1", () => {
    console.log(`Google Flow Runner companion service listening on http://127.0.0.1:${port}`);
    console.log(`Allowed UI origin: ${process.env.GFR_UI_ORIGIN ?? DEFAULT_UI_ORIGIN}`);
  });
}

if (require.main === module) {
  main();
}
