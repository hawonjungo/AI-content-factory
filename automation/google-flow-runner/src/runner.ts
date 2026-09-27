/**
 * Orchestrates the whole run: READ_SCENES -> BUILD_VIDEO_QUEUE -> for each
 * queued scene, SEQUENTIALLY (never concurrently) -> OPEN_FLOW (once)
 * -> GENERATE_SCENE -> WAIT_RENDER -> DOWNLOAD -> VALIDATE_FILE
 * -> IMPORT_STEP_6 -> NEXT_SCENE, ending in COMPLETE.
 *
 * On any per-scene failure, the failure is recorded in statusStore and the
 * run continues to the next scene (one bad scene must not abort the rest) -
 * but the process still exits non-zero with a clear summary if anything
 * failed. AUTH_REQUIRED / FLOW_UNAVAILABLE, by contrast, abort the whole run,
 * since those aren't per-scene problems.
 *
 * This module is the composition root: it is the one place allowed to
 * combine the pure logic (queue.ts, stateMachine.ts, download.ts) with the
 * actual side-effecting pieces (apiClient.ts, browserSession.ts,
 * importer.ts, statusStore.ts). It has NOT been run against the real Google
 * Flow site - see googleFlowPage.ts / flowSelectors.ts for why.
 */
import * as path from "node:path";
import { getFlowPlan } from "./apiClient";
import { buildVideoQueue, type QueuedScene } from "./queue";
import { INITIAL_STATE, transition, type RunState } from "./stateMachine";
import { CdpConnectionError, openFlowSession, waitForHumanLogin, type FlowSession } from "./browserSession";
import { clipFilePath, ensureProjectDownloadsDir, hasExistingValidClip, validateDownloadedFile } from "./download";
import { importClip } from "./importer";
import { loadStatus, saveStatus, updateSceneStatus, type AutomationStatus } from "./statusStore";
import { DEFAULT_AUTH_TIMEOUT_MS, DEFAULT_CDP_ENDPOINT, DEFAULT_RENDER_TIMEOUT_MS } from "./config";

export interface RunnerConfig {
  projectId: string;
  baseUrl: string;
  downloadsDir: string;
  /** Re-generate/re-download/re-import even if a valid clip already exists locally or on the server. */
  force: boolean;
  /** Defaults to false - a visible window is needed for the human login flow. */
  headless?: boolean;
  /**
   * Attach to a real Chrome the user launched/logged into themselves, via
   * --remote-debugging-port. Defaults to DEFAULT_CDP_ENDPOINT
   * ("http://localhost:9222") - this is the recommended mode (see
   * browserSession.ts for why: Google's sign-in blocks a Playwright-launched
   * browser). Set `launchOwnBrowser: true` to opt out and fall back to the
   * old Playwright-launches-its-own-Chromium mode instead.
   */
  cdpEndpoint?: string;
  /** Opt out of CDP-attach and fall back to Playwright launching its own persistent-profile Chromium. Not recommended - see browserSession.ts. */
  launchOwnBrowser?: boolean;
  /** Only used when `launchOwnBrowser` is true. Defaults to `<downloadsDir>/.flow-profile`. */
  userDataDir?: string;
  authTimeoutMs?: number;
  renderTimeoutMs?: number;
}

export interface RunnerResult {
  completed: number;
  skipped: number;
  failed: number;
  failedScenes: number[];
}

/**
 * Thrown when the WHOLE run aborts before any scene could even be attempted
 * (Chrome's debug port unreachable, Flow itself looks broken, or the human
 * never logged in within the timeout). Distinct from a per-scene failure -
 * per-scene failures are recorded in the returned RunnerResult and the run
 * continues to the next scene; this is a run-level problem that stops
 * everything. Thrown (rather than silently returned as a "completed" result
 * with every scene marked failed) so the actual reason reaches whoever is
 * watching for the outcome - the CLI's top-level error handler, or the
 * companion service's /status "error" field for the web UI - instead of only
 * ever being printed to a terminal nobody may be looking at.
 */
export class RunAbortedError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "RunAbortedError";
  }
}

const NOTHING_TO_DO: RunnerResult = { completed: 0, skipped: 0, failed: 0, failedScenes: [] };

