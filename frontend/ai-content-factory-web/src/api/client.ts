const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:8080";

export { API_BASE_URL };

export type VoiceGenderName = "Unspecified" | "Male" | "Female";
export type CreditStrategyName = "Balanced" | "MaxImpact" | "Economy";

/** Step 2 idea configuration. All fields optional; the backend clamps/trims. */
export interface IdeaConfig {
  contentPillar?: string | null;
  targetAudience?: string | null;
  storyType?: string | null;
  hookStyle?: string | null;
  emotion?: string | null;
  voiceGender?: VoiceGenderName | null;
  voiceStyle?: string | null;
  speakingRate?: number | null;
  narrationLanguage?: string | null;
  creditStrategy?: CreditStrategyName | null;
}

export interface ContentProject {
  id: string;
  title: string;
  topic?: string | null;
  niche?: string | null;
  status: string;
  targetDurationSeconds: number;
  aspectRatio: string;
  language: string;
  templateId?: string | null;
  stylePresetId?: string | null;
  voicePresetId?: string | null;
  captionPresetId?: string | null;
  captions: CaptionSettings;
  ideaConfig: IdeaConfig;
  audioMode: AudioMode;
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
  templateId?: string;
  stylePresetId?: string;
  voicePresetId?: string;
  captionPresetId?: string;
  ideaConfig?: IdeaConfig;
}

export interface UpdateContentProjectInput {
  title: string;
  topic?: string;
  niche?: string;
  targetDurationSeconds: number;
  ideaConfig?: IdeaConfig;
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
  /** "None" | "Generating" | "Generated" | "Approved" | "Failed" - Step 5's optional two-stage Keyframe workflow. */
  keyframeStatus?: string;
  keyframeAssetId?: string | null;
  keyframeImagePrompt?: string | null;
  motionPrompt?: string | null;
  /** Framing chosen by the prompt agent: "Unspecified" | "ExtremeWide" | "Wide" | "Medium" | "MediumCloseUp" | "CloseUp" | "ExtremeCloseUp". */
  shotSize?: string;
  /** Prompt agent's decision whether a recurring character is visible in this shot; null = not decided yet (legacy heuristic applies). */
  characterOnScreen?: boolean | null;
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

  // Commands accepted for background processing commonly return 202 with no
  // response body. Calling Response.json() in that case throws before the UI
  // can refresh and show the job's progress.
  const body = await res.text();
  if (!body.trim()) {
    return undefined as T;
  }

  return JSON.parse(body) as T;
}

/**
 * Turns an Error thrown by handleResponse ("Request failed (<status>): <body>")
 * into a message safe to show the user directly: the backend's own
 * ProblemDetails `detail` when present, a specific message for the one status
 * that means something to every caller (429 = AI cost/credit limit reached),
 * or the caller's own fallback - never the raw HTTP status/JSON text.
 */
export function describeApiError(err: unknown, fallback: string): string {
  const raw = err instanceof Error ? err.message : "";
  const status = Number(raw.match(/\((\d{3})\)/)?.[1]);

  const bodyStart = raw.indexOf("): ");
  if (bodyStart !== -1) {
    try {
      const problem = JSON.parse(raw.slice(bodyStart + 3)) as { detail?: string };
      if (problem.detail?.trim()) return problem.detail.trim();
    } catch {
      // body was not JSON - fall through to the status/fallback messages below
    }
  }

  if (status === 429) {
    return "Đã đạt giới hạn chi phí / credit AI. Vui lòng thử lại sau, hoặc kiểm tra hạn mức (spend cap) của API key.";
  }
  return fallback;
}

/**
 * The machine-readable `code` of a ProblemDetails error body thrown by handleResponse (e.g. a 409
 * "REFERENCE_PENDING_CHANGED"), or null. ASP.NET flattens ProblemDetails extensions to the JSON root; a nested
 * `extensions.code` is accepted too.
 */
export function apiErrorCode(err: unknown): string | null {
  const raw = err instanceof Error ? err.message : "";
  const bodyStart = raw.indexOf("): ");
  if (bodyStart === -1) return null;
  try {
    const problem = JSON.parse(raw.slice(bodyStart + 3)) as { code?: unknown; extensions?: { code?: unknown } };
    const code = problem.code ?? problem.extensions?.code;
    return typeof code === "string" ? code : null;
  } catch {
    return null;
  }
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

  update: (id: string, input: UpdateContentProjectInput) =>
    fetch(`${API_BASE_URL}/content-projects/${id}`, { ...json(input), method: "PUT" }).then((res) =>
      handleResponse<ContentProject>(res),
    ),

  generate: (id: string) =>
    fetch(`${API_BASE_URL}/content-projects/${id}/generate`, { method: "POST" }).then((res) =>
      handleResponse<{ jobId: string }>(res),
    ),

  /** Deletes the project and its own generation data. Refused (see thrown error) while busy or while it has a publish job still in flight/retryable. */
  remove: (id: string) =>
    fetch(`${API_BASE_URL}/content-projects/${id}`, { method: "DELETE" }).then((res) => handleResponse<void>(res)),
};

export const scriptApi = {
  get: (contentProjectId: string) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/script`).then((res) => {
      if (res.status === 404) return null;
      return handleResponse<Script>(res);
    }),

  upsert: (contentProjectId: string, input: UpsertScriptInput) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/script`, { ...json(input), method: "PUT" }).then(
      (res) => handleResponse<Script>(res),
    ),
};

// ---------------------------------------------------------------------------
// Step 2 - AI content idea suggestions. Idea-level only (no scripts): the user
// picks a goal + niche, the backend returns its single best idea (still
// wrapped in `ideas` for schema compatibility - always length 0 or 1), and
// the frontend drops it straight into the normal Step 2 fields.
// ---------------------------------------------------------------------------

export type ContentIdeaGoal = "overall" | "monetization" | "views";

export interface ContentIdeaSuggestion {
  rank: number;
  title: string;
  niche: string;
  concept: string;
  hook: string;
  whyItWorks: string;
  monetizationScore: number;
  viralScore: number;
  overallScore: number;
}

export interface ContentIdeaSuggestionsInput {
  goal: ContentIdeaGoal;
  /** A niche name, or "auto" to let the AI choose. */
  niche: string;
  platforms?: string[];
  /** Project language code ("en", "vi", ...). */
  language: string;
  /** Target finished-video length in seconds. */
  duration: number;
  /** The user's own ideas + any previous suggestions to avoid repeating. */
  existingIdeas?: string[];
}

export interface ContentIdeaSuggestionsResult {
  ideas: ContentIdeaSuggestion[];
  goal: ContentIdeaGoal;
  /** Honest framing - the app has no real-time trend feed. */
  disclaimer: string;
}

