import type {
  FlowGenerationPlan,
  SceneGenerationType,
  SceneVisualKind,
  VideoModelTier,
  WizardClip,
  ClipState,
  CameraMovement,
  ShotSize,
} from "../api/client";

// ---------------------------------------------------------------------------
// Step 5 media type: the 4 options the unified scene card's header selector
// offers, mapped onto the existing (SceneVisualType, ModelTier) axes. Nothing
// here invents a new server concept - these are just named shortcuts for
// combinations the backend already accepts via `storyboardApi.setVisualType` /
// `setModelTier`.
// ---------------------------------------------------------------------------

export type MediaOptionId = "videoFast" | "videoLite" | "image" | "motion";

export interface MediaOptionDef {
  id: MediaOptionId;
  label: string;
  visualType: SceneVisualKind;
  modelTier: VideoModelTier | null;
}

export const MEDIA_OPTIONS: MediaOptionDef[] = [
  { id: "videoFast", label: "🎬 Video AI Fast", visualType: "AiVideo", modelTier: "Fast" },
  { id: "videoLite", label: "🎬 Video AI Lite", visualType: "AiVideo", modelTier: "Lite" },
  { id: "image", label: "🖼️ Image", visualType: "AiImage", modelTier: null },
  { id: "motion", label: "🪄 Animation / Motion", visualType: "MotionGraphic", modelTier: null },
];

export function optionById(id: MediaOptionId): MediaOptionDef {
  return MEDIA_OPTIONS.find((o) => o.id === id)!;
}

/**
 * Which selector option a scene's current (visualType, modelTier) maps to.
 * Scenes are only ever written by this selector as AiVideo/AiImage/MotionGraphic,
 * but a storyboard scene could in principle carry one of the Advanced page's
 * other visual types (ExistingFootage/TextAnimation/Diagram) - those fall back
 * to "image" (the least cost-implying, safest default) rather than crashing.
 */
export function optionIdForScene(visualType: string, modelTier: string | null): MediaOptionId {
  if (visualType === "AiVideo") return modelTier === "Fast" ? "videoFast" : "videoLite";
  if (visualType === "MotionGraphic") return "motion";
  return "image";
}

// ---------------------------------------------------------------------------
// Presets: an ordered list of (visualType, modelTier) rules, applied to scenes
// by position (0-based), wrapping around when there are more scenes than rules.
// ---------------------------------------------------------------------------

export interface MediaRule {
  visualType: SceneVisualKind;
  modelTier: VideoModelTier | null;
}

export interface MediaPreset {
  id: string;
  name: string;
  builtin: boolean;
  rules: MediaRule[];
}

const ruleOf = (id: MediaOptionId): MediaRule => {
  const o = optionById(id);
  return { visualType: o.visualType, modelTier: o.modelTier };
};

/**
 * Built-in presets, always available, not deletable. Every "Video" cell in the
 * spec's rule tables maps to Video AI Fast (rather than alternating Fast/Lite) -
 * there was no stated reason to prefer one tier over another per-scene, and
 * Fast is the existing default recommendation for hook/climax beats elsewhere
 * in the wizard (see CREDIT_STRATEGIES in steps.tsx). Per the spec text,
 * "Balanced" and "Hook Heavy" both use the literal sequence
 * (Video, Video, Image, Video, Image) - kept as given rather than invented.
 */
export const BUILTIN_PRESETS: MediaPreset[] = [
  {
    id: "builtin:balanced",
    name: "Balanced",
    builtin: true,
    rules: [ruleOf("videoFast"), ruleOf("videoFast"), ruleOf("image"), ruleOf("videoFast"), ruleOf("image")],
  },
  {
    id: "builtin:free",
    name: "Free / Credit Saver",
    builtin: true,
    rules: [ruleOf("videoFast"), ruleOf("image"), ruleOf("image"), ruleOf("motion"), ruleOf("image")],
  },
  {
    id: "builtin:hookheavy",
    name: "Hook Heavy",
    builtin: true,
    rules: [ruleOf("videoFast"), ruleOf("videoFast"), ruleOf("image"), ruleOf("videoFast"), ruleOf("image")],
  },
];

export function ruleForIndex(preset: MediaPreset, index: number): MediaRule | null {
  if (preset.rules.length === 0) return null;
  return preset.rules[index % preset.rules.length];
}

export function optionForRule(rule: MediaRule): MediaOptionDef | undefined {
  return MEDIA_OPTIONS.find((o) => o.visualType === rule.visualType && o.modelTier === rule.modelTier);
}

// ---------------------------------------------------------------------------
// localStorage persistence - this is the first localStorage usage in the repo,
// so every access is wrapped in try/catch (private browsing / quota can throw).
// ---------------------------------------------------------------------------

