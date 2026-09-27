/**
 * Pure, side-effect-free run state machine for this tool. No Playwright, no
 * filesystem, no network - just a typed union of states and a total
 * transition function, so the sequencing logic can be exhaustively unit
 * tested (see tests/stateMachine.test.ts) independent of anything that can
 * only be verified against the real Google Flow site.
 *
 * `transition` throws on any event that isn't valid from the current state -
 * this is intentional: an invalid transition is a bug, and we would rather
 * crash loudly than silently drift into a state (in particular COMPLETE)
 * that wasn't actually earned.
 */

export type RunStatus =
  // Whole-run states
  | "IDLE"
  | "READ_SCENES"
  | "BUILD_VIDEO_QUEUE"
  | "OPEN_FLOW"
  | "COMPLETE"
  // Per-scene states (successful path)
  | "GENERATE_SCENE"
  | "WAIT_RENDER"
  | "DOWNLOAD"
  | "VALIDATE_FILE"
  | "IMPORT_STEP_6"
  | "NEXT_SCENE"
  // Error states
  | "AUTH_REQUIRED"
  | "FLOW_UNAVAILABLE"
  | "GENERATION_FAILED"
  | "RENDER_TIMEOUT"
  | "DOWNLOAD_FAILED"
  | "INVALID_FILE"
  | "IMPORT_FAILED";

export interface RunState {
  status: RunStatus;
  /** The scene this status applies to, when the status is scene-scoped. */
  sceneNumber: number | null;
}

export type RunEvent =
  | { type: "START" }
  | { type: "SCENES_READ" }
  | { type: "QUEUE_BUILT"; queueLength: number }
  | { type: "FLOW_OPENED"; sceneNumber: number }
  | { type: "AUTH_MISSING" }
  | { type: "FLOW_LOAD_FAILED" }
  | { type: "LOGIN_CONFIRMED" }
  | { type: "PROMPT_SUBMITTED" }
  | { type: "GENERATION_ERROR" }
  | { type: "RENDER_COMPLETE" }
  | { type: "RENDER_TIMED_OUT" }
  /** Google Flow itself reported a generation failure while we were waiting on a render (quota, content policy, provider-side error, etc). */
  | { type: "RENDER_FAILED_IN_FLOW" }
  | { type: "DOWNLOAD_COMPLETE" }
  | { type: "DOWNLOAD_ERROR" }
  | { type: "FILE_VALID" }
  | { type: "FILE_INVALID" }
  | { type: "IMPORT_SUCCESS" }
  | { type: "IMPORT_ERROR" }
  /** Record a per-scene failure and move on to the next scene rather than aborting the whole run. */
  | { type: "SCENE_FAILED_CONTINUE" }
  | { type: "NEXT_SCENE_AVAILABLE"; sceneNumber: number }
  | { type: "QUEUE_EXHAUSTED" };

export const INITIAL_STATE: RunState = { status: "IDLE", sceneNumber: null };

const ERROR_STATUSES: readonly RunStatus[] = [
  "AUTH_REQUIRED",
  "FLOW_UNAVAILABLE",
  "GENERATION_FAILED",
  "RENDER_TIMEOUT",
  "DOWNLOAD_FAILED",
  "INVALID_FILE",
  "IMPORT_FAILED",
];

export function isErrorStatus(status: RunStatus): boolean {
  return ERROR_STATUSES.includes(status);
}

/** States with no outgoing transition at all - the run truly stops here. */
export function isTerminal(status: RunStatus): boolean {
  return status === "COMPLETE" || status === "FLOW_UNAVAILABLE";
}

function invalidTransition(state: RunState, event: RunEvent): never {
  throw new Error(
    `Invalid transition: event "${event.type}" is not valid from state "${state.status}" (scene ${String(state.sceneNumber)})`,
  );
}

