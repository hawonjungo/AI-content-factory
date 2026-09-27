import { test } from "node:test";
import assert from "node:assert/strict";
import type { AddressInfo } from "node:net";
import { createServer, type ServerDeps } from "../src/server";
import type { RunnerConfig, RunnerResult } from "../src/runner";

const ALLOWED_ORIGIN = "http://localhost:5173";
const DISALLOWED_ORIGIN = "http://evil.example";
const UI_HEADER = { "X-Google-Flow-Runner-UI": "1" };

/** Starts a fresh server (own in-memory state) on an OS-picked ephemeral port. */
async function startTestServer(deps: ServerDeps = {}): Promise<{ baseUrl: string; close: () => Promise<void> }> {
  const server = createServer({ uiOrigin: ALLOWED_ORIGIN, ...deps });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  const { port } = server.address() as AddressInfo;
  return {
    baseUrl: `http://127.0.0.1:${port}`,
    close: () => new Promise<void>((resolve, reject) => server.close((err) => (err ? reject(err) : resolve()))),
  };
}

function neverResolvingRun(): (config: RunnerConfig) => Promise<RunnerResult> {
  return () => new Promise<RunnerResult>(() => {}); // deliberately never settles
}

function resolvingRun(result: RunnerResult): (config: RunnerConfig) => Promise<RunnerResult> {
  return () => Promise.resolve(result);
}

test("POST /run rejects a disallowed Origin with 403", async () => {
  const { baseUrl, close } = await startTestServer({ runImpl: neverResolvingRun() });
  try {
    const res = await fetch(`${baseUrl}/run`, {
      method: "POST",
      headers: { "Content-Type": "application/json", Origin: DISALLOWED_ORIGIN, ...UI_HEADER },
      body: JSON.stringify({ projectId: "p1" }),
    });
    assert.equal(res.status, 403);
    assert.equal(res.headers.get("access-control-allow-origin"), null);
  } finally {
    await close();
  }
});

test("OPTIONS preflight for /run from a disallowed Origin is rejected without an Allow-Origin header", async () => {
  const { baseUrl, close } = await startTestServer({ runImpl: neverResolvingRun() });
  try {
    const res = await fetch(`${baseUrl}/run`, {
      method: "OPTIONS",
      headers: {
        Origin: DISALLOWED_ORIGIN,
        "Access-Control-Request-Method": "POST",
        "Access-Control-Request-Headers": "content-type,x-google-flow-runner-ui",
      },
    });
    assert.equal(res.status, 403);
    assert.equal(res.headers.get("access-control-allow-origin"), null);
  } finally {
    await close();
  }
});

test("POST /run without the X-Google-Flow-Runner-UI header is rejected with 400", async () => {
  const { baseUrl, close } = await startTestServer({ runImpl: neverResolvingRun() });
  try {
    const res = await fetch(`${baseUrl}/run`, {
      method: "POST",
      headers: { "Content-Type": "application/json", Origin: ALLOWED_ORIGIN },
      body: JSON.stringify({ projectId: "p1" }),
    });
    assert.equal(res.status, 400);
  } finally {
    await close();
  }
});

test("GET /status for a project with no prior run returns idle with empty scenes", async () => {
  const { baseUrl, close } = await startTestServer({ runImpl: neverResolvingRun() });
  try {
    const res = await fetch(`${baseUrl}/status?project=never-started`, {
      headers: { Origin: ALLOWED_ORIGIN },
    });
    assert.equal(res.status, 200);
    const body = await res.json();
    assert.deepEqual(body, {
      runState: "idle",
      startedAt: null,
      updatedAt: null,
      scenes: {},
      result: null,
    });
  } finally {
    await close();
  }
});

test("calling POST /run twice for the same projectId while the first is still in flight returns 409 the second time", async () => {
  const { baseUrl, close } = await startTestServer({ runImpl: neverResolvingRun() });
  try {
    const first = await fetch(`${baseUrl}/run`, {
      method: "POST",
      headers: { "Content-Type": "application/json", Origin: ALLOWED_ORIGIN, ...UI_HEADER },
      body: JSON.stringify({ projectId: "same-project" }),
    });
    assert.equal(first.status, 202);
    const firstBody = await first.json();
    assert.deepEqual(firstBody, { status: "started", projectId: "same-project" });

    const second = await fetch(`${baseUrl}/run`, {
      method: "POST",
      headers: { "Content-Type": "application/json", Origin: ALLOWED_ORIGIN, ...UI_HEADER },
      body: JSON.stringify({ projectId: "same-project" }),
    });
    assert.equal(second.status, 409);
    const secondBody = await second.json();
    assert.deepEqual(secondBody, { status: "already_running", projectId: "same-project" });
  } finally {
    await close();
  }
});

test("GET /status after a stubbed run resolves reflects runState completed and the RunnerResult", async () => {
  const fakeResult: RunnerResult = { completed: 2, skipped: 1, failed: 0, failedScenes: [] };
  const { baseUrl, close } = await startTestServer({ runImpl: resolvingRun(fakeResult) });
  try {
    const startRes = await fetch(`${baseUrl}/run`, {
      method: "POST",
      headers: { "Content-Type": "application/json", Origin: ALLOWED_ORIGIN, ...UI_HEADER },
      body: JSON.stringify({ projectId: "will-complete" }),
    });
    assert.equal(startRes.status, 202);

    // The stub run() resolves on the next microtask; poll briefly instead of
    // assuming a single tick is always enough under test-runner scheduling.
    let body: any;
    for (let i = 0; i < 50; i++) {
      const statusRes = await fetch(`${baseUrl}/status?project=will-complete`, {
        headers: { Origin: ALLOWED_ORIGIN },
      });
      body = await statusRes.json();
      if (body.runState === "completed") break;
      await new Promise((resolve) => setTimeout(resolve, 10));
    }

    assert.equal(body.runState, "completed");
    assert.deepEqual(body.result, fakeResult);
  } finally {
    await close();
  }
});
