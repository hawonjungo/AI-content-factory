/**
 * Writes a small per-project JSON status file recording each queued scene's
 * current automation state:
 *   downloads/project_{projectId}/automation-status.json
 *
 * This is the "automation status representable in the app" - a future UI
 * pass can poll/read this file (or a backend endpoint backed by it); wiring
 * that up is explicitly out of scope for this package.
 */
import * as fs from "node:fs";
import * as path from "node:path";
import { projectDownloadsDir } from "./download";

export type SceneAutomationState =
  | "PENDING"
  | "GENERATING"
  | "WAITING_RENDER"
  | "DOWNLOADING"
  | "VALIDATING"
  | "IMPORTING"
  | "DONE"
  | "SKIPPED"
  | "FAILED";

export interface SceneStatusEntry {
  sceneId: string;
  sceneNumber: number;
  state: SceneAutomationState;
  message?: string;
  updatedAt: string;
}

export interface AutomationStatus {
  projectId: string;
  startedAt: string;
  updatedAt: string;
  /** Keyed by sceneId. */
  scenes: Record<string, SceneStatusEntry>;
}

export function statusFilePath(downloadsDir: string, projectId: string): string {
  return path.join(projectDownloadsDir(downloadsDir, projectId), "automation-status.json");
}

export function loadStatus(downloadsDir: string, projectId: string): AutomationStatus {
  const file = statusFilePath(downloadsDir, projectId);
  if (fs.existsSync(file)) {
    try {
      return JSON.parse(fs.readFileSync(file, "utf-8")) as AutomationStatus;
    } catch {
      // Corrupt/unreadable status file: start fresh rather than crashing the run.
    }
  }
  const now = new Date().toISOString();
  return { projectId, startedAt: now, updatedAt: now, scenes: {} };
}

export function saveStatus(downloadsDir: string, status: AutomationStatus): void {
  const file = statusFilePath(downloadsDir, status.projectId);
  fs.mkdirSync(path.dirname(file), { recursive: true });
  status.updatedAt = new Date().toISOString();
  fs.writeFileSync(file, JSON.stringify(status, null, 2), "utf-8");
}

export function updateSceneStatus(
  status: AutomationStatus,
  sceneId: string,
  sceneNumber: number,
  state: SceneAutomationState,
  message?: string,
): AutomationStatus {
  status.scenes[sceneId] = {
    sceneId,
    sceneNumber,
    state,
    message,
    updatedAt: new Date().toISOString(),
  };
  return status;
}
