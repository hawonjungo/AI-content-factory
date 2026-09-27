/**
 * Pure, network-free, browser-free logic for turning a FlowGenerationPlan
 * into the ordered list of scenes this tool actually needs to drive through
 * Google Flow. Fully unit-testable (see tests/queue.test.ts).
 */
import type { FlowGenerationPlan, FlowPlanScene } from "./apiClient";

export interface QueuedScene {
  sceneId: string;
  /**
   * The deterministic 1-based scene index from the backend
   * (FlowPlanScene.sceneNumber). This is used verbatim everywhere (file
   * names, status keys, logs) and must NEVER be re-derived from the scene's
   * position in this queue - a scene being AI_IMAGE/STATIC/skipped must not
   * shift the numbering of later scenes.
   */
  sceneNumber: number;
  flowVideoPrompt: string;
  recommendedModel: string | null;
  durationSeconds: number;
}

function toQueuedScene(scene: FlowPlanScene): QueuedScene {
  return {
    sceneId: scene.sceneId,
    sceneNumber: scene.sceneNumber,
    flowVideoPrompt: scene.flowVideoPrompt,
    recommendedModel: scene.recommendedModel,
    durationSeconds: scene.durationSeconds,
  };
}

/**
 * Builds the deterministic AI_VIDEO generation queue from a FlowGenerationPlan:
 * - only `generationType === "AI_VIDEO"` scenes
 * - only scenes where `!skipGeneration` (the user already has a usable clip
 *   for `skipGeneration` scenes, so there is nothing for Flow to do)
 * - sorted by `sceneNumber` ascending (the plan's own scenes are already in
 *   this order, but we don't rely on that - this is defensive and explicit)
 *
 * `sceneNumber` on each returned item is carried through unchanged; it is
 * never recomputed from the resulting array index.
 */
export function buildVideoQueue(plan: FlowGenerationPlan): QueuedScene[] {
  return plan.scenes
    .filter((s) => s.generationType === "AI_VIDEO" && !s.skipGeneration)
    .slice()
    .sort((a, b) => a.sceneNumber - b.sceneNumber)
    .map(toQueuedScene);
}
