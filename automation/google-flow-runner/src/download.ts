/**
 * Deterministic local file layout + validation for downloaded clips. Pure
 * filesystem logic, no Playwright, no network - fully unit-testable (see
 * tests/download.test.ts).
 */
import * as fs from "node:fs";
import * as path from "node:path";

export interface DownloadValidationResult {
  ok: boolean;
  reason?: string;
}

/**
 * `downloads/project_{projectId}/clip_{sceneNumber}.mp4`
 *
 * `sceneNumber` must be the plan's own FlowPlanScene.sceneNumber, carried
 * through unchanged from queue.ts - never a queue array index.
 */
export function clipFilePath(downloadsDir: string, projectId: string, sceneNumber: number): string {
  return path.join(downloadsDir, `project_${projectId}`, `clip_${sceneNumber}.mp4`);
}

export function projectDownloadsDir(downloadsDir: string, projectId: string): string {
  return path.join(downloadsDir, `project_${projectId}`);
}

export function ensureProjectDownloadsDir(downloadsDir: string, projectId: string): string {
  const dir = projectDownloadsDir(downloadsDir, projectId);
  fs.mkdirSync(dir, { recursive: true });
  return dir;
}

/**
 * Validates a file that is supposed to be a downloaded clip:
 *  - must exist
 *  - must be a regular file
 *  - must have a `.mp4` extension
 *  - must be non-zero bytes
 *
 * A missing or zero-byte file is ALWAYS classified as invalid - never
 * silently treated as a successful download. This intentionally does not
 * probe codec/resolution/duration (that's the backend's job via ffprobe on
 * import, see FlowClipImportResult) - this is just "is this a plausible,
 * non-corrupt local file worth uploading".
 */
export function validateDownloadedFile(targetPath: string): DownloadValidationResult {
  if (!fs.existsSync(targetPath)) {
    return { ok: false, reason: `File does not exist: ${targetPath}` };
  }

  const stats = fs.statSync(targetPath);
  if (!stats.isFile()) {
    return { ok: false, reason: `Not a regular file: ${targetPath}` };
  }

  if (path.extname(targetPath).toLowerCase() !== ".mp4") {
    return { ok: false, reason: `Expected a .mp4 file, got: ${targetPath}` };
  }

  if (stats.size <= 0) {
    return { ok: false, reason: `File is zero bytes: ${targetPath}` };
  }

  return { ok: true };
}

/** True only when a file already exists at targetPath AND passes validation. */
export function hasExistingValidClip(targetPath: string): boolean {
  return validateDownloadedFile(targetPath).ok;
}
