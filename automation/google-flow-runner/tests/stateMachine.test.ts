import { test } from "node:test";
import assert from "node:assert/strict";
import { INITIAL_STATE, isErrorStatus, transition, type RunState } from "../src/stateMachine";

test("happy path: IDLE all the way to COMPLETE for a single successful scene", () => {
  let state: RunState = INITIAL_STATE;

  state = transition(state, { type: "START" });
  assert.equal(state.status, "READ_SCENES");

  state = transition(state, { type: "SCENES_READ" });
  assert.equal(state.status, "BUILD_VIDEO_QUEUE");

  state = transition(state, { type: "QUEUE_BUILT", queueLength: 1 });
  assert.equal(state.status, "OPEN_FLOW");

  state = transition(state, { type: "FLOW_OPENED", sceneNumber: 1 });
  assert.equal(state.status, "GENERATE_SCENE");
  assert.equal(state.sceneNumber, 1);

  state = transition(state, { type: "PROMPT_SUBMITTED" });
  assert.equal(state.status, "WAIT_RENDER");

  state = transition(state, { type: "RENDER_COMPLETE" });
  assert.equal(state.status, "DOWNLOAD");

  state = transition(state, { type: "DOWNLOAD_COMPLETE" });
  assert.equal(state.status, "VALIDATE_FILE");

  state = transition(state, { type: "FILE_VALID" });
  assert.equal(state.status, "IMPORT_STEP_6");

  state = transition(state, { type: "IMPORT_SUCCESS" });
  assert.equal(state.status, "NEXT_SCENE");

  state = transition(state, { type: "QUEUE_EXHAUSTED" });
  assert.equal(state.status, "COMPLETE");
});

test("happy path across multiple scenes loops NEXT_SCENE -> GENERATE_SCENE before reaching COMPLETE", () => {
  let state: RunState = INITIAL_STATE;
  state = transition(state, { type: "START" });
  state = transition(state, { type: "SCENES_READ" });
  state = transition(state, { type: "QUEUE_BUILT", queueLength: 2 });
  state = transition(state, { type: "FLOW_OPENED", sceneNumber: 1 });

  // scene 1
  state = transition(state, { type: "PROMPT_SUBMITTED" });
  state = transition(state, { type: "RENDER_COMPLETE" });
  state = transition(state, { type: "DOWNLOAD_COMPLETE" });
  state = transition(state, { type: "FILE_VALID" });
  state = transition(state, { type: "IMPORT_SUCCESS" });
  assert.equal(state.status, "NEXT_SCENE");

  state = transition(state, { type: "NEXT_SCENE_AVAILABLE", sceneNumber: 2 });
  assert.equal(state.status, "GENERATE_SCENE");
  assert.equal(state.sceneNumber, 2);

  // scene 2
  state = transition(state, { type: "PROMPT_SUBMITTED" });
  state = transition(state, { type: "RENDER_COMPLETE" });
  state = transition(state, { type: "DOWNLOAD_COMPLETE" });
  state = transition(state, { type: "FILE_VALID" });
  state = transition(state, { type: "IMPORT_SUCCESS" });
  assert.equal(state.status, "NEXT_SCENE");

  state = transition(state, { type: "QUEUE_EXHAUSTED" });
  assert.equal(state.status, "COMPLETE");
});

test("an empty queue goes straight from BUILD_VIDEO_QUEUE to COMPLETE without ever opening the browser", () => {
  let state: RunState = INITIAL_STATE;
  state = transition(state, { type: "START" });
  state = transition(state, { type: "SCENES_READ" });
  state = transition(state, { type: "QUEUE_BUILT", queueLength: 0 });
  assert.equal(state.status, "COMPLETE");
});

test("AUTH_REQUIRED is reachable from OPEN_FLOW and recovers back to OPEN_FLOW once a human logs in", () => {
  let state: RunState = INITIAL_STATE;
  state = transition(state, { type: "START" });
  state = transition(state, { type: "SCENES_READ" });
  state = transition(state, { type: "QUEUE_BUILT", queueLength: 1 });

  state = transition(state, { type: "AUTH_MISSING" });
  assert.equal(state.status, "AUTH_REQUIRED");
  assert.ok(isErrorStatus(state.status));

  // While stuck in AUTH_REQUIRED, nothing else is a valid transition.
  assert.throws(() => transition(state, { type: "QUEUE_EXHAUSTED" }));
  assert.throws(() => transition(state, { type: "FLOW_OPENED", sceneNumber: 1 }));

  state = transition(state, { type: "LOGIN_CONFIRMED" });
  assert.equal(state.status, "OPEN_FLOW");
});

