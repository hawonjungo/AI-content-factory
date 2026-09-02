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
  uploadSceneVideo: (contentProjectId: string, sceneId: string, file: File) => {
    const form = new FormData();
    form.append("file", file);
    return fetch(`${API_BASE_URL}/content-projects/${contentProjectId}/storyboard/scenes/${sceneId}/video`, {
      method: "POST",
      body: form,
    }).then((res) => handleResponse<Asset>(res));
  },
};

/** The two visual kinds a scene can be built as. */
export type SceneVisualKind = "AiVideo" | "AiImage";

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
  rationale: string | null;
  skipGeneration: boolean;
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

export interface CompositionStatus {
  narrationStatus: "none" | "missing" | "partial" | "ready";
  narrationSeconds: number;
  scenesWithNarration: number;
  scenesExpectingNarration: number;
  captionStatus: "disabled" | "pending" | "burned";
  compositionStatus: string;
  renderStatus: string;
  validationStatus: "pending" | "passed" | "failed";
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
};

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
};
