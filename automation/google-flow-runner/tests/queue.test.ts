import { test } from "node:test";
import assert from "node:assert/strict";
import { buildVideoQueue } from "../src/queue";
import type { FlowGenerationPlan, FlowPlanScene, SceneGenerationType } from "../src/apiClient";

interface SceneOverrides {
  sceneId: string;
  sceneNumber: number;
  generationType: SceneGenerationType;
  skipGeneration?: boolean;
}

function makeScene(overrides: SceneOverrides): FlowPlanScene {
  return {
    sceneId: overrides.sceneId,
    sceneNumber: overrides.sceneNumber,
    generationType: overrides.generationType,
    recommendedModel: overrides.generationType === "AI_VIDEO" ? "Lite" : null,
    estimatedCredits: 0,
    priority: 0,
    narrationText: "",
    captionText: "",
    visualDescription: "",
    flowVideoPrompt: `prompt for scene ${overrides.sceneNumber}`,
    imagePrompt: "",
    characterRequired: false,
    referenceImageUrls: [],
    characterReferenceImageUrl: null,
    recommendFreeTool: false,
    rationale: null,
    skipGeneration: overrides.skipGeneration ?? false,
    durationSeconds: 5,
  };
}

function makePlan(scenes: FlowPlanScene[]): FlowGenerationPlan {
  return {
    dailyBudgetCredits: 1000,
    usedCredits: 0,
    remainingCredits: 1000,
    plannedCredits: 0,
    scenesRequiringFlow: 0,
    withinBudget: true,
    fastModelLabel: "Veo Fast",
    liteModelLabel: "Veo Lite",
    scenes,
    copyAllText: "",
    aspectRatio: "9:16",
  };
}

test("buildVideoQueue keeps only non-skipped AI_VIDEO scenes", () => {
  const plan = makePlan([
    makeScene({ sceneId: "s1", sceneNumber: 1, generationType: "AI_VIDEO" }),
    makeScene({ sceneId: "s2", sceneNumber: 2, generationType: "AI_IMAGE" }),
    makeScene({ sceneId: "s3", sceneNumber: 3, generationType: "STATIC" }),
    makeScene({ sceneId: "s4", sceneNumber: 4, generationType: "AI_VIDEO", skipGeneration: true }),
    makeScene({ sceneId: "s5", sceneNumber: 5, generationType: "AI_VIDEO" }),
  ]);

  const queue = buildVideoQueue(plan);

  assert.deepEqual(
    queue.map((s) => s.sceneNumber),
    [1, 5],
  );
  assert.deepEqual(
    queue.map((s) => s.sceneId),
    ["s1", "s5"],
  );
});

test("buildVideoQueue sorts by sceneNumber even when the plan itself is out of order", () => {
  const plan = makePlan([
    makeScene({ sceneId: "s3", sceneNumber: 3, generationType: "AI_VIDEO" }),
    makeScene({ sceneId: "s1", sceneNumber: 1, generationType: "AI_VIDEO" }),
    makeScene({ sceneId: "s2", sceneNumber: 2, generationType: "AI_VIDEO" }),
  ]);

  const queue = buildVideoQueue(plan);

  assert.deepEqual(
    queue.map((s) => s.sceneNumber),
    [1, 2, 3],
  );
});

test("a non-video scene at sceneNumber 3 never causes scene 4's clip to be renamed to clip_3 - sceneNumber is carried through verbatim, never re-derived from queue position", () => {
  const plan = makePlan([
    makeScene({ sceneId: "s1", sceneNumber: 1, generationType: "AI_VIDEO" }),
    makeScene({ sceneId: "s2", sceneNumber: 2, generationType: "AI_VIDEO" }),
    makeScene({ sceneId: "s3", sceneNumber: 3, generationType: "AI_IMAGE" }), // excluded from the video queue
    makeScene({ sceneId: "s4", sceneNumber: 4, generationType: "AI_VIDEO" }),
  ]);

  const queue = buildVideoQueue(plan);

  assert.equal(queue.length, 3);
  // The 3rd queue entry (0-indexed position 2) corresponds to scene 4, NOT
  // scene 3 - the index must never be renumbered/compacted just because
  // scene 3 didn't need a Flow clip. If sceneNumber were incorrectly
  // re-derived from array position, this would be 3 (queue.length).
  assert.equal(queue[2]!.sceneNumber, 4);
  assert.equal(queue[2]!.sceneId, "s4");
  assert.notEqual(queue[2]!.sceneNumber, queue.length);
});

test("multiple non-video scenes interleaved still preserve every remaining sceneNumber exactly", () => {
  const plan = makePlan([
    makeScene({ sceneId: "s1", sceneNumber: 1, generationType: "STATIC" }),
    makeScene({ sceneId: "s2", sceneNumber: 2, generationType: "AI_VIDEO" }),
    makeScene({ sceneId: "s3", sceneNumber: 3, generationType: "AI_IMAGE" }),
    makeScene({ sceneId: "s4", sceneNumber: 4, generationType: "AI_VIDEO", skipGeneration: true }),
    makeScene({ sceneId: "s5", sceneNumber: 5, generationType: "AI_VIDEO" }),
    makeScene({ sceneId: "s6", sceneNumber: 6, generationType: "STATIC" }),
    makeScene({ sceneId: "s7", sceneNumber: 7, generationType: "AI_VIDEO" }),
  ]);

  const queue = buildVideoQueue(plan);

  assert.deepEqual(
    queue.map((s) => s.sceneNumber),
    [2, 5, 7],
  );
});

test("buildVideoQueue returns an empty array when there are no eligible AI_VIDEO scenes", () => {
  const plan = makePlan([
    makeScene({ sceneId: "s1", sceneNumber: 1, generationType: "AI_IMAGE" }),
    makeScene({ sceneId: "s2", sceneNumber: 2, generationType: "STATIC" }),
    makeScene({ sceneId: "s3", sceneNumber: 3, generationType: "AI_VIDEO", skipGeneration: true }),
  ]);

  assert.deepEqual(buildVideoQueue(plan), []);
});

test("buildVideoQueue carries the exact flowVideoPrompt through unchanged", () => {
  const plan = makePlan([
    { ...makeScene({ sceneId: "s1", sceneNumber: 1, generationType: "AI_VIDEO" }), flowVideoPrompt: "EXACT PROMPT TEXT" },
  ]);

  const queue = buildVideoQueue(plan);

  assert.equal(queue[0]!.flowVideoPrompt, "EXACT PROMPT TEXT");
});