test("FLOW_UNAVAILABLE is reachable from OPEN_FLOW and has no recovery transition at all", () => {
  let state: RunState = INITIAL_STATE;
  state = transition(state, { type: "START" });
  state = transition(state, { type: "SCENES_READ" });
  state = transition(state, { type: "QUEUE_BUILT", queueLength: 1 });

  state = transition(state, { type: "FLOW_LOAD_FAILED" });
  assert.equal(state.status, "FLOW_UNAVAILABLE");
  assert.ok(isErrorStatus(state.status));

  assert.throws(() => transition(state, { type: "QUEUE_EXHAUSTED" }));
  assert.throws(() => transition(state, { type: "LOGIN_CONFIRMED" }));
  assert.throws(() => transition(state, { type: "FLOW_OPENED", sceneNumber: 1 }));
});

test("GENERATION_FAILED is reachable from GENERATE_SCENE and only proceeds via SCENE_FAILED_CONTINUE, never straight to COMPLETE", () => {
  let state: RunState = { status: "GENERATE_SCENE", sceneNumber: 1 };
  state = transition(state, { type: "GENERATION_ERROR" });
  assert.equal(state.status, "GENERATION_FAILED");
  assert.ok(isErrorStatus(state.status));

  assert.throws(() => transition(state, { type: "QUEUE_EXHAUSTED" }));
  assert.throws(() => transition(state, { type: "IMPORT_SUCCESS" }));

  state = transition(state, { type: "SCENE_FAILED_CONTINUE" });
  assert.equal(state.status, "NEXT_SCENE");
});

test("RENDER_TIMEOUT is reachable from WAIT_RENDER via a timeout, and only proceeds via SCENE_FAILED_CONTINUE", () => {
  let state: RunState = { status: "WAIT_RENDER", sceneNumber: 1 };
  state = transition(state, { type: "RENDER_TIMED_OUT" });
  assert.equal(state.status, "RENDER_TIMEOUT");

  assert.throws(() => transition(state, { type: "DOWNLOAD_COMPLETE" }));

  state = transition(state, { type: "SCENE_FAILED_CONTINUE" });
  assert.equal(state.status, "NEXT_SCENE");
});

test("a Flow-reported render failure from WAIT_RENDER lands in GENERATION_FAILED (not a fabricated success)", () => {
  const state: RunState = { status: "WAIT_RENDER", sceneNumber: 1 };
  const next = transition(state, { type: "RENDER_FAILED_IN_FLOW" });
  assert.equal(next.status, "GENERATION_FAILED");
});

test("DOWNLOAD_FAILED is reachable from DOWNLOAD and only proceeds via SCENE_FAILED_CONTINUE", () => {
  let state: RunState = { status: "DOWNLOAD", sceneNumber: 1 };
  state = transition(state, { type: "DOWNLOAD_ERROR" });
  assert.equal(state.status, "DOWNLOAD_FAILED");

  assert.throws(() => transition(state, { type: "FILE_VALID" }));

  state = transition(state, { type: "SCENE_FAILED_CONTINUE" });
  assert.equal(state.status, "NEXT_SCENE");
});

test("INVALID_FILE is reachable from VALIDATE_FILE and only proceeds via SCENE_FAILED_CONTINUE", () => {
  let state: RunState = { status: "VALIDATE_FILE", sceneNumber: 1 };
  state = transition(state, { type: "FILE_INVALID" });
  assert.equal(state.status, "INVALID_FILE");

  assert.throws(() => transition(state, { type: "IMPORT_SUCCESS" }));

  state = transition(state, { type: "SCENE_FAILED_CONTINUE" });
  assert.equal(state.status, "NEXT_SCENE");
});

test("IMPORT_FAILED is reachable from IMPORT_STEP_6 and only proceeds via SCENE_FAILED_CONTINUE", () => {
  let state: RunState = { status: "IMPORT_STEP_6", sceneNumber: 1 };
  state = transition(state, { type: "IMPORT_ERROR" });
  assert.equal(state.status, "IMPORT_FAILED");

  assert.throws(() => transition(state, { type: "QUEUE_EXHAUSTED" }));

  state = transition(state, { type: "SCENE_FAILED_CONTINUE" });
  assert.equal(state.status, "NEXT_SCENE");
});

test("no invalid/fabricated transition is ever silently accepted", () => {
  assert.throws(() => transition(INITIAL_STATE, { type: "QUEUE_EXHAUSTED" }));
  assert.throws(() => transition({ status: "COMPLETE", sceneNumber: null }, { type: "START" }));
  assert.throws(() => transition({ status: "IMPORT_STEP_6", sceneNumber: 1 }, { type: "RENDER_COMPLETE" }));
  assert.throws(() => transition({ status: "GENERATE_SCENE", sceneNumber: 1 }, { type: "DOWNLOAD_COMPLETE" }));
});
