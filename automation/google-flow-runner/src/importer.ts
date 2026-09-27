/**
 * Wraps the Step 6 import call (POST .../storyboard/scenes/{sceneId}/video)
 * with idempotency: before uploading, checks flow-import-status so a scene
 * that already has a valid imported clip is skipped rather than
 * re-uploaded/re-billed-against-credits, unless the caller explicitly forces
 * a re-run.
 *
 * There is deliberately no separate "bulk folder import" here - the caller
 * already has the freshly-downloaded file in hand right after download.ts
 * validates it, so this just does the one upload for that one scene.
 */
import {
  type FlowClipImportResult,
  type FlowImportStatus,
  getFlowImportStatus,
  uploadSceneVideo,
} from "./apiClient";

export interface ImportOutcome {
  skipped: boolean;
  result?: FlowClipImportResult;
  reason?: string;
}

export function findSceneImportStatus(status: FlowImportStatus, sceneId: string) {
  return status.scenes.find((s) => s.sceneId === sceneId);
}

export function isAlreadyImported(status: FlowImportStatus, sceneId: string): boolean {
  const scene = findSceneImportStatus(status, sceneId);
  return !!scene && scene.hasClip && scene.clipValid;
}

export interface ImportClipOptions {
  force: boolean;
  fetchImpl?: typeof fetch;
}

/**
 * Idempotent import of one scene's clip. Never throws on a "rejected clip"
 * from the backend (accepted:false) - the caller inspects
 * `result.accepted`/`issues`/`warnings` and decides the run state
 * (IMPORT_FAILED when `!accepted`). A network/HTTP-level failure to reach
 * the API does throw, since there's nothing meaningful to report otherwise.
 */
export async function importClip(
  baseUrl: string,
  projectId: string,
  sceneId: string,
  filePath: string,
  options: ImportClipOptions,
): Promise<ImportOutcome> {
  const status = await getFlowImportStatus(baseUrl, projectId, options.fetchImpl);

  if (!options.force && isAlreadyImported(status, sceneId)) {
    return {
      skipped: true,
      reason: "Scene already has a valid imported clip on the server - skipping upload (use --force to re-import).",
    };
  }

  const result = await uploadSceneVideo(baseUrl, projectId, sceneId, filePath, options.fetchImpl);
  return { skipped: false, result };
}