const USER_PRESETS_KEY = "acf:mediaPresets:v1";
const overridesKey = (contentProjectId: string) => `acf:mediaOverrides:${contentProjectId}`;

function isMediaRule(v: unknown): v is MediaRule {
  return !!v && typeof v === "object" && typeof (v as MediaRule).visualType === "string";
}

function isUserPreset(v: unknown): v is MediaPreset {
  if (!v || typeof v !== "object") return false;
  const p = v as MediaPreset;
  return typeof p.id === "string" && typeof p.name === "string" && Array.isArray(p.rules) && p.rules.every(isMediaRule);
}

export function loadUserPresets(): MediaPreset[] {
  try {
    const raw = localStorage.getItem(USER_PRESETS_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [];
    return parsed.filter(isUserPreset).map((p) => ({ ...p, builtin: false }));
  } catch {
    return [];
  }
}

export function saveUserPresets(presets: MediaPreset[]): void {
  try {
    localStorage.setItem(USER_PRESETS_KEY, JSON.stringify(presets));
  } catch {
    // Private browsing / storage quota - preset saving is best-effort only.
  }
}

export function loadOverrides(contentProjectId: string): Set<string> {
  try {
    const raw = localStorage.getItem(overridesKey(contentProjectId));
    if (!raw) return new Set();
    const parsed = JSON.parse(raw);
    return new Set(Array.isArray(parsed) ? parsed.filter((x): x is string => typeof x === "string") : []);
  } catch {
    return new Set();
  }
}

export function saveOverrides(contentProjectId: string, overrides: Set<string>): void {
  try {
    localStorage.setItem(overridesKey(contentProjectId), JSON.stringify(Array.from(overrides)));
  } catch {
    // Best-effort - a failed write just means overrides aren't remembered next visit.
  }
}

// ---------------------------------------------------------------------------
// Prompt preservation across a media-type switch (see Scene.SetVisualType,
// which clears the prompt server-side on purpose). Cached in memory per
// component instance; sessionStorage backs it up so it survives a re-render/
// remount within the same tab session (not guaranteed across a full reload).
// ---------------------------------------------------------------------------

const promptCacheStorageKey = (sceneId: string, visualType: string) => `acf:promptCache:${sceneId}:${visualType}`;

export function readCachedPrompt(sceneId: string, visualType: string): string | null {
  try {
    return sessionStorage.getItem(promptCacheStorageKey(sceneId, visualType));
  } catch {
    return null;
  }
}

export function writeCachedPrompt(sceneId: string, visualType: string, prompt: string): void {
  try {
    sessionStorage.setItem(promptCacheStorageKey(sceneId, visualType), prompt);
  } catch {
    // Best-effort - worst case the prompt just doesn't survive the type switch.
  }
}

// ---------------------------------------------------------------------------
// Merging FlowPlanScene (rich prompt/rationale/cost data) with WizardClip
// (model tier + generation status) into one shape the unified card renders.
// Matched by sceneId === clip.id (both identify the same Storyboard Scene).
// ---------------------------------------------------------------------------

export interface MergedScene {
  sceneId: string;
  sceneNumber: number;
  durationSeconds: number;
  narration: string;
  visualDescription: string;
  flowVideoPrompt: string;
  imagePrompt: string;
  /** Mirrors FlowPlanScene.isUnprompted - true when this scene has no AI-suggested/hand-edited prompt and no visual description yet, so flowVideoPrompt is empty. */
  isUnprompted: boolean;
  generationPrompt: string | null;
  characterRequired: boolean;
  referenceImageUrls: string[];
  characterReferenceImageUrl: string | null;
  characterReferenceImages: { name: string; url: string }[];
  environmentReferenceImageUrl: string | null;
  recommendFreeTool: boolean;
  rationale: string | null;
  skipGeneration: boolean;
  estimatedCredits: number;
  generationType: SceneGenerationType;
  recommendedModel: string | null;
  priority: number;
  /** Raw value as currently persisted on the scene ("AiVideo" / "AiImage" / "MotionGraphic" / a legacy Advanced-page type). */
  visualTypeRaw: string;
  modelTier: string | null;
  cameraMovement: CameraMovement;
  aiVideoPriority: number;
  allocationRationale: string | null;
  state: ClipState;
  previewUrl: string | null;
  hasExistingVideo: boolean;
  /** "None" | "Generating" | "Generated" | "Approved" | "Failed" - Step 5's optional two-stage Keyframe workflow. */
  keyframeStatus: string;
  keyframeImageUrl: string | null;
  keyframeApproved: boolean;
  motionPrompt: string;
  shotSize: ShotSize;
  /** Whether a recurring character is visible in this shot; null = not decided yet (automatic heuristic). */
  characterOnScreen: boolean | null;
  /** Still-image prompt for the scene's first frame (two-step Flow workflow); empty when not applicable. */
  firstFramePrompt: string;
}

export function mergeScenes(plan: FlowGenerationPlan, clips: WizardClip[]): MergedScene[] {
  const clipById = new Map(clips.map((c) => [c.id, c]));
  const merged: MergedScene[] = [];
  for (const s of plan.scenes) {
    const clip = clipById.get(s.sceneId);
    // A scene the clip-plan overview hasn't caught up with yet (mid-refresh) -
    // skip it this render rather than show a half-populated card.
    if (!clip) continue;
    merged.push({
      sceneId: s.sceneId,
      sceneNumber: s.sceneNumber,
      durationSeconds: clip.durationSeconds,
      narration: s.narrationText || clip.narration,
      visualDescription: s.visualDescription,
      flowVideoPrompt: s.flowVideoPrompt,
      imagePrompt: s.imagePrompt,
      isUnprompted: s.isUnprompted,
      generationPrompt: clip.generationPrompt,
      characterRequired: s.characterRequired,
      referenceImageUrls: s.referenceImageUrls,
      characterReferenceImageUrl: s.characterReferenceImageUrl,
      characterReferenceImages: s.characterReferenceImages,
      environmentReferenceImageUrl: s.environmentReferenceImageUrl,
      recommendFreeTool: s.recommendFreeTool,
      rationale: s.rationale,
      skipGeneration: s.skipGeneration,
      estimatedCredits: s.estimatedCredits,
      generationType: s.generationType,
      recommendedModel: s.recommendedModel,
      priority: s.priority,
      visualTypeRaw: clip.visualType,
      modelTier: clip.modelTier,
      cameraMovement: clip.cameraMovement,
      aiVideoPriority: clip.aiVideoPriority,
      allocationRationale: clip.allocationRationale,
      state: clip.state,
      previewUrl: clip.previewUrl,
      hasExistingVideo: clip.hasExistingVideo,
      keyframeStatus: s.keyframeStatus,
      keyframeImageUrl: s.keyframeImageUrl,
      keyframeApproved: s.keyframeApproved,
      motionPrompt: s.motionPrompt,
      shotSize: s.shotSize ?? "Unspecified",
      characterOnScreen: s.characterOnScreen ?? null,
      firstFramePrompt: s.firstFramePrompt ?? "",
    });
  }
  return merged.sort((a, b) => a.sceneNumber - b.sceneNumber);
}

// ---------------------------------------------------------------------------
// Cost estimation - never invented. Real per-type credit costs are flat
// (CreditCostOptions.VideoCreditsFor/ImageCredits are not duration-dependent),
// so the cost of "a Fast video scene" can be read off ANY scene in the current
// plan that is currently a Fast video, etc. A type not currently present
// anywhere in the plan has no observed cost and is left undefined rather than
// guessed.
// ---------------------------------------------------------------------------

function planOptionId(generationType: SceneGenerationType, recommendedModel: string | null): MediaOptionId {
  if (generationType === "AI_VIDEO") return recommendedModel === "Fast" ? "videoFast" : "videoLite";
  if (generationType === "AI_IMAGE") return "image";
  // STATIC covers MotionGraphic and the Advanced page's other non-generated
  // types; the backend prices all of them at 0, so any STATIC scene's real
  // estimatedCredits is a valid observed value for the "motion" bucket.
  return "motion";
}

export function observedCreditsByOption(scenes: MergedScene[]): Partial<Record<MediaOptionId, number>> {
  const result: Partial<Record<MediaOptionId, number>> = {};
  for (const s of scenes) {
    const id = planOptionId(s.generationType, s.recommendedModel);
    if (result[id] === undefined) {
      result[id] = s.estimatedCredits;
    }
  }
  return result;
}

/**
 * Total credits a preset would draw if applied right now, honoring overrides
 * (which keep their own real current cost, not the preset's rule). Returns
 * null when some non-overridden position's option has no observed cost yet
 * (see observedCreditsByOption) - callers should show that as "unknown"
 * rather than a fabricated number.
 */
export function estimatePresetTotal(
  scenes: MergedScene[],
  preset: MediaPreset,
  overrides: Set<string>,
  credits: Partial<Record<MediaOptionId, number>>,
): number | null {
  let total = 0;
  for (let i = 0; i < scenes.length; i++) {
    const scene = scenes[i];
    if (overrides.has(scene.sceneId)) {
      total += scene.estimatedCredits;
      continue;
    }
    const rule = ruleForIndex(preset, i);
    if (!rule) return null;
    const option = optionForRule(rule);
    if (!option) return null;
    const known = credits[option.id];
    if (known === undefined) return null;
    total += known;
  }
  return total;
}