export function transition(state: RunState, event: RunEvent): RunState {
  switch (state.status) {
    case "IDLE":
      if (event.type === "START") {
        return { status: "READ_SCENES", sceneNumber: null };
      }
      break;

    case "READ_SCENES":
      if (event.type === "SCENES_READ") {
        return { status: "BUILD_VIDEO_QUEUE", sceneNumber: null };
      }
      break;

    case "BUILD_VIDEO_QUEUE":
      if (event.type === "QUEUE_BUILT") {
        return event.queueLength > 0
          ? { status: "OPEN_FLOW", sceneNumber: null }
          : { status: "COMPLETE", sceneNumber: null };
      }
      break;

    case "OPEN_FLOW":
      if (event.type === "FLOW_OPENED") {
        return { status: "GENERATE_SCENE", sceneNumber: event.sceneNumber };
      }
      if (event.type === "AUTH_MISSING") {
        return { status: "AUTH_REQUIRED", sceneNumber: state.sceneNumber };
      }
      if (event.type === "FLOW_LOAD_FAILED") {
        return { status: "FLOW_UNAVAILABLE", sceneNumber: state.sceneNumber };
      }
      break;

    // Recoverable: a human logging in during the AUTH_REQUIRED wait brings us
    // back to OPEN_FLOW to actually proceed - this models browserSession's
    // "poll and wait, then continue" behavior rather than a hard abort.
    case "AUTH_REQUIRED":
      if (event.type === "LOGIN_CONFIRMED") {
        return { status: "OPEN_FLOW", sceneNumber: state.sceneNumber };
      }
      break;

    case "GENERATE_SCENE":
      if (event.type === "PROMPT_SUBMITTED") {
        return { status: "WAIT_RENDER", sceneNumber: state.sceneNumber };
      }
      if (event.type === "GENERATION_ERROR") {
        return { status: "GENERATION_FAILED", sceneNumber: state.sceneNumber };
      }
      break;

    case "WAIT_RENDER":
      if (event.type === "RENDER_COMPLETE") {
        return { status: "DOWNLOAD", sceneNumber: state.sceneNumber };
      }
      if (event.type === "RENDER_TIMED_OUT") {
        return { status: "RENDER_TIMEOUT", sceneNumber: state.sceneNumber };
      }
      if (event.type === "RENDER_FAILED_IN_FLOW") {
        return { status: "GENERATION_FAILED", sceneNumber: state.sceneNumber };
      }
      break;

    case "DOWNLOAD":
      if (event.type === "DOWNLOAD_COMPLETE") {
        return { status: "VALIDATE_FILE", sceneNumber: state.sceneNumber };
      }
      if (event.type === "DOWNLOAD_ERROR") {
        return { status: "DOWNLOAD_FAILED", sceneNumber: state.sceneNumber };
      }
      break;

    case "VALIDATE_FILE":
      if (event.type === "FILE_VALID") {
        return { status: "IMPORT_STEP_6", sceneNumber: state.sceneNumber };
      }
      if (event.type === "FILE_INVALID") {
        return { status: "INVALID_FILE", sceneNumber: state.sceneNumber };
      }
      break;

    case "IMPORT_STEP_6":
      if (event.type === "IMPORT_SUCCESS") {
        return { status: "NEXT_SCENE", sceneNumber: state.sceneNumber };
      }
      if (event.type === "IMPORT_ERROR") {
        return { status: "IMPORT_FAILED", sceneNumber: state.sceneNumber };
      }
      break;

    // Every per-scene failure state funnels into NEXT_SCENE the same way:
    // the run records the failure (statusStore, caller's responsibility) and
    // continues - it never jumps straight to COMPLETE or to a fabricated
    // success from an error state.
    case "GENERATION_FAILED":
    case "RENDER_TIMEOUT":
    case "DOWNLOAD_FAILED":
    case "INVALID_FILE":
    case "IMPORT_FAILED":
      if (event.type === "SCENE_FAILED_CONTINUE") {
        return { status: "NEXT_SCENE", sceneNumber: state.sceneNumber };
      }
      break;

    case "NEXT_SCENE":
      if (event.type === "NEXT_SCENE_AVAILABLE") {
        return { status: "GENERATE_SCENE", sceneNumber: event.sceneNumber };
      }
      if (event.type === "QUEUE_EXHAUSTED") {
        return { status: "COMPLETE", sceneNumber: null };
      }
      break;

    // COMPLETE and FLOW_UNAVAILABLE are terminal - no outgoing transitions.
    case "COMPLETE":
    case "FLOW_UNAVAILABLE":
      break;

    default: {
      // Exhaustiveness guard: if a new RunStatus is added without a case
      // above, this will fail to compile.
      const _exhaustive: never = state.status;
      void _exhaustive;
    }
  }

  return invalidTransition(state, event);
}