export const contentIdeasApi = {
  suggest: (input: ContentIdeaSuggestionsInput) =>
    fetch(`${API_BASE_URL}/content-ideas/suggestions`, json(input)).then((res) =>
      handleResponse<ContentIdeaSuggestionsResult>(res),
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

  updateScene: (contentProjectId: string, sceneId: string, input: CreateSceneInput) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/storyboard/scenes/${sceneId}`, {
      ...json(input),
      method: "PUT",
    }).then((res) => handleResponse<Storyboard>(res)),

  removeScene: (contentProjectId: string, sceneId: string) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/storyboard/scenes/${sceneId}`, {
      method: "DELETE",
    }).then((res) => handleResponse<Storyboard>(res)),

  // Cost lever: flip one scene between an AI video clip and a cheap AI still.
  setVisualType: (contentProjectId: string, sceneId: string, visualType: SceneVisualKind) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/storyboard/scenes/${sceneId}/visual-type`, {
      ...json({ visualType }),
      method: "PUT",
    }).then((res) => handleResponse<Storyboard>(res)),

  setPrompt: (contentProjectId: string, sceneId: string, generationPrompt: string, negativePrompt?: string) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/storyboard/scenes/${sceneId}/prompt`, {
      ...json({ generationPrompt, negativePrompt }),
      method: "PUT",
    }).then((res) => handleResponse<Storyboard>(res)),

  suggestPrompt: (contentProjectId: string, sceneId: string) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/storyboard/scenes/${sceneId}/prompt/suggest`, {
      method: "POST",
    }).then((res) => handleResponse<Storyboard>(res)),

  // Bulk version of suggestPrompt above - enqueues a background job that
  // suggests a prompt for every scene that doesn't have one yet (skips
  // already-prompted scenes, tolerates individual scene failures). Same
  // ReserveJobAsync-backed "busy" convention as pipelineApi's generateAssets/
  // render/runQa, so it returns the same { jobId } shape rather than an
  // updated Storyboard - the project overview poll picks up progress/results.
  suggestAllPrompts: (contentProjectId: string, options?: { includePrompted?: boolean }) =>
    fetch(
      `${API_BASE_URL}/content-projects/${contentProjectId}/storyboard/scenes/suggest-all${
        options?.includePrompted ? "?includePrompted=true" : ""
      }`,
      { method: "POST" },
    ).then((res) => handleResponse<{ jobId: string }>(res)),

  /** Free (no AI). Stores an image made in Google Flow as the scene's approved first frame. PNG/JPEG, max 10 MB. */
  uploadFirstFrame: (contentProjectId: string, sceneId: string, file: File) => {
    const form = new FormData();
    form.append("file", file);
    return fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/storyboard/scenes/${sceneId}/first-frame`, {
      method: "POST",
      body: form,
    }).then((res) => handleResponse<unknown>(res));
  },

  /** Free (local FFmpeg, no AI). Uses the previous scene clip's last frame as this scene's first frame. */
  usePreviousClipLastFrame: (contentProjectId: string, sceneId: string) =>
    fetch(
      `${API_BASE_URL}/content-projects/${contentProjectId}/storyboard/scenes/${sceneId}/first-frame/from-previous-clip`,
      { method: "POST" },
    ).then((res) => handleResponse<unknown>(res)),

  /** Free (no AI). Downloads a Pexels video and imports it as the scene's clip - no Flow credits booked. */
  importStockVideo: (contentProjectId: string, sceneId: string, videoId: string) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/storyboard/scenes/${sceneId}/stock-video`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ videoId }),
    }).then((res) => handleResponse<FlowClipImportResult>(res)),

  /** Free. Hand-sets the scene's framing (leads the video prompt's cinematography sentence). */
  setShotSize: (contentProjectId: string, sceneId: string, shotSize: ShotSize) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/storyboard/scenes/${sceneId}/shot-size`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ shotSize }),
    }).then((res) => handleResponse<unknown>(res)),

  /** Free. Hand-sets whether a recurring character is visible in the scene; null = back to automatic. */
  setCharacterOnScreen: (contentProjectId: string, sceneId: string, characterOnScreen: boolean | null) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/storyboard/scenes/${sceneId}/character-on-screen`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ characterOnScreen }),
    }).then((res) => handleResponse<unknown>(res)),

  // Per-clip cost lever: which Veo model tier this one clip uses.
  setModelTier: (contentProjectId: string, sceneId: string, modelTier: VideoModelTier) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/storyboard/scenes/${sceneId}/model`, {
      ...json({ modelTier }),
      method: "PUT",
    }).then((res) => handleResponse<Storyboard>(res)),

  // Deterministic camera movement for one scene (feeds the "Camera:" prompt line).
  setCameraMovement: (contentProjectId: string, sceneId: string, cameraMovement: CameraMovement) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/storyboard/scenes/${sceneId}/camera`, {
      ...json({ cameraMovement }),
      method: "PUT",
    }).then((res) => handleResponse<Storyboard>(res)),

  // "Already have a video for this clip" - true skips Veo entirely; false also
  // drops any imported clip so the scene can be generated again.
  setSkipGeneration: (contentProjectId: string, sceneId: string, skip: boolean) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/storyboard/scenes/${sceneId}/skip-generation`, {
      ...json({ skip }),
      method: "PUT",
    }).then((res) => handleResponse<Storyboard>(res)),

  // The genuinely-$0 path: make the clip yourself in labs.google/flow, drop it in here.
  // Same endpoint as flowApi.importClip below - returns a FlowClipImportResult
  // (accepted/issues/warnings), not an Asset.
  uploadSceneVideo: (contentProjectId: string, sceneId: string, file: File) => {
    const form = new FormData();
    form.append("file", file);
    return fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/storyboard/scenes/${sceneId}/video`, {
      method: "POST",
      body: form,
    }).then((res) => handleResponse<FlowClipImportResult>(res));
  },

  // --- Step 5 Keyframe -> Video workflow (optional, per-scene) ---
  // Every response body here is discarded by ScenePlanPanel, which always
  // refetches the flow plan afterward (same convention as the mutators
  // above) - the plan already carries keyframeImageUrl/keyframeStatus/
  // keyframeApproved/motionPrompt, so a second round-trip isn't needed.

  /** Stage 1: generate (or regenerate) this scene's Keyframe still image. A real, billable image-generation call. */
  generateKeyframe: (contentProjectId: string, sceneId: string) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/storyboard/scenes/${sceneId}/keyframe`, {
      method: "POST",
    }).then((res) => handleResponse<Scene>(res)),

  /** Approves the scene's currently-generated Keyframe so it can anchor a video generation. */
  approveKeyframe: (contentProjectId: string, sceneId: string) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/storyboard/scenes/${sceneId}/keyframe/approve`, {
      method: "POST",
    }).then((res) => handleResponse<Storyboard>(res)),

  /** Hand-edited motion prompt for animating the approved Keyframe. Blank = compose a default at generation time. */
  setMotionPrompt: (contentProjectId: string, sceneId: string, motionPrompt: string) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/storyboard/scenes/${sceneId}/motion-prompt`, {
      ...json({ motionPrompt }),
      method: "PUT",
    }).then((res) => handleResponse<Storyboard>(res)),

  /** Stage 2: animate the APPROVED Keyframe into an 8s video via the standard Veo image-to-video call. A real, billable video-generation call. */
  generateVideoFromKeyframe: (contentProjectId: string, sceneId: string) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/storyboard/scenes/${sceneId}/keyframe/video`, {
      method: "POST",
    }).then((res) => handleResponse<Scene>(res)),
};

/**
 * The visual kinds the Step 5 media-type selector writes via `setVisualType`.
 * The backend's `SceneVisualType` enum also has `ExistingFootage`,
 * `TextAnimation` and `Diagram` (used by the separate Advanced page's manual
 * scene editor - see `AdvancedProjectPage.tsx`), but the wizard itself only
 * ever assigns one of these three, so callers that switch on this type do not
 * need to handle the others.
 */
export type SceneVisualKind = "AiVideo" | "AiImage" | "MotionGraphic";

/** Per-clip Veo model tier (drives the clip's cost). */
export type VideoModelTier = "Fast" | "Lite";

/** Deterministic camera behaviour for a scene - the prompt builder turns it into one explicit line. */
export type CameraMovement =
  | "Unspecified"
  | "Static"
  | "SlowPushIn"
  | "SlowPullOut"
  | "HandheldFollow"
  | "SideTracking"
  | "ForwardTracking"
  | "OverShoulder";

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

export interface QaScore {
  id: string;
  contentProjectId: string;
  hook: number;
  story: number;
  pacing: number;
  visualQuality: number;
  audioQuality: number;
  subtitleQuality: number;
  consistency: number;
  factualAccuracy: number;
  platformSuitability: number;
  overall: number;
  notes: string;
  createdAt: string;
}

export type GenerationModeName = "standard" | "googleflow";

export const pipelineApi = {
  // mode "standard" = your clip plan (text→video + stills). "googleflow" =
  // image→video for every scene; with autoHook it also skips your plan and
  // auto-writes a fixed ~20s hook script.
  generateAssets: (id: string, mode: GenerationModeName = "standard", autoHook = false) =>
    fetch(`${API_BASE_URL}/content-projects/${id}/generate-assets?mode=${mode}&autoHook=${autoHook}`, {
      method: "POST",
    }).then((res) => handleResponse<{ jobId: string }>(res)),

  // Captions are no longer a render argument - they live in the project's
  // caption settings, so re-rendering with different captions means saving
  // the settings first.
  render: (id: string) =>
    fetch(`${API_BASE_URL}/content-projects/${id}/render`, { method: "POST" }).then((res) =>
      handleResponse<{ jobId: string }>(res),
    ),

  runQa: (id: string) =>
    fetch(`${API_BASE_URL}/content-projects/${id}/run-qa`, { method: "POST" }).then((res) =>
      handleResponse<{ jobId: string }>(res),
    ),

  setStatus: (id: string, status: string) =>
    fetch(`${API_BASE_URL}/content-projects/${id}/status`, { ...json({ status }), method: "POST" }).then((res) =>
      handleResponse<ContentProject>(res),
    ),
};

export const qaApi = {
  getHistory: (contentProjectId: string) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/qa-scores`).then((res) =>
      handleResponse<QaScore[]>(res),
    ),
};