export async function run(config: RunnerConfig): Promise<RunnerResult> {
  let state: RunState = INITIAL_STATE;
  state = transition(state, { type: "START" });

  console.log(`Fetching Google Flow generation plan for project ${config.projectId}...`);
  const plan = await getFlowPlan(config.baseUrl, config.projectId);
  state = transition(state, { type: "SCENES_READ" });

  // This is the app's OWN internal AI budget tracker (relevant to its paid
  // Veo path) - not Google Flow's real free-tier quota, which the app has no
  // account connection to and cannot observe. Printed every run so a
  // credits-related question has a concrete number to check against, without
  // implying this tool knows your actual Google Flow balance.
  console.log(
    `App AI budget: used ${plan.usedCredits}/${plan.dailyBudgetCredits}, remaining ${plan.remainingCredits}` +
      (plan.withinBudget ? "" : " - OVER the app's own budget (this does not block Google Flow, which is free and untracked by the app)"),
  );

  const queue = buildVideoQueue(plan);
  state = transition(state, { type: "QUEUE_BUILT", queueLength: queue.length });

  if (queue.length === 0) {
    console.log(
      "No AI_VIDEO scenes require Google Flow generation right now (all scenes are images/static, or already marked skipGeneration). Nothing to do.",
    );
    return NOTHING_TO_DO;
  }

  ensureProjectDownloadsDir(config.downloadsDir, config.projectId);
  const status = loadStatus(config.downloadsDir, config.projectId);
  console.log(`Queued ${queue.length} scene(s) for Google Flow: scene ${queue.map((s) => s.sceneNumber).join(", scene ")}`);

  const result: RunnerResult = { completed: 0, skipped: 0, failed: 0, failedScenes: [] };
  let session: FlowSession | null = null;

  try {
    try {
      session = await openFlowSession(
        config.launchOwnBrowser
          ? { userDataDir: config.userDataDir ?? path.join(config.downloadsDir, ".flow-profile"), headless: config.headless }
          : { cdpEndpoint: config.cdpEndpoint ?? DEFAULT_CDP_ENDPOINT },
      );
    } catch (err) {
      if (err instanceof CdpConnectionError) {
        console.error(`FLOW_UNAVAILABLE: ${err.message}`);
        throw new RunAbortedError(err.message);
      }
      throw err;
    }

    if (await session.flowPage.isFlowUnavailable()) {
      transition(state, { type: "FLOW_LOAD_FAILED" }); // validates the transition is legal; not stored, run is aborting anyway
      const message = "labs.google/flow does not look reachable/usable right now. Stopping without generating anything.";
      console.error(`FLOW_UNAVAILABLE: ${message}`);
      throw new RunAbortedError(message);
    }

    if (!(await session.flowPage.isAuthenticated())) {
      state = transition(state, { type: "AUTH_MISSING" });
      console.warn("AUTH_REQUIRED: please log in to Google Flow in the browser window that just opened.");
      const loggedIn = await waitForHumanLogin(session.flowPage, {
        timeoutMs: config.authTimeoutMs ?? DEFAULT_AUTH_TIMEOUT_MS,
        pollIntervalMs: 3000,
        onWaiting: () => console.warn("Still waiting for you to log in to Google Flow..."),
      });
      if (!loggedIn) {
        const message = "Timed out waiting for you to log in to Google Flow. Stopping without generating anything.";
        console.error(message);
        throw new RunAbortedError(message);
      }
      state = transition(state, { type: "LOGIN_CONFIRMED" });
      console.log("Login detected, continuing.");
    }

    state = transition(state, { type: "FLOW_OPENED", sceneNumber: queue[0]!.sceneNumber });

    for (let i = 0; i < queue.length; i++) {
      const scene = queue[i]!;
      console.log(`\n--- Scene ${scene.sceneNumber} (${i + 1}/${queue.length}) ---`);

      const outcome = await runScene(session, config, status, scene, state);
      state = outcome.state;

      if (outcome.failed) {
        result.failed += 1;
        result.failedScenes.push(scene.sceneNumber);
      } else if (outcome.skipped) {
        result.skipped += 1;
      } else {
        result.completed += 1;
      }
      saveStatus(config.downloadsDir, status);

      const next = queue[i + 1];
      state = next
        ? transition(state, { type: "NEXT_SCENE_AVAILABLE", sceneNumber: next.sceneNumber })
        : transition(state, { type: "QUEUE_EXHAUSTED" });
    }
  } finally {
    if (session) {
      await session.close();
    }
  }

  return result;
}

interface SceneOutcome {
  state: RunState;
  failed: boolean;
  skipped: boolean;
}

/**
 * Drives one scene through GENERATE_SCENE -> WAIT_RENDER -> DOWNLOAD
 * -> VALIDATE_FILE -> IMPORT_STEP_6 -> NEXT_SCENE (success or failure - both
 * paths always end at NEXT_SCENE so the caller's loop can keep driving the
 * state machine uniformly).
 *
 * If a valid clip for this scene already exists locally (and `--force` was
 * not passed), the actual browser generate/wait/download steps are skipped -
 * we still drive the state machine through the same events, since we already
 * know (from a previous run) that those stages succeeded; we do NOT skip the
 * import idempotency check, since the local file may exist without having
 * been imported yet (e.g. a previous run crashed between download and
 * import).
 */
