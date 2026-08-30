const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:8080";

export interface ContentProject {
  id: string;
  title: string;
  topic?: string | null;
  niche?: string | null;
  status: string;
  targetDurationSeconds: number;
  aspectRatio: string;
  language: string;
  createdAt: string;
  updatedAt: string;
}

export interface CreateContentProjectInput {
  title: string;
  topic?: string;
  niche?: string;
  targetDurationSeconds: number;
  aspectRatio?: string;
  language?: string;
}

export interface Script {
  id: string;
  contentProjectId: string;
  hook: string;
  introduction: string;
  body: string;
  escalation: string;
  payoff: string;
  callToAction: string;
  updatedAt: string;
}

export interface UpsertScriptInput {
  hook: string;
  introduction: string;
  body: string;
  escalation: string;
  payoff: string;
  callToAction: string;
}

export type SceneVisualType =
  | "aiVideo"
  | "aiImage"
  | "existingFootage"
  | "motionGraphic"
  | "textAnimation"
  | "diagram";

export interface Scene {
  id: string;
  sceneNumber: number;
  durationSeconds: number;
  narration: string;
  visualDescription: string;
  cameraDirection: string;
  visualStyle?: string | null;
  generationPrompt?: string | null;
  negativePrompt?: string | null;
  visualType: string;
  provider?: string | null;
  status: string;
}

export interface Storyboard {
  id: string;
  contentProjectId: string;
  scenes: Scene[];
}

export interface CreateSceneInput {
  durationSeconds: number;
  narration: string;
  visualDescription: string;
  cameraDirection: string;
  visualType: SceneVisualType;
}

export interface Asset {
  id: string;
  contentProjectId: string;
  sceneId?: string | null;
  type: string;
  filePath?: string | null;
  provider?: string | null;
  prompt?: string | null;
  durationSeconds?: number | null;
  width?: number | null;
  height?: number | null;
  status: string;
  createdAt: string;
}

export type AssetType = "Video" | "Image" | "Audio" | "Voice" | "Music" | "Subtitle" | "Thumbnail";

export interface CreateAssetInput {
  sceneId?: string;
  type: AssetType;
  provider?: string;
  prompt?: string;
  filePath?: string;
  durationSeconds?: number;
  width?: number;
  height?: number;
}

async function handleResponse<T>(res: Response): Promise<T> {
  if (!res.ok) {
    const body = await res.text().catch(() => "");
    throw new Error(`Request failed (${res.status}): ${body || res.statusText}`);
  }
  if (res.status === 204) {
    return undefined as T;
  }
  return res.json() as Promise<T>;
}

const json = (body: unknown) => ({
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify(body),
});

export const contentProjectsApi = {
  getAll: () =>
    fetch(`${API_BASE_URL}/content-projects`).then((res) => handleResponse<ContentProject[]>(res)),

  getById: (id: string) =>
    fetch(`${API_BASE_URL}/content-projects/${id}`).then((res) => handleResponse<ContentProject>(res)),

  create: (input: CreateContentProjectInput) =>
    fetch(`${API_BASE_URL}/content-projects`, json(input)).then((res) => handleResponse<ContentProject>(res)),

  generate: (id: string) =>
    fetch(`${API_BASE_URL}/content-projects/${id}/generate`, { method: "POST" }).then((res) =>
      handleResponse<{ jobId: string }>(res),
    ),
};

export const scriptApi = {
  get: (contentProjectId: string) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/script`).then((res) => {
      // Older API instances return 404 when a script has not been generated yet.
      if (res.status === 204 || res.status === 404) return null;
      return handleResponse<Script>(res);
    }),

  upsert: (contentProjectId: string, input: UpsertScriptInput) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/script`, { ...json(input), method: "PUT" }).then(
      (res) => handleResponse<Script>(res),
    ),
};

export const storyboardApi = {
  get: (contentProjectId: string) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/storyboard`).then((res) =>
      handleResponse<Storyboard>(res),
    ),

  addScene: (contentProjectId: string, input: CreateSceneInput) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/storyboard/scenes`, json(input)).then((res) =>
      handleResponse<Storyboard>(res),
    ),

  removeScene: (contentProjectId: string, sceneId: string) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/storyboard/scenes/${sceneId}`, {
      method: "DELETE",
    }).then((res) => handleResponse<Storyboard>(res)),
};

export const assetsApi = {
  getAll: (contentProjectId: string) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/assets`).then((res) =>
      handleResponse<Asset[]>(res),
    ),

  create: (contentProjectId: string, input: CreateAssetInput) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/assets`, json(input)).then((res) =>
      handleResponse<Asset>(res),
    ),

  remove: (contentProjectId: string, assetId: string) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/assets/${assetId}`, {
      method: "DELETE",
    }).then((res) => handleResponse<void>(res)),
};