export function assetFileUrl(contentProjectId: string, assetId: string): string {
  return `${API_BASE_URL}/content-projects/${contentProjectId}/assets/${assetId}/file`;
}

export interface ClipCheckResult {
  assetId: string;
  checkedAt: string;
  /** "pass" | "warn" | "fail" */
  verdict: string;
  score: number;
  /** null when no recurring character is expected in the shot */
  characterMatches: boolean | null;
  actionMatches: boolean;
  issues: string[];
  summary: string;
  model: string;
  /** Estimated cost of THIS response - 0 when the stored result was returned. */
  estimatedCostUsd: number;
}

export interface StockVideo {
  id: string;
  previewImageUrl: string;
  durationSeconds: number;
  width: number;
  height: number;
  pageUrl: string;
  author: string;
  authorUrl: string;
}

/** Free stock footage (Pexels) - no AI, no cost. Needs PEXELS_API_KEY on the server. */
export const stockApi = {
  search: (query: string, page = 1) =>
    fetch(`${API_BASE_URL}/stock/videos?query=${encodeURIComponent(query)}&page=${page}`).then((res) =>
      handleResponse<StockVideo[]>(res),
    ),
};

export const clipCheckApi = {
  /**
   * BILLABLE (one vision call, ~pricing.clipCheckUsd) unless a result for the same clip is already stored
   * (then free). `force` always runs a new paid check.
   */
  check: (contentProjectId: string, sceneId: string, force = false) =>
    fetch(
      `${API_BASE_URL}/content-projects/${contentProjectId}/storyboard/scenes/${sceneId}/clip-check${force ? "?force=true" : ""}`,
      { method: "POST" },
    ).then((res) => handleResponse<ClipCheckResult>(res)),
};

// ---------------------------------------------------------------------------
// Pricing - per-action cost ESTIMATES (USD) from the backend "Pricing" config,
// shown by every CostNote. Planning figures, not provider billing.
// ---------------------------------------------------------------------------

export interface UnitPricing {
  fastVideoUsdPer8s: number;
  liteVideoUsdPer8s: number;
  imageUsd: number;
  ttsUsdPer1000Chars: number;
  textCallUsd: number;
  longTextCallUsd: number;
  clipCheckUsd: number;
}

export const pricingApi = {
  get: () => fetch(`${API_BASE_URL}/pricing`).then((res) => handleResponse<UnitPricing>(res)),
};

// ---------------------------------------------------------------------------
// Google Flow (human-in-the-loop video step)
// ---------------------------------------------------------------------------

export type SceneGenerationType = "AI_VIDEO" | "AI_IMAGE" | "STATIC";

export interface FlowPlanScene {
  sceneId: string;
  sceneNumber: number;
  generationType: SceneGenerationType;
  recommendedModel: string | null; // "Fast" | "Lite" for AI_VIDEO
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
  /** One entry per named character actually matched/tagged for this specific scene (e.g. both "Milo" and "Mimi" when a scene needs both) - superset of characterReferenceImageUrl above, which only ever carries one image. */
  characterReferenceImages: { name: string; url: string }[];
  environmentReferenceImageUrl: string | null;
  recommendFreeTool: boolean;
  rationale: string | null;
  skipGeneration: boolean;
  durationSeconds: number;
  /** True when the scene has no AI-suggested/hand-edited prompt and no written visual description - flowVideoPrompt is deliberately left empty for it (never falls back to raw narration). The UI must warn instead of letting "Copy Prompt" silently copy nothing. */
  isUnprompted: boolean;
  /** "None" | "Generating" | "Generated" | "Approved" | "Failed" - Step 5's optional two-stage Keyframe workflow (see storyboardApi.generateKeyframe/approveKeyframe). */
  keyframeStatus: string;
  /** API-relative URL of this scene's own Keyframe, present as soon as one is Generated (before approval too - so it can be previewed) - null if the scene never used the Keyframe workflow. */
  keyframeImageUrl: string | null;
  /** True once the Keyframe has been reviewed and approved - required before storyboardApi.generateVideoFromKeyframe will succeed. */
  keyframeApproved: boolean;
  /** The scene's dedicated motion prompt (hand-edited via setMotionPrompt) if set, otherwise falls back to flowVideoPrompt. */
  motionPrompt: string;
  /** Framing chosen by the prompt agent or by hand. */
  shotSize?: ShotSize;
  /** Whether a recurring character is visible in this shot; null = not decided (legacy heuristic). */
  characterOnScreen?: boolean | null;
  /** Two-step Flow workflow (video scenes): the still-image prompt for the scene's first frame. Empty when unprompted. */
  firstFramePrompt?: string;
}

export type ShotSize = "Unspecified" | "ExtremeWide" | "Wide" | "Medium" | "MediumCloseUp" | "CloseUp" | "ExtremeCloseUp";

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

export const flowApi = {
  getPlan: (contentProjectId: string) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/flow-plan`).then((res) =>
      handleResponse<FlowGenerationPlan>(res),
    ),

  getImportStatus: (contentProjectId: string) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/flow-import-status`).then((res) =>
      handleResponse<FlowImportStatus>(res),
    ),

  importClip: (contentProjectId: string, sceneId: string, file: File) => {
    const form = new FormData();
    form.append("file", file);
    return fetch(
      `${API_BASE_URL}/content-projects/${contentProjectId}/storyboard/scenes/${sceneId}/video`,
      { method: "POST", body: form },
    ).then((res) => handleResponse<FlowClipImportResult>(res));
  },
};

export type ClipPlanStrategy = "AllVideo" | "CostOptimized" | "AllImages";

export interface GenerateClipPlanInput {
  clipCount: number;
  clipDurationSeconds: number;
  /** AllVideo = every scene a Veo clip; CostOptimized = video only on hook + climax; AllImages = all cheap stills. */
  strategy?: ClipPlanStrategy;
}