async function runScene(
  session: FlowSession,
  config: RunnerConfig,
  status: AutomationStatus,
  scene: QueuedScene,
  incomingState: RunState,
): Promise<SceneOutcome> {
  const targetPath = clipFilePath(config.downloadsDir, config.projectId, scene.sceneNumber);
  let state = incomingState;

  const reuseExisting = !config.force && hasExistingValidClip(targetPath);

  if (reuseExisting) {
    console.log(`Scene ${scene.sceneNumber}: reusing existing valid local clip at ${targetPath} (use --force to regenerate).`);
  } else {
    updateSceneStatus(status, scene.sceneId, scene.sceneNumber, "GENERATING");
    try {
      await session.flowPage.submitPrompt(scene.flowVideoPrompt, { model: scene.recommendedModel });
    } catch (err) {
      state = transition(state, { type: "GENERATION_ERROR" });
      updateSceneStatus(status, scene.sceneId, scene.sceneNumber, "FAILED", `Prompt submission failed: ${describeError(err)}`);
      state = transition(state, { type: "SCENE_FAILED_CONTINUE" });
      return { state, failed: true, skipped: false };
    }
  }
  state = transition(state, { type: "PROMPT_SUBMITTED" });

  if (!reuseExisting) {
    updateSceneStatus(status, scene.sceneId, scene.sceneNumber, "WAITING_RENDER");
    const renderResult = await session.flowPage.waitForRender(config.renderTimeoutMs ?? DEFAULT_RENDER_TIMEOUT_MS);

    if (renderResult === "timeout") {
      state = transition(state, { type: "RENDER_TIMED_OUT" });
      updateSceneStatus(status, scene.sceneId, scene.sceneNumber, "FAILED", "Render timed out waiting for Google Flow.");
      state = transition(state, { type: "SCENE_FAILED_CONTINUE" });
      return { state, failed: true, skipped: false };
    }
    if (renderResult === "failed") {
      state = transition(state, { type: "RENDER_FAILED_IN_FLOW" });
      updateSceneStatus(status, scene.sceneId, scene.sceneNumber, "FAILED", "Google Flow reported the generation failed.");
      state = transition(state, { type: "SCENE_FAILED_CONTINUE" });
      return { state, failed: true, skipped: false };
    }
  }
  state = transition(state, { type: "RENDER_COMPLETE" });

  if (!reuseExisting) {
    updateSceneStatus(status, scene.sceneId, scene.sceneNumber, "DOWNLOADING");
    try {
      await session.flowPage.download(targetPath);
    } catch (err) {
      state = transition(state, { type: "DOWNLOAD_ERROR" });
      updateSceneStatus(status, scene.sceneId, scene.sceneNumber, "FAILED", `Download failed: ${describeError(err)}`);
      state = transition(state, { type: "SCENE_FAILED_CONTINUE" });
      return { state, failed: true, skipped: false };
    }
  }
  state = transition(state, { type: "DOWNLOAD_COMPLETE" });

  updateSceneStatus(status, scene.sceneId, scene.sceneNumber, "VALIDATING");
  const validation = validateDownloadedFile(targetPath);
  if (!validation.ok) {
    state = transition(state, { type: "FILE_INVALID" });
    updateSceneStatus(status, scene.sceneId, scene.sceneNumber, "FAILED", validation.reason);
    state = transition(state, { type: "SCENE_FAILED_CONTINUE" });
    return { state, failed: true, skipped: false };
  }
  state = transition(state, { type: "FILE_VALID" });

  updateSceneStatus(status, scene.sceneId, scene.sceneNumber, "IMPORTING");
  try {
    const outcome = await importClip(config.baseUrl, config.projectId, scene.sceneId, targetPath, { force: config.force });

    if (outcome.skipped) {
      console.log(`Scene ${scene.sceneNumber}: ${outcome.reason}`);
      updateSceneStatus(status, scene.sceneId, scene.sceneNumber, "SKIPPED", outcome.reason);
      state = transition(state, { type: "IMPORT_SUCCESS" });
      return { state, failed: false, skipped: true };
    }

    const result = outcome.result!;
    if (!result.accepted) {
      state = transition(state, { type: "IMPORT_ERROR" });
      updateSceneStatus(status, scene.sceneId, scene.sceneNumber, "FAILED", `Rejected by server: ${result.issues.join("; ")}`);
      state = transition(state, { type: "SCENE_FAILED_CONTINUE" });
      return { state, failed: true, skipped: false };
    }

    if (result.warnings.length > 0) {
      console.warn(`Scene ${scene.sceneNumber}: imported with warnings: ${result.warnings.join("; ")}`);
    }
    updateSceneStatus(status, scene.sceneId, scene.sceneNumber, "DONE");
    state = transition(state, { type: "IMPORT_SUCCESS" });
    return { state, failed: false, skipped: false };
  } catch (err) {
    state = transition(state, { type: "IMPORT_ERROR" });
    updateSceneStatus(status, scene.sceneId, scene.sceneNumber, "FAILED", `Import request failed: ${describeError(err)}`);
    state = transition(state, { type: "SCENE_FAILED_CONTINUE" });
    return { state, failed: true, skipped: false };
  }
}

function describeError(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}
