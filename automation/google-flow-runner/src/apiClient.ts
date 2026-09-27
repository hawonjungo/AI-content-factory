/**
 * Thin fetch wrappers around the three ai-content-factory-web HTTP endpoints
 * this tool needs. Shapes mirror (field-for-field) the TypeScript interfaces
 * in frontend/ai-content-factory-web/src/api/client.ts's `flowApi` section
 * and the C# records in
 * src/AiContentFactory.Application/Generation/FlowGenerationPlanService.cs -
 * verified against both before writing this file. Do not add fields here
 * that aren't in those two sources of truth.
 *
 * This module never talks to Google Flow - only to the app's own API.
 */
import * as fs from "node:fs/promises";
import * as path from "node:path";

export type SceneGenerationType = "AI_VIDEO" | "AI_IMAGE" | "STATIC";

export interface FlowPlanScene {
  sceneId: string;
  sceneNumber: number;
  generationType: SceneGenerationType;
  recommendedModel: string | null; // "Fast" | "Lite" for AI_VIDEO, otherwise null
  estimatedCredits: number;
  priority: number;
  narrationText: string;
  captionText: string;
  visualDescription: string;
  flowVideoPrompt: string;
  imagePrompt: string;
  characterRequired: boolean;
  referenceImageUrls: string[];
  characterReferenceImageUrl: string | null;
  recommendFreeTool: boolean;
  rationale: string | null;
  skipGeneration: boolean;
  durationSeconds: number;
}

export interface FlowGenerationPlan {
  dailyBudgetCredits: number;
  usedCredits: number;
  remainingCredits: number;
  plannedCredits: number;
  scenesRequiringFlow: number;
  withinBudget: boolean;
  fastModelLabel: string;
  liteModelLabel: string;
  scenes: FlowPlanScene[];
  copyAllText: string;
  aspectRatio: string;
}

export interface FlowImportSceneStatus {
  sceneId: string;
  sceneNumber: number;
  generationType: SceneGenerationType;
  needsFlowClip: boolean;
  hasClip: boolean;
  clipValid: boolean;
  durationSeconds: number | null;
  width: number | null;
  height: number | null;
  issues: string[];
}

export interface FlowImportStatus {
  totalScenes: number;
  scenesNeedingFlow: number;
  importedValid: number;
  missingOrInvalid: number;
  readyForRender: boolean;
  scenes: FlowImportSceneStatus[];
}

export interface FlowClipImportResult {
  sceneId: string;
  sceneNumber: number;
  accepted: boolean;
  durationSeconds: number;
  width: number;
  height: number;
  aspectLabel: string;
  issues: string[];
  warnings: string[];
}

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly body?: string,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

/**
 * The request never reached the server at all (DNS failure, connection
 * refused, timeout, TLS error, ...) - this is a pure connectivity problem
 * between this machine and `baseUrl`. It is NEVER a credits/budget issue:
 * a credits/budget condition can only come back as a normal HTTP response
 * (see FlowGenerationPlan.withinBudget) or, for Google Flow's own free-tier
 * quota specifically, from inside a live browser session - this tool never
 * even got that far if a NetworkError was thrown.
 */
export class NetworkError extends Error {
  constructor(
    message: string,
    public readonly baseUrl: string,
    public readonly cause: unknown,
  ) {
    super(message);
    this.name = "NetworkError";
  }
}

type FetchFn = typeof fetch;

async function assertOk(res: Response, context: string): Promise<Response> {
  if (!res.ok) {
    let body: string | undefined;
    try {
      body = await res.text();
    } catch {
      // best-effort only
    }
    throw new ApiError(`${context} failed: HTTP ${res.status} ${res.statusText}`, res.status, body);
  }
  return res;
}

/**
 * Runs a fetch and converts a low-level network failure (the request never
 * reaching the server) into a NetworkError with actionable guidance, instead
 * of letting a raw ECONNREFUSED/fetch-failed error bubble up unexplained.
 * HTTP-level errors (4xx/5xx, handled by assertOk) are untouched - those DID
 * reach the server and are a different kind of problem.
 */
async function fetchOrExplain(fetchImpl: FetchFn, url: string, init: RequestInit | undefined, baseUrl: string): Promise<Response> {
  try {
    return await fetchImpl(url, init);
  } catch (cause) {
    throw new NetworkError(
      `Could not reach the API at ${baseUrl} - the connection was refused or the request never got a response. ` +
        `This is a connectivity problem, not a credits/budget issue (a credits condition can only be reported by a ` +
        `successful HTTP response). Check that the backend is actually running at that address:\n` +
        `  - Docker stack (docker-compose up): usually http://localhost:8080 -> pass --base-url http://localhost:8080\n` +
        `  - Running from source (dotnet run --project src/AiContentFactory.Api): usually http://localhost:5126\n` +
        `Run "docker ps" to see if the api container is up, or check for a running "dotnet run" process, then retry.`,
      baseUrl,
      cause,
    );
  }
}

/** GET /content-projects/{contentProjectId}/flow-plan */
export async function getFlowPlan(
  baseUrl: string,
  contentProjectId: string,
  fetchImpl: FetchFn = fetch,
): Promise<FlowGenerationPlan> {
  const url = `${baseUrl}/content-projects/${contentProjectId}/flow-plan`;
  const res = await fetchOrExplain(fetchImpl, url, undefined, baseUrl);
  await assertOk(res, "GET flow-plan");
  return (await res.json()) as FlowGenerationPlan;
}

/** GET /content-projects/{contentProjectId}/flow-import-status */
export async function getFlowImportStatus(
  baseUrl: string,
  contentProjectId: string,
  fetchImpl: FetchFn = fetch,
): Promise<FlowImportStatus> {
  const url = `${baseUrl}/content-projects/${contentProjectId}/flow-import-status`;
  const res = await fetchOrExplain(fetchImpl, url, undefined, baseUrl);
  await assertOk(res, "GET flow-import-status");
  return (await res.json()) as FlowImportStatus;
}

/**
 * POST /content-projects/{contentProjectId}/storyboard/scenes/{sceneId}/video
 * multipart/form-data, field name "file" - this is the Step 6 import
 * mechanism itself (see StoryboardsController.UploadSceneVideo). There is no
 * separate "import" endpoint to build; this IS it.
 */
export async function uploadSceneVideo(
  baseUrl: string,
  contentProjectId: string,
  sceneId: string,
  filePath: string,
  fetchImpl: FetchFn = fetch,
): Promise<FlowClipImportResult> {
  const buffer = await fs.readFile(filePath);
  const form = new FormData();
  form.append("file", new Blob([buffer], { type: "video/mp4" }), path.basename(filePath));

  const url = `${baseUrl}/content-projects/${contentProjectId}/storyboard/scenes/${sceneId}/video`;
  const res = await fetchOrExplain(fetchImpl, url, { method: "POST", body: form }, baseUrl);
  // Note: a 422 ("rejected clip") is a normal, expected response shape here
  // (FlowClipImportResult with accepted:false + issues), NOT necessarily an
  // HTTP-level failure worth throwing on - but StoryboardsController returns
  // 422 via Problem(), which is !res.ok. We still want the body in that case,
  // so treat 422 specially and parse it as a FlowClipImportResult-shaped
  // problem is out of scope for this thin wrapper: importer.ts is expected to
  // treat any non-2xx here as IMPORT_FAILED, which matches "never treat a
  // rejected clip as success".
  await assertOk(res, "POST scenes/:sceneId/video");
  return (await res.json()) as FlowClipImportResult;
}