export const clipPlanApi = {
  generate: (contentProjectId: string, input: GenerateClipPlanInput) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/storyboard/clip-plan`, json(input)).then((res) =>
      handleResponse<Storyboard>(res),
    ),
};

// ---------------------------------------------------------------------------
// Asset references (the "Ảnh mẫu" wizard step): Character + Environment
// consistency anchors the user reviews and locks in before any clip is made.
// ---------------------------------------------------------------------------

export type AssetReferenceType = "Character" | "Environment";
export type AssetReferenceStatus = "Pending" | "Generated" | "Approved" | "Skipped";

export interface AssetReferenceVariant {
  id: string;
  imageUrl: string;
}

export interface AssetReferenceSlot {
  type: AssetReferenceType;
  status: AssetReferenceStatus;
  imageUrl: string | null;
  prompt: string | null;
  variants: AssetReferenceVariant[];
}

export interface AssetReferenceSlots {
  character: AssetReferenceSlot;
  environment: AssetReferenceSlot;
}

const refBase = (id: string) => `${API_BASE_URL}/content-projects/${id}/asset-references`;

/** Default reference prompt for review/edit before generation (human-in-the-loop). */
export interface SuggestedReferencePrompt {
  prompt: string;
  negativePrompt: string;
  imageUsd: number;
}

export const assetReferencesApi = {
  getSlots: (contentProjectId: string) =>
    fetch(refBase(contentProjectId)).then((res) => handleResponse<AssetReferenceSlots>(res)),

  getSuggestedPrompt: (contentProjectId: string, type: AssetReferenceType) =>
    fetch(`${refBase(contentProjectId)}/${type}/prompt`).then((res) =>
      handleResponse<SuggestedReferencePrompt>(res),
    ),

  // Character generates exactly 1 image; count defaults to 1 for every type.
  generate: (contentProjectId: string, type: AssetReferenceType, opts: { count?: number; prompt?: string } = {}) =>
    fetch(`${refBase(contentProjectId)}/generate`, json({ type, count: opts.count ?? 1, prompt: opts.prompt })).then(
      (res) => handleResponse<void>(res),
    ),

  upload: (contentProjectId: string, type: AssetReferenceType, file: File) => {
    const form = new FormData();
    form.append("type", type);
    form.append("file", file);
    return fetch(`${refBase(contentProjectId)}/upload`, { method: "POST", body: form }).then((res) =>
      handleResponse<{ id: string }>(res),
    );
  },

  approve: (contentProjectId: string, refId: string) =>
    fetch(`${refBase(contentProjectId)}/${refId}/approve`, { method: "PUT" }).then((res) =>
      handleResponse<AssetReferenceSlots>(res),
    ),

  skip: (contentProjectId: string, type: AssetReferenceType) =>
    fetch(`${refBase(contentProjectId)}/${type.toLowerCase()}/skip`, { method: "PUT" }).then((res) =>
      handleResponse<AssetReferenceSlots>(res),
    ),

  deleteVariant: (contentProjectId: string, refId: string) =>
    fetch(`${refBase(contentProjectId)}/${refId}`, { method: "DELETE" }).then((res) => handleResponse<void>(res)),
};

// ---------------------------------------------------------------------------
// Wizard surface
//
// Everything below backs the six-step wizard. It deliberately speaks a
// different vocabulary from the pipeline endpoints above: user-facing clip
// states instead of AssetStatus, a step instead of a ContentProjectStatus, and
// no provider names, storage paths, or job ids anywhere.
// ---------------------------------------------------------------------------

export type CaptionPosition = "Bottom" | "Center" | "Top";
export type CaptionAnimation = "None" | "FadeIn" | "PopIn" | "SlideUp";

export interface CaptionSettings {
  enabled: boolean;
  fontFamily: string;
  fontSizePt: number;
  primaryColor: string;
  highlightColor: string;
  outlineColor: string;
  outlineWidth: number;
  shadowDepth: number;
  bold: boolean;
  uppercase: boolean;
  position: CaptionPosition;
  marginVerticalPx: number;
  maxWordsPerCue: number;
  animation: CaptionAnimation;
  karaoke: boolean;
}

export interface ContentTemplate {
  id: string;
  name: string;
  niche: string;
  description: string;
  defaultDurationSeconds: number;
  defaultAspectRatio: string;
  defaultStylePresetId: string;
  defaultVoicePresetId: string;
  defaultCaptionPresetId: string;
}

export interface NamedPreset {
  id: string;
  name: string;
  description: string;
}

export interface VoicePresetInfo extends NamedPreset {
  gender: VoiceGenderName;
  /** Free self-hosted voice (Kokoro, English only) - no per-call cost. */
  isFree?: boolean;
}

export interface CaptionPreset extends NamedPreset {
  settings: CaptionSettings;
}

export interface Pricing {
  imageUsd: number;
  videoUsdPerSecond: number;
}

export interface PresetCatalog {
  templates: ContentTemplate[];
  styles: NamedPreset[];
  voices: VoicePresetInfo[];
  captions: CaptionPreset[];
  pricing: Pricing;
}

export interface EstimateLineItem {
  label: string;
  quantity: number;
  estimatedCostUsd: number;
  estimatedSeconds: number;
  estimatedCredits: number;
}

export interface GenerationEstimate {
  lineItems: EstimateLineItem[];
  totalCostUsd: number;
  /** Estimated wall-clock WAIT to build the video - NOT its playback length (that's outputVideoSeconds). */
  totalSeconds: number;
  outputVideoSeconds: number;
  /** Reference-only draw on Google's Flow-tier daily credit figure - no longer enforced. */
  totalCredits: number;
  dailyCredits: number;
  dailyCreditsRemaining: number;
  /** Reference only - never blocks generation. */
  withinCredits: boolean;
  /** True when totalCostUsd is $0. */
  freeWithinQuota: boolean;
  clipCount: number;
  videoSceneCount: number;
  imageSceneCount: number;
  alreadyGeneratedClips: number;
  mode: string;
  /** True for Google Flow: it writes its own fixed script, so the clip plan doesn't apply. */
  clipPlanIgnored: boolean;
  monthlyBudgetRemainingUsd: number | null;
  dailyQuotaRemainingVideos: number | null;
  withinBudget: boolean;
}

export type WizardStepName = "Template" | "Idea" | "Script" | "References" | "Generate" | "Preview" | "Export";
export type ClipState = "NotStarted" | "Working" | "Ready" | "Failed";

export interface WizardClip {
  id: string;
  number: number;
  narration: string;
  durationSeconds: number;
  state: ClipState;
  visualType: SceneVisualKind;
  generationPrompt: string | null;
  previewUrl: string | null;
  hasVoice: boolean;
  /** 0-100: how much the storyboard allocator thought this scene needs motion. */
  aiVideoPriority: number;
  /** "Fast" / "Lite" for a video scene, null for a still. */
  modelTier: string | null;
  /** Deterministic camera movement for the video prompt ("Unspecified" until chosen). */
  cameraMovement: CameraMovement;
  /** Plain-language "why AI video vs image" for the storyboard review. */
  allocationRationale: string | null;
  /** User flagged "already has a video" - Veo is skipped for this clip. */
  skipGeneration: boolean;
  /** A ready video asset exists for this scene (an imported .mp4). */
  hasExistingVideo: boolean;
  /** Latest AI clip check - present only while it still describes this clip's CURRENT video. */
  clipCheck?: ClipCheckResult | null;
  /** Story character/location reference names recognized in this clip's narration; empty for non-Story projects or unmatched scenes. */
  relevantReferenceLabels: string[];
}

export type GenerationAttemptState =
  | "Pending"
  | "Generating"
  | "Completed"
  | "Failed"
  | "Retrying"
  | "Validated";

export interface GenerationAttempt {
  id: string;
  kind: string;
  sceneNumber: number | null;
  provider: string;
  model: string;
  modelTier: string | null;
  state: GenerationAttemptState;
  estimatedCredits: number;
  actualCredits: number | null;
  attemptNumber: number;
  failureReason: string | null;
  audioDurationSeconds: number | null;
  createdAt: string;
}

export interface CreditSummary {
  dailyBudget: number;
  reserved: number;
  used: number;
  remaining: number;
  failedToday: number;
  /** Credits the current storyboard would draw if generated now. */
  estimatedForProject: number;
  /** False when estimatedForProject exceeds what's left today - the UI warns and blocks. */
  withinBudget: boolean;
}

/**
 * Step 6 Voice / Audio option. "Smart" and "Original" = per clip: keep original
 * audio where present, AI voice only for narrated clips that lack it. "Generated"
 * (labelled "AI Voice") = AI voice-over for every narrated clip. "Muted" = no
 * audio at all. Subtitles are independent of this setting in every mode.
 */
export type AudioMode = "Smart" | "Original" | "Generated" | "Muted";

export interface CompositionStatus {
  narrationStatus: "none" | "missing" | "partial" | "ready" | "smart" | "original" | "muted";
  narrationSeconds: number;
  scenesWithNarration: number;
  scenesExpectingNarration: number;
  captionStatus: "disabled" | "pending" | "burned";
  compositionStatus: string;
  renderStatus: string;
  validationStatus: "pending" | "passed" | "failed";
  audioMode: AudioMode;
  validationErrors: string[];
  validationWarnings: string[];
}

export interface ValidationSummary {
  hasRun: boolean;
  ok: boolean;
  summary: string;
  durationSeconds: number;
  errors: string[];
  warnings: string[];
  checkedAt: string | null;
}

export interface WizardProgress {
  busy: boolean;
  stage: string;
  label: string;
  percent: number;
  completedUnits: number;
  totalUnits: number;
}

export interface WizardQa {
  overall: number;
  label: string;
  advice: string;
}

export interface WizardPresetSelection {
  templateId: string | null;
  templateName: string | null;
  stylePresetId: string | null;
  styleName: string | null;
  voicePresetId: string | null;
  voiceName: string | null;
  captionPresetId: string | null;
  captionName: string | null;
}

export interface ProjectOverview {
  id: string;
  title: string;
  topic: string | null;
  niche: string | null;
  targetDurationSeconds: number;
  aspectRatio: string;
  language: string;
  presets: WizardPresetSelection;
  captions: CaptionSettings;
  ideaConfig: IdeaConfig;
  step: WizardStepName;
  reachableSteps: WizardStepName[];
  progress: WizardProgress;
  script: Script | null;
  clips: WizardClip[];
  references: AssetReferenceSlots;
  finalVideoUrl: string | null;
  captionPreviewUrl: string | null;
  hasBackgroundMusic: boolean;
  googleFlowAvailable: boolean;
  estimate: GenerationEstimate;
  credits: CreditSummary;
  attempts: GenerationAttempt[];
  composition: CompositionStatus;
  lastValidation: ValidationSummary;
  qa: WizardQa | null;
  blockers: string[];
  failed: boolean;
}

export interface ApplyPresetsInput {
  templateId?: string | null;
  stylePresetId?: string | null;
  voicePresetId?: string | null;
  captionPresetId?: string | null;
}

/** Overview URLs come back API-relative so no storage key ever reaches the browser. */
export function apiUrl(relativeUrl: string): string {
  return `${API_BASE_URL}${relativeUrl}`;
}

export const presetsApi = {
  getCatalog: () => fetch(`${API_BASE_URL}/presets`).then((res) => handleResponse<PresetCatalog>(res)),
};

export const wizardApi = {
  getOverview: (contentProjectId: string) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/overview`).then((res) =>
      handleResponse<ProjectOverview>(res),
    ),

  applyPresets: (contentProjectId: string, input: ApplyPresetsInput) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/presets`, json(input)).then((res) =>
      handleResponse<ContentProject>(res),
    ),

  getCaptions: (contentProjectId: string) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/captions`).then((res) =>
      handleResponse<CaptionSettings>(res),
    ),

  updateCaptions: (contentProjectId: string, settings: CaptionSettings) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/captions`, {
      ...json(settings),
      method: "PUT",
    }).then((res) => handleResponse<CaptionSettings>(res)),

  previewCaptions: (contentProjectId: string) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/captions/preview`, { method: "POST" }).then((res) =>
      handleResponse<{ previewUrl: string }>(res),
    ),

  getEstimate: (contentProjectId: string, mode: GenerationModeName = "standard") =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/estimate?mode=${mode}`).then((res) =>
      handleResponse<GenerationEstimate>(res),
    ),

  getClipEstimate: (contentProjectId: string, sceneId: string, includeVoice: boolean) =>
    fetch(
      `${API_BASE_URL}/content-projects/${contentProjectId}/clips/${sceneId}/estimate?includeVoice=${includeVoice}`,
    ).then((res) => handleResponse<GenerationEstimate>(res)),

  regenerateClip: (contentProjectId: string, sceneId: string, input: { narration?: string; regenerateVoice: boolean }) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/clips/${sceneId}/regenerate`, json(input)).then(
      (res) => handleResponse<void>(res),
    ),

  uploadMusic: (contentProjectId: string, file: File) => {
    const form = new FormData();
    form.append("file", file);
    return fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/assets/music`, {
      method: "POST",
      body: form,
    }).then((res) => handleResponse<Asset>(res));
  },

  // Step 6 Voice option. Applied by the composition pipeline on the next render.
  setAudioMode: (contentProjectId: string, audioMode: AudioMode) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/audio-mode`, {
      ...json({ audioMode }),
      method: "PUT",
    }).then((res) => handleResponse<ContentProject>(res)),

  // Step 6 narration voice. Invalidates the project's existing TTS tracks so the
  // NEXT render regenerates them with the new voice (a re-render alone would
  // reuse the old audio).
  setVoiceSettings: (contentProjectId: string, input: VoiceSettingsInput) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/voice-settings`, {
      ...json(input),
      method: "PUT",
    }).then((res) => handleResponse<ContentProject>(res)),
};

export interface VoiceSettingsInput {
  voicePresetId?: string;
  voiceGender: VoiceGenderName;
  voiceStyle?: string;
  speakingRate?: number;
  narrationLanguage?: string;
}

// ---------------------------------------------------------------------------
// Step 7 - social publishing / scheduling (TikTok, YouTube Shorts, Instagram Reels)
// ---------------------------------------------------------------------------

export type PublishPlatform = "TikTok" | "YouTubeShorts" | "InstagramReels" | "FacebookPage";
export type SocialConnectionStatusName = "Disconnected" | "Connected" | "Expired" | "PendingSelection";
export type PublishJobStatusName =
  | "Pending"
  | "Scheduled"
  | "Publishing"
  | "Published"
  | "Failed"
  | "Cancelled";

const PLATFORM_SLUG: Record<PublishPlatform, string> = {
  TikTok: "tiktok",
  YouTubeShorts: "youtube",
  InstagramReels: "instagram",
  FacebookPage: "facebook",
};

export interface SocialPage {
  id: string;
  name: string;
}

export interface SocialConnection {
  platform: PublishPlatform;
  status: SocialConnectionStatusName;
  accountName: string | null;
  /** The platform's client key/secret are present in server config. */
  configured: boolean;
  expiresAtUtc: string | null;
  /** Connected target id (Facebook Page ID / Instagram user id / channel). */
  accountId: string | null;
  /** When status is "PendingSelection", the Facebook Pages the user can still choose from. */
  pages: SocialPage[] | null;
}

export interface PublishJob {
  id: string;
  platform: PublishPlatform;
  status: PublishJobStatusName;
  title: string;
  scheduledAtUtc: string | null;
  publishedAtUtc: string | null;
  externalPostId: string | null;
  publishedUrl: string | null;
  errorMessage: string | null;
  attemptCount: number;
  canRetry: boolean;
  isPermanentFailure: boolean;
  /** Platform-specific privacy/visibility used for this job (e.g. TikTok "SELF_ONLY", YouTube "unlisted"). Null = platform default. */
  privacy: string | null;
  /** Same relative URL as the wizard's own "final video" - matches when this job published the video currently loaded. */
  sourceVideoUrl: string;
}

/** One privacy/visibility choice a platform actually supports right now - never a hard-coded guess (TikTok's come straight from creator_info/query). */
export interface PublishPrivacyOption {
  value: string;
  label: string;
}

/**
 * Per-platform privacy/visibility settings for Step 7. Empty `privacyOptions`
 * means this platform has no supported privacy picker (Instagram, Facebook) -
 * the UI must not invent one.
 */
export interface PublishPlatformOptions {
  privacyOptions: PublishPrivacyOption[];
  defaultPrivacy: string | null;
  /** e.g. TikTok's unaudited-app explanation - show next to the picker when present. */
  notice: string | null;
}

export interface PublishSkip {
  platform: string;
  reason: string;
}

export interface PublishResult {
  created: PublishJob[];
  skipped: PublishSkip[];
}

export interface PublishInput {
  platforms: PublishPlatform[];
  title: string;
  caption?: string;
  hashtags?: string;
  mode: "now" | "schedule";
  scheduledAt?: string; // ISO-8601 UTC
  /** Per-platform privacy/visibility choice, keyed by platform. Independent per platform - never a shared value. */
  platformPrivacy?: Partial<Record<PublishPlatform, string>>;
}

export const publishApi = {
  connections: () =>
    fetch(`${API_BASE_URL}/social/connections`).then((res) => handleResponse<SocialConnection[]>(res)),

  /** Returns the provider OAuth URL to open in a new tab/window. */
  authorizeUrl: (platform: PublishPlatform) =>
    fetch(`${API_BASE_URL}/social/connections/${PLATFORM_SLUG[platform]}/authorize`).then((res) =>
      handleResponse<{ authorizationUrl: string }>(res),
    ),

  disconnect: (platform: PublishPlatform) =>
    fetch(`${API_BASE_URL}/social/connections/${PLATFORM_SLUG[platform]}`, { method: "DELETE" }).then((res) =>
      handleResponse<void>(res),
    ),

  /** Finalise a Facebook connection that manages multiple Pages. */
  selectPage: (platform: PublishPlatform, pageId: string) =>
    fetch(`${API_BASE_URL}/social/connections/${PLATFORM_SLUG[platform]}/page`, json({ pageId })).then((res) =>
      handleResponse<SocialConnection>(res),
    ),

  /** The privacy/visibility choices this connected platform supports right now (TikTok: live creator_info/query; YouTube: fixed set; others: empty). */
  publishOptions: (platform: PublishPlatform) =>
    fetch(`${API_BASE_URL}/social/connections/${PLATFORM_SLUG[platform]}/publish-options`).then((res) =>
      handleResponse<PublishPlatformOptions>(res),
    ),

  jobs: (contentProjectId: string) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/publish`).then((res) =>
      handleResponse<PublishJob[]>(res),
    ),

  publish: (contentProjectId: string, input: PublishInput) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/publish`, json(input)).then((res) =>
      handleResponse<PublishResult>(res),
    ),

  retry: (contentProjectId: string, jobId: string) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/publish/${jobId}/retry`, { method: "POST" }).then(
      (res) => handleResponse<PublishJob>(res),
    ),

  /** Deletes Failed/Cancelled history entries. Successful publishes (and their links) are never removed. Returns what's left. */
  clearHistory: (contentProjectId: string) =>
    fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/publish/history`, { method: "DELETE" }).then((res) =>
      handleResponse<PublishJob[]>(res),
    ),
};

// ---------------------------------------------------------------------------
// Stories - multi-episode "series" content with cross-episode continuity
// (Story Bible + Story State + per-episode outline/script/validation). This
// is a separate pipeline from ContentProject: a Story episode's script is a
// plain text field with no link to a ContentProject/storyboard, so it does
// not (yet) feed into scene/asset/video generation.
// ---------------------------------------------------------------------------

export type StoryEpisodeStatus = "Draft" | "Scripted" | "Completed";

/** Opt-in prompt-instruction profile for a character; scopes cat/kitten-specific
 * anthropomorphic behavior instructions to only the characters that request them. */
export type StoryCharacterBehaviorProfile = "None" | "AnthropomorphicCat";

/** Canonical classification of a character (drives the neutral reference pose). Separate from `behaviorProfile`,
 * which only scopes scene-action behavior instructions. Sent/received as the enum name, like `behaviorProfile`. */
export type StoryCharacterKind = "Unspecified" | "Animal" | "AnthropomorphicAnimal" | "Human" | "Other";

/** Where a reference image came from: generated in-app (billable) or uploaded by the user (free). */
export type StoryReferenceImageSource = "Generated" | "Uploaded";

/** Ids the backend uses in `missingReferenceFields` / `missingFields`. */
export type StoryCharacterCanonicalField = "appearance" | "species" | "clothingAndAccessories" | "distinctiveFeatures";

export interface StoryCharacterResponse {
  id: string;
  name: string;
  description: string | null;
  visualDescription: string | null;
  referenceImageStatus: string;
  hasReferenceImage: boolean;
  behaviorProfile: StoryCharacterBehaviorProfile;
  kind: StoryCharacterKind;
  species: string | null;
  clothingAndAccessories: string | null;
  distinctiveFeatures: string | null;
  /** True when a candidate image is waiting to be approved/discarded (the approved image is untouched). */
  hasPendingReferenceImage: boolean;
  /** Source of the CURRENT (approved / latest) reference image. */
  referenceImageSource: StoryReferenceImageSource;
  pendingReferenceImageSource: StoryReferenceImageSource | null;
  /** Identifies the current candidate image; sent back as `expectedPendingVersion` when approving it. */
  pendingVersion: string | null;
  /** Canonical field ids still empty (appearance | species | clothingAndAccessories | distinctiveFeatures). */
  missingReferenceFields: string[];
  /** False when the paid in-app generation should be blocked (not enough character info). */
  canGenerateReference: boolean;
}

export interface StoryLocationResponse {
  id: string;
  name: string;
  description: string | null;
  visualDescription: string | null;
  referenceImageStatus: string;
  hasReferenceImage: boolean;
}

export interface StoryReferenceImageResponse {
  id: string;
  status: "Pending" | "Generated" | "Approved" | "Skipped" | "Superseded";
  imagePath: string | null;
  prompt: string | null;
  provider: string | null;
  /** Character images: whether a CANDIDATE image is waiting; `source`/`pendingSource` = "Generated" | "Uploaded". */
  hasPending: boolean;
  source: string | null;
  pendingSource: string | null;
  /** Identifies the current candidate image (null when there is none). */
  pendingVersion: string | null;
}

/** Free (no AI) prompt for producing the reference image outside the app. */
export type StoryReferencePromptTarget = "inapp" | "generic" | "flow" | "midjourney" | "dalle" | "flux";

export interface StoryReferencePromptResponse {
  target: string;
  prompt: string;
  negativePrompt: string | null;
  /** Canonical field ids still empty (same ids as `missingReferenceFields`). */
  missingFields: string[];
  notes: string[];
}

export interface SyncStoryReferencesResponse {
  contentProjectId: string;
  /** References newly seeded by this call, formatted "{Type}: {Label}" (e.g. "Character: Milo"); empty when nothing to do. */
  syncedLabels?: string[];
  /** Characters whose episode-level approved reference differs from the Story's current approved image (detection only). */
  outdatedLabels?: string[];
}

export interface StoryEpisodeSummaryResponse {
  id: string;
  storyId: string;
  episodeNumber: number;
  title: string;
  status: StoryEpisodeStatus;
  contentProjectId: string | null;
}

export interface StoryStateSnapshotResponse {
  currentLocation: string | null;
  currentObjective: string | null;
  characterStates: string | null;
  importantEvents: string[];
  openStoryThreads: string[];
  unresolvedConflicts: string[];
  knownFacts: string[];
  nextPlannedDestination: string | null;
  notes: string | null;
}

export interface StoryEpisodeResponse {
  id: string;
  storyId: string;
  episodeNumber: number;
  title: string;
  script: string | null;
  summary: string | null;
  previousEpisodeId: string | null;
  status: StoryEpisodeStatus;
  storyStateSnapshot: StoryStateSnapshotResponse | null;
  contentProjectId: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface StoryStateResponse {
  storyId: string;
  currentLocation: string | null;
  currentObjective: string | null;
  characterStates: string | null;
  importantEvents: string[];
  openStoryThreads: string[];
  unresolvedConflicts: string[];
  knownFacts: string[];
  nextPlannedDestination: string | null;
  notes: string | null;
  updatedAt: string;
}

export interface StoryResponse {
  id: string;
  title: string;
  premise: string | null;
  niche: string | null;
  language: string;
  aspectRatio: string;
  /**
   * Visual style preset (id from presetsApi.getCatalog().styles) chosen for the whole Story. Used for the character
   * reference images and inherited by every NEW episode video; already-created episodes keep their own style.
   * Null = none chosen (generation then falls back to the system's default style, which can differ per generation path).
   */
  stylePresetId: string | null;
  characters: StoryCharacterResponse[];
  locations: StoryLocationResponse[];
  episodes: StoryEpisodeSummaryResponse[];
  episodeCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface StoryBibleResponse {
  premise: string | null;
  worldRules: string[];
  characterDefinitions: string | null;
  characterRelationships: string | null;
  visualConsistencyRules: string[];
  tone: string | null;
  recurringElements: string[];
  storyConstraints: string[];
  storyArc: string | null;
}

export interface StoryEpisodeOutlineResponse {
  episodeId: string;
  title: string | null;
  objective: string | null;
  setup: string | null;
  majorBeats: string[];
  conflict: string | null;
  escalation: string | null;
  resolution: string | null;
  cliffhanger: string | null;
  continuityRequirements: string[];
  scenesRequired: string[];
}

export type ContinuityIssueCategory = "Character" | "Location" | "Timeline" | "Object" | "Plot" | "Other";

export interface ContinuityIssueResponse {
  category: ContinuityIssueCategory;
  type: string;
  message: string;
}

export interface ContinuityValidationResponse {
  valid: boolean;
  /** 0.0-1.0 self-assessed continuity-consistency score - display only, no threshold logic. */
  score: number;
  warnings: ContinuityIssueResponse[];
  criticalIssues: ContinuityIssueResponse[];
}

export interface FinalizeEpisodeResponse {
  valid: boolean;
  score: number;
  warnings: ContinuityIssueResponse[];
  criticalIssues: ContinuityIssueResponse[];
  episode: StoryEpisodeResponse | null;
}

export interface UpdateEpisodeScriptInput {
  script: string;
}

export interface CreateStoryInput {
  title: string;
  premise?: string;
  niche?: string;
  language?: string;
  aspectRatio?: string;
  /** Optional style preset id; null/omitted = no style chosen. */
  stylePresetId?: string | null;
}

export interface UpdateStoryInput {
  title: string;
  premise?: string;
  niche?: string;
  /** Optional style preset id. On UPDATE, omitted (or null) = leave the style unchanged; a blank string "" = clear it. */
  stylePresetId?: string;
}

export interface CreateStoryEpisodeInput {
  episodeNumber: number;
  title: string;
  previousEpisodeId?: string;
}

export interface CreateStoryCharacterInput {
  name: string;
  description?: string;
  visualDescription?: string;
  behaviorProfile?: StoryCharacterBehaviorProfile;
  kind?: StoryCharacterKind;
  species?: string;
  clothingAndAccessories?: string;
  distinctiveFeatures?: string;
}

/** Canonical fields on update: omitted/undefined = unchanged, "" = clear (for `kind`, omitted = unchanged). */
export interface UpdateStoryCharacterInput {
  name: string;
  description?: string;
  visualDescription?: string;
  behaviorProfile?: StoryCharacterBehaviorProfile;
  kind?: StoryCharacterKind;
  species?: string;
  clothingAndAccessories?: string;
  distinctiveFeatures?: string;
}

export interface CreateStoryLocationInput {
  name: string;
  description?: string;
  visualDescription?: string;
}

export interface UpdateStoryBibleInput {
  premise?: string;
  worldRules?: string[];
  characterDefinitions?: string;
  characterRelationships?: string;
  visualConsistencyRules?: string[];
  tone?: string;
  recurringElements?: string[];
  storyConstraints?: string[];
  storyArc?: string;
}

export interface GenerateStoryBibleInput {
  theme?: string;
  genre?: string;
  tone?: string;
  targetAudience?: string;
  mainCharacters?: string[];
  locations?: string[];
  storyRulesConstraints?: string;
  desiredEpisodeCount?: number;
}

export const storiesApi = {
  getAll: () => fetch(`${API_BASE_URL}/stories`).then((res) => handleResponse<StoryResponse[]>(res)),

  getById: (id: string) => fetch(`${API_BASE_URL}/stories/${id}`).then((res) => handleResponse<StoryResponse>(res)),

  create: (input: CreateStoryInput) =>
    fetch(`${API_BASE_URL}/stories`, json(input)).then((res) => handleResponse<StoryResponse>(res)),

  update: (id: string, input: UpdateStoryInput) =>
    fetch(`${API_BASE_URL}/stories/${id}`, { ...json(input), method: "PUT" }).then((res) =>
      handleResponse<StoryResponse>(res),
    ),

  addEpisode: (id: string, input: CreateStoryEpisodeInput) =>
    fetch(`${API_BASE_URL}/stories/${id}/episodes`, json(input)).then((res) =>
      handleResponse<StoryEpisodeResponse>(res),
    ),

  getEpisodes: (id: string) =>
    fetch(`${API_BASE_URL}/stories/${id}/episodes`).then((res) =>
      handleResponse<StoryEpisodeSummaryResponse[]>(res),
    ),

  getEpisode: (id: string, episodeId: string) =>
    fetch(`${API_BASE_URL}/stories/${id}/episodes/${episodeId}`).then((res) =>
      handleResponse<StoryEpisodeResponse>(res),
    ),

  addCharacter: (id: string, input: CreateStoryCharacterInput) =>
    fetch(`${API_BASE_URL}/stories/${id}/characters`, json(input)).then((res) =>
      handleResponse<StoryCharacterResponse>(res),
    ),

  updateCharacter: (id: string, characterId: string, input: UpdateStoryCharacterInput) =>
    fetch(`${API_BASE_URL}/stories/${id}/characters/${characterId}`, { ...json(input), method: "PUT" }).then((res) =>
      handleResponse<StoryCharacterResponse>(res),
    ),

  addLocation: (id: string, input: CreateStoryLocationInput) =>
    fetch(`${API_BASE_URL}/stories/${id}/locations`, json(input)).then((res) =>
      handleResponse<StoryLocationResponse>(res),
    ),

  getState: (id: string) =>
    fetch(`${API_BASE_URL}/stories/${id}/state`).then((res) => handleResponse<StoryStateResponse>(res)),

  /** Overwrites any existing Bible - always confirm with the user before calling this on a Story that already has one. */
  generateBible: (id: string, input: GenerateStoryBibleInput) =>
    fetch(`${API_BASE_URL}/stories/${id}/bible`, json(input)).then((res) => handleResponse<StoryBibleResponse>(res)),

  /** Returns null (not a thrown error) when no Bible has been generated yet - callers should show an empty state, not an error. */
  getBible: (id: string) =>
    fetch(`${API_BASE_URL}/stories/${id}/bible`).then((res) => {
      if (res.status === 404) return null;
      return handleResponse<StoryBibleResponse>(res);
    }),

  /** Plain CRUD edit - no AI call, replaces the Bible wholesale. Requires a Bible to already exist server-side (404 otherwise). */
  updateBible: (id: string, input: UpdateStoryBibleInput) =>
    fetch(`${API_BASE_URL}/stories/${id}/bible`, { ...json(input), method: "PUT" }).then((res) =>
      handleResponse<StoryBibleResponse>(res),
    ),

  /** Requires a Bible to already exist server-side - throws (surface via describeApiError) otherwise. */
  generateOutline: (id: string, episodeId: string) =>
    fetch(`${API_BASE_URL}/stories/${id}/episodes/${episodeId}/outline`, { method: "POST" }).then((res) =>
      handleResponse<StoryEpisodeOutlineResponse>(res),
    ),

  /** Returns null (not a thrown error) when no outline has been generated yet for this episode. */
  getOutline: (id: string, episodeId: string) =>
    fetch(`${API_BASE_URL}/stories/${id}/episodes/${episodeId}/outline`).then((res) => {
      if (res.status === 404) return null;
      return handleResponse<StoryEpisodeOutlineResponse>(res);
    }),

  /** Requires an outline to already exist server-side (422 otherwise). */
  generateScript: (id: string, episodeId: string) =>
    fetch(`${API_BASE_URL}/stories/${id}/episodes/${episodeId}/script`, { method: "POST" }).then((res) =>
      handleResponse<StoryEpisodeResponse>(res),
    ),

  validateEpisode: (id: string, episodeId: string) =>
    fetch(`${API_BASE_URL}/stories/${id}/episodes/${episodeId}/validate`, { method: "POST" }).then((res) =>
      handleResponse<ContinuityValidationResponse>(res),
    ),

  /**
   * Always 200 OK for both outcomes. `valid` is `criticalIssues.length === 0`.
   * `valid:true` means the episode is now Completed and StoryState was updated
   * (`episode` populated) - warnings alone do NOT block this. `valid:false`
   * means nothing was mutated server-side - show the critical issues and let
   * the user regenerate or manually fix the script, this is NOT an error state.
   */
  finalizeEpisode: (id: string, episodeId: string) =>
    fetch(`${API_BASE_URL}/stories/${id}/episodes/${episodeId}/finalize`, { method: "POST" }).then((res) =>
      handleResponse<FinalizeEpisodeResponse>(res),
    ),

  /** Persists a manually-edited script directly - no AI call, no regeneration. */
  updateEpisodeScript: (id: string, episodeId: string, script: string) =>
    fetch(`${API_BASE_URL}/stories/${id}/episodes/${episodeId}/script`, {
      ...json({ script } satisfies UpdateEpisodeScriptInput),
      method: "PUT",
    }).then((res) => handleResponse<StoryEpisodeResponse>(res)),

  /**
   * Creates a ContentProject from the episode's script the first time
   * (`created: true`), reuses the existing one on every subsequent call
   * (`created: false`). No AI/LLM call involved - pure orchestration, no
   * CostNote needed for this action.
   */
  createOrOpenVideo: (id: string, episodeId: string) =>
    fetch(`${API_BASE_URL}/stories/${id}/episodes/${episodeId}/video`, { method: "POST" }).then((res) =>
      handleResponse<{ contentProjectId: string; created: boolean }>(res),
    ),

  /**
   * Copies any newly-approved Character/Environment reference images from the
   * Story onto an already-existing episode video. No AI/LLM call involved
   * (copies an already-approved image's file path) - no CostNote needed.
   * 400 if the episode has no linked video yet.
   */
  syncVideoReferences: (id: string, episodeId: string) =>
    fetch(`${API_BASE_URL}/stories/${id}/episodes/${episodeId}/video/sync-references`, { method: "POST" }).then(
      (res) => handleResponse<SyncStoryReferencesResponse>(res),
    ),

  /**
   * Billable AI image call - only invoke on explicit user action (generate/regenerate). When the character already
   * has an APPROVED image this creates a CANDIDATE (the approved image is untouched); otherwise it sets the image.
   */
  generateCharacterReferenceImage: (id: string, characterId: string) =>
    fetch(`${API_BASE_URL}/stories/${id}/characters/${characterId}/reference-image`, { method: "POST" }).then(
      (res) => handleResponse<StoryReferenceImageResponse>(res),
    ),

  /**
   * Free. Uploads an image made outside the app (PNG/JPEG only, ~10MB) as the reference image; like generation,
   * it becomes a CANDIDATE when an approved image already exists.
   */
  uploadCharacterReferenceImage: (id: string, characterId: string, file: File) => {
    const form = new FormData();
    form.append("file", file);
    return fetch(`${API_BASE_URL}/stories/${id}/characters/${characterId}/reference-image/upload`, {
      method: "POST",
      body: form,
    }).then((res) => handleResponse<StoryReferenceImageResponse>(res));
  },

  /**
   * Free. Drops the pending candidate image; the current (approved) image is kept. The response body is not
   * specified by the contract, so `undefined` (empty body, e.g. 204) is possible - callers must not rely on it.
   */
  discardCharacterPendingReferenceImage: (id: string, characterId: string) =>
    fetch(`${API_BASE_URL}/stories/${id}/characters/${characterId}/reference-image/pending`, {
      method: "DELETE",
    }).then((res) => handleResponse<StoryReferenceImageResponse | undefined>(res)),

  /**
   * 400 if the character has nothing to approve. No AI call. Promoting a pending candidate over an existing approved
   * image is REJECTED (409 code REFERENCE_REPLACE_CONFIRMATION_REQUIRED) unless `confirmReplace: true` - only pass
   * that after the user explicitly confirmed. `expectedPendingVersion` = `pendingVersion` of the candidate the user
   * is looking at; if another tab replaced it meanwhile the server answers 409 code REFERENCE_PENDING_CHANGED
   * (see `apiErrorCode`).
   */
  approveCharacterReferenceImage: (
    id: string,
    characterId: string,
    options?: { confirmReplace?: boolean; expectedPendingVersion?: string | null },
  ) => {
    const query = new URLSearchParams();
    if (options?.confirmReplace !== undefined) query.set("confirmReplace", String(options.confirmReplace));
    if (options?.expectedPendingVersion) query.set("expectedPendingVersion", options.expectedPendingVersion);
    const qs = query.toString();
    return fetch(
      `${API_BASE_URL}/stories/${id}/characters/${characterId}/reference-image/approve${qs ? `?${qs}` : ""}`,
      { method: "PUT" },
    ).then((res) => handleResponse<StoryReferenceImageResponse>(res));
  },

  /** Free (no AI). Prompt text to paste into an external image tool; `target` picks the tool-specific wording. */
  getCharacterReferencePrompt: (id: string, characterId: string, target: StoryReferencePromptTarget) =>
    fetch(
      `${API_BASE_URL}/stories/${id}/characters/${characterId}/reference-prompt?target=${encodeURIComponent(target)}`,
    ).then((res) => handleResponse<StoryReferencePromptResponse>(res)),

  /** Billable AI image call - only invoke on explicit user action (generate/regenerate). */
  generateLocationReferenceImage: (id: string, locationId: string) =>
    fetch(`${API_BASE_URL}/stories/${id}/locations/${locationId}/reference-image`, { method: "POST" }).then((res) =>
      handleResponse<StoryReferenceImageResponse>(res),
    ),

  /** 400 if the location's reference image is not yet Generated. No AI call. */
  approveLocationReferenceImage: (id: string, locationId: string) =>
    fetch(`${API_BASE_URL}/stories/${id}/locations/${locationId}/reference-image/approve`, {
      method: "PUT",
    }).then((res) => handleResponse<StoryReferenceImageResponse>(res)),
};

/** Current (approved / latest) reference image. `cacheKey` is an optional cache-buster for after the image changed. */
export function characterReferenceImageFileUrl(storyId: string, characterId: string, cacheKey?: string | number): string {
  const url = `${API_BASE_URL}/stories/${storyId}/characters/${characterId}/reference-image/file`;
  return cacheKey === undefined ? url : `${url}?v=${cacheKey}`;
}

/** Candidate (pending) reference image awaiting approval - 404 when there is none. */
export function characterPendingReferenceImageFileUrl(
  storyId: string,
  characterId: string,
  cacheKey?: string | number,
): string {
  const url = `${API_BASE_URL}/stories/${storyId}/characters/${characterId}/reference-image/pending/file`;
  return cacheKey === undefined ? url : `${url}?v=${cacheKey}`;
}

export function locationReferenceImageFileUrl(storyId: string, locationId: string): string {
  return `${API_BASE_URL}/stories/${storyId}/locations/${locationId}/reference-image/file`;
}
