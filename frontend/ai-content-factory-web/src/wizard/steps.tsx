import { useEffect, useMemo, useRef, useState } from "react";
import {
  apiUrl,
  clipPlanApi,
  contentProjectsApi,
  describeApiError,
  pipelineApi,
  scriptApi,
  storyboardApi,
  wizardApi,
  type ClipPlanStrategy,
  type CreditStrategyName,
  type FlowGenerationPlan,
  type GenerationEstimate,
  type IdeaConfig,
  type PresetCatalog,
  type ProjectOverview,
  type UpsertScriptInput,
  type WizardStepName,
} from "../api/client";
import type { ContentIdeaSuggestion } from "../api/client";
import { AudioModeSelector } from "./AudioModeSelector";
import { VoiceSettings } from "./VoiceSettings";
import { CaptionEditor } from "./CaptionEditor";
import { ClipCard } from "./ClipCard";
import { ContentIdeaSuggestions } from "./ContentIdeaSuggestions";
import { FlowImportPanel } from "./FlowImportPanel";
import { ScenePlanPanel } from "./ScenePlanPanel";
import { PublishPanel } from "./PublishPanel";
import {
  AttemptsPanel,
  Blockers,
  CompositionStatusPanel,
  CostEstimate,
  CostNote,
  CreditPanel,
  Field,
  PresetPicker,
  StatusBadge,
  ValidationList,
  formatDuration,
  formatUsdEstimate,
  usePricing,
} from "./components";

const STORY_TYPES = ["Kể chuyện", "Case study", "Phản biện / bóc phốt", "Hướng dẫn", "Danh sách", "So sánh", "Giải thích"];
const HOOK_STYLES = ["Câu hỏi sốc", "Con số gây sốc", "Tuyên bố táo bạo", "Nghịch lý", "Bí ẩn mở màn"];
const EMOTIONS = ["Tò mò", "Kinh ngạc", "Căng thẳng", "Ấm áp", "Hài hước", "Truyền cảm hứng"];
const CREDIT_STRATEGIES: { value: CreditStrategyName; label: string; hint: string }[] = [
  { value: "Balanced", label: "Cân bằng", hint: "1 clip Fast cho hook + các beat mạnh trong ngân sách (20+10+10+10)." },
  { value: "MaxImpact", label: "Tối đa hình ảnh", hint: "Dùng hết ngân sách cho video ở mọi cảnh có thể." },
  { value: "Economy", label: "Tiết kiệm", hint: "Chỉ 1 cảnh hero là video, còn lại ảnh tĩnh." },
];

export interface StepProps {
  overview: ProjectOverview;
  catalog: PresetCatalog;
  busy: boolean;
  onRefresh: () => void;
  /** Marks a background job as just-started so polling begins before the worker picks it up. */
  onJobStarted: () => void;
  onGoTo: (step: WizardStepName) => void;
  onError: (message: string | null) => void;
  /** Reports whether this step currently has unsaved edits, so leaving the step (nav or a "continue" button) can warn before discarding them. Optional - only steps with their own local draft state need to call it. */
  onDirtyChange?: (dirty: boolean) => void;
}

// --------------------------------------------------------------------------

export function TemplateStep({ overview, catalog, busy, onRefresh, onGoTo, onError }: StepProps) {
  const [saving, setSaving] = useState(false);

  const apply = async (patch: Parameters<typeof wizardApi.applyPresets>[1]) => {
    setSaving(true);
    onError(null);
    try {
      await wizardApi.applyPresets(overview.id, patch);
      onRefresh();
    } catch (err) {
      onError(describeApiError(err, "Không lưu được lựa chọn."));
    } finally {
      setSaving(false);
    }
  };

  const disabled = busy || saving;

  return (
    <section className="wz-card">
      <h2>Chọn kiểu nội dung</h2>
      <p className="wz-sub">
        Mẫu quyết định cách viết kịch bản. Chọn xong, ba mục còn lại sẽ tự điền theo mẫu - bạn có thể đổi lại bất cứ lúc
        nào.
      </p>

      <PresetPicker
        label="Mẫu nội dung"
        presets={catalog.templates}
        selectedId={overview.presets.templateId}
        disabled={disabled}
        onSelect={(templateId) => apply({ templateId })}
        extra={(template) => (
          <span className="wz-hint" style={{ display: "block", marginTop: 5 }}>
            {template.niche} · {template.defaultDurationSeconds}s
          </span>
        )}
      />

      <PresetPicker
        label="Phong cách hình ảnh"
        presets={catalog.styles}
        selectedId={overview.presets.stylePresetId}
        disabled={disabled}
        onSelect={(stylePresetId) => apply({ stylePresetId })}
      />

      <PresetPicker
        label="Giọng đọc"
        presets={catalog.voices}
        selectedId={overview.presets.voicePresetId}
        disabled={disabled}
        onSelect={(voicePresetId) => apply({ voicePresetId })}
      />

      <PresetPicker
        label="Kiểu phụ đề"
        presets={catalog.captions}
        selectedId={overview.presets.captionPresetId}
        disabled={disabled}
        onSelect={(captionPresetId) => apply({ captionPresetId })}
      />

      <div className="wz-actions">
        <button
          type="button"
          className="wz-btn wz-btn-primary"
          disabled={disabled || !overview.presets.templateId}
          onClick={() => onGoTo("Idea")}
        >
          Tiếp tục
        </button>
      </div>
    </section>
  );
}

// --------------------------------------------------------------------------

const EMPTY_IDEA: IdeaConfig = {};

/** The fields IdeaStep tracks locally, in the shape used to detect unsaved edits. */
interface IdeaDraftSnapshot {
  title: string;
  topic: string;
  niche: string;
  duration: number;
  idea: IdeaConfig;
}

function ideaSnapshotFrom(overview: ProjectOverview): IdeaDraftSnapshot {
  return {
    title: overview.title,
    topic: overview.topic ?? "",
    niche: overview.niche ?? "",
    duration: overview.targetDurationSeconds,
    idea: overview.ideaConfig ?? EMPTY_IDEA,
  };
}

export function IdeaStep({ overview, busy, onRefresh, onJobStarted, onGoTo, onError, onDirtyChange }: StepProps) {
  const [title, setTitle] = useState(overview.title);
  const [topic, setTopic] = useState(overview.topic ?? "");
  const [niche, setNiche] = useState(overview.niche ?? "");
  const [duration, setDuration] = useState(overview.targetDurationSeconds);
  const [idea, setIdea] = useState<IdeaConfig>(overview.ideaConfig ?? EMPTY_IDEA);
  const [submitting, setSubmitting] = useState<null | "save" | "generate">(null);

  // What's actually saved server-side right now - compared against the fields
  // above to know whether there are unsaved edits. Kept separate from
  // `overview` itself because `overview` only catches up asynchronously after
  // a save (see persist()), which would otherwise make a just-saved form look
  // dirty for one extra render.
  const [saved, setSaved] = useState<IdeaDraftSnapshot>(() => ideaSnapshotFrom(overview));

  useEffect(() => {
    const snapshot = ideaSnapshotFrom(overview);
    setTitle(snapshot.title);
    setTopic(snapshot.topic);
    setNiche(snapshot.niche);
    setDuration(snapshot.duration);
    setIdea(snapshot.idea);
    setSaved(snapshot);
  }, [overview.id]); // eslint-disable-line react-hooks/exhaustive-deps

  const dirty =
    title !== saved.title ||
    topic !== saved.topic ||
    niche !== saved.niche ||
    duration !== saved.duration ||
    JSON.stringify(idea) !== JSON.stringify(saved.idea);

  useEffect(() => {
    onDirtyChange?.(dirty);
  }, [dirty, onDirtyChange]);

  const patchIdea = (p: Partial<IdeaConfig>) => setIdea((cur) => ({ ...cur, ...p }));

  const persist = async () => {
    const snapshot: IdeaDraftSnapshot = {
      title: title.trim(),
      topic: topic.trim(),
      niche: niche.trim(),
      duration,
      idea,
    };
    await contentProjectsApi.update(overview.id, {
      title: snapshot.title,
      topic: snapshot.topic || undefined,
      niche: snapshot.niche || undefined,
      targetDurationSeconds: snapshot.duration,
      ideaConfig: snapshot.idea,
    });
    setSaved(snapshot);
    // Tell the parent immediately rather than waiting for the `dirty`-tracking
    // effect above to run on the next render - handleGenerate calls onGoTo
    // right after persist() resolves, in the same tick, and the parent's
    // guard must see "clean" by then or it will show a needless confirm.
    onDirtyChange?.(false);
  };

  const handleSave = async () => {
    setSubmitting("save");
    onError(null);
    try {
      await persist();
      onRefresh();
    } catch (err) {
      onError(describeApiError(err, "Không lưu được cấu hình."));
    } finally {
      setSubmitting(null);
    }
  };

  const handleGenerate = async () => {
    setSubmitting("generate");
    onError(null);
    try {
      await persist();
      await contentProjectsApi.generate(overview.id);
      onJobStarted();
      onGoTo("Script");
      onRefresh();
    } catch (err) {
      onError(describeApiError(err, "Không tạo được kịch bản."));
    } finally {
      setSubmitting(null);
    }
  };

  const disabled = busy || submitting !== null;

  // Drops an AI-suggested idea into the normal Step 2 fields. From here on it is
  // just a user-entered idea - the existing save / generate flow is unchanged.
  const useSuggestedIdea = (suggestion: ContentIdeaSuggestion) => {
    onError(null);
    setTitle(suggestion.title);
    setNiche(suggestion.niche);
    setTopic(
      suggestion.hook.trim()
        ? `${suggestion.concept.trim()}\n\nMở đầu gợi ý: ${suggestion.hook.trim()}`
        : suggestion.concept.trim(),
    );
  };

  const existingIdeaHints = [title, topic].map((s) => s.trim()).filter((s) => s.length > 0);

  return (
    <section className="wz-card">
      <h2>Ý tưởng video</h2>
      <p className="wz-sub">Càng cụ thể thì kịch bản càng sắc. Một câu mô tả rõ ràng tốt hơn một từ khoá chung chung.</p>

      <ContentIdeaSuggestions
        language={overview.language}
        durationSeconds={duration}
        existingIdeas={existingIdeaHints}
        disabled={disabled}
        onUseIdea={useSuggestedIdea}
      />

      <Field label="Tiêu đề">
        <input type="text" value={title} onChange={(e) => setTitle(e.target.value)} />
      </Field>

      <Field label="Nội dung muốn kể" hint="Ví dụ: “Vì sao mèo hay ngồi trong hộp giấy”.">
        <textarea rows={3} value={topic} onChange={(e) => setTopic(e.target.value)} />
      </Field>

      <div className="wz-row">
        <Field label="Chủ đề / ngách">
          <input type="text" value={niche} onChange={(e) => setNiche(e.target.value)} />
        </Field>
        <Field label="Độ dài video (giây)">
          <input
            type="number"
            min={1}
            max={180}
            value={duration}
            onChange={(e) => setDuration(Number(e.target.value))}
          />
        </Field>
      </div>

      <h3>Định hướng nội dung</h3>
      <div className="wz-row">
        <Field label="Cột nội dung (pillar)" hint="Chủ đề lặp lại của kênh.">
          <input
            type="text"
            value={idea.contentPillar ?? ""}
            onChange={(e) => patchIdea({ contentPillar: e.target.value || undefined })}
          />
        </Field>
        <Field label="Đối tượng khán giả">
          <input
            type="text"
            value={idea.targetAudience ?? ""}
            onChange={(e) => patchIdea({ targetAudience: e.target.value || undefined })}
          />
        </Field>
      </div>
      <div className="wz-row">
        <Field label="Kiểu chuyện">
          <select value={idea.storyType ?? ""} onChange={(e) => patchIdea({ storyType: e.target.value || undefined })}>
            <option value="">(tự chọn)</option>
            {STORY_TYPES.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Kiểu hook">
          <select value={idea.hookStyle ?? ""} onChange={(e) => patchIdea({ hookStyle: e.target.value || undefined })}>
            <option value="">(tự chọn)</option>
            {HOOK_STYLES.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Cảm xúc chủ đạo">
          <select value={idea.emotion ?? ""} onChange={(e) => patchIdea({ emotion: e.target.value || undefined })}>
            <option value="">(tự chọn)</option>
            {EMOTIONS.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </select>
        </Field>
      </div>

      <p className="wz-hint" style={{ margin: "0 0 4px" }}>
        🎙️ Giọng đọc, âm thanh và phụ đề chuyển sang <strong>Bước 6 – Dựng video</strong> (đây là thiết lập khi xuất bản,
        không phải khi lên ý tưởng).
      </p>

      <h3>Chiến lược tín dụng</h3>
      <Field label="Phân bổ credit cho video AI" hint={CREDIT_STRATEGIES.find((s) => s.value === (idea.creditStrategy ?? "Balanced"))?.hint}>
        <select
          value={idea.creditStrategy ?? "Balanced"}
          onChange={(e) => patchIdea({ creditStrategy: e.target.value as CreditStrategyName })}
        >
          {CREDIT_STRATEGIES.map((s) => (
            <option key={s.value} value={s.value}>
              {s.label}
            </option>
          ))}
        </select>
      </Field>

      <div className="wz-actions">
        <button
          type="button"
          className="wz-btn wz-btn-primary"
          disabled={disabled || !title.trim()}
          onClick={handleGenerate}
        >
          {submitting === "generate" ? "Đang gửi..." : overview.script ? "Viết lại kịch bản" : "Viết kịch bản"}
        </button>
        <CostNote kind="longText">Viết kịch bản bằng AI</CostNote>
        <button type="button" className="wz-btn" disabled={disabled} onClick={handleSave}>
          {submitting === "save" ? "Đang lưu..." : "Lưu cấu hình"}
        </button>
        {overview.script && (
          <button type="button" className="wz-btn" disabled={disabled} onClick={() => onGoTo("Script")}>
            Xem kịch bản hiện có
          </button>
        )}
      </div>
    </section>
  );
}

// --------------------------------------------------------------------------

const SCRIPT_FIELDS: { key: keyof UpsertScriptInput; label: string; rows: number }[] = [
  { key: "hook", label: "Câu mở đầu (hook)", rows: 2 },
  { key: "introduction", label: "Dẫn nhập", rows: 2 },
  { key: "body", label: "Nội dung chính", rows: 4 },
  { key: "escalation", label: "Cao trào", rows: 2 },
  { key: "payoff", label: "Chốt hạ", rows: 2 },
  { key: "callToAction", label: "Kêu gọi hành động", rows: 2 },
];

const EMPTY_SCRIPT: UpsertScriptInput = {
  hook: "",
  introduction: "",
  body: "",
  escalation: "",
  payoff: "",
  callToAction: "",
};

export function ScriptStep({ overview, busy, onRefresh, onJobStarted, onGoTo, onError, onDirtyChange }: StepProps) {
  const [draft, setDraft] = useState<UpsertScriptInput>(EMPTY_SCRIPT);
  const [saving, setSaving] = useState(false);
  const [scoring, setScoring] = useState(false);

  // What's actually saved server-side, for the same reason as IdeaStep's
  // `saved` snapshot: comparing against `overview.script` directly would make
  // the form look dirty for one extra render right after a save/score, since
  // `overview` only catches up once onRefresh's fetch resolves.
  const [saved, setSaved] = useState<UpsertScriptInput>(EMPTY_SCRIPT);

  useEffect(() => {
    if (!overview.script) {
      setDraft(EMPTY_SCRIPT);
      setSaved(EMPTY_SCRIPT);
      return;
    }
    const { hook, introduction, body, escalation, payoff, callToAction } = overview.script;
    const next = { hook, introduction, body, escalation, payoff, callToAction };
    setDraft(next);
    setSaved(next);
  }, [overview.script]);

  const dirty = JSON.stringify(draft) !== JSON.stringify(saved);

  useEffect(() => {
    onDirtyChange?.(dirty);
  }, [dirty, onDirtyChange]);

  const handleSave = async () => {
    setSaving(true);
    onError(null);
    try {
      await scriptApi.upsert(overview.id, draft);
      setSaved(draft);
      onDirtyChange?.(false);
      onRefresh();
    } catch (err) {
      onError(describeApiError(err, "Không lưu được kịch bản."));
    } finally {
      setSaving(false);
    }
  };

  const handleScore = async () => {
    setScoring(true);
    onError(null);
    try {
      await scriptApi.upsert(overview.id, draft);
      setSaved(draft);
      onDirtyChange?.(false);
      await pipelineApi.runQa(overview.id);
      onJobStarted();
      onRefresh();
    } catch (err) {
      onError(describeApiError(err, "Không chấm điểm được kịch bản."));
    } finally {
      setScoring(false);
    }
  };

  const disabled = busy || saving || scoring;

  if (!overview.script && !busy) {
    return (
      <section className="wz-card">
        <h2>Kịch bản</h2>
        <p className="wz-sub">Chưa có kịch bản. Quay lại bước ý tưởng để tạo.</p>
        <button type="button" className="wz-btn" onClick={() => onGoTo("Idea")}>
          Quay lại ý tưởng
        </button>
      </section>
    );
  }

  return (
    <section className="wz-card">
      <h2>Xem lại kịch bản</h2>
      <p className="wz-sub">
        Sửa thoải mái ở đây - đây là lúc rẻ nhất để thay đổi, vì chưa có clip nào được dựng.
      </p>

      {overview.qa && (
        <div className="wz-card" style={{ marginBottom: 18 }}>
          <h3>
            Đánh giá: {overview.qa.label} ({overview.qa.overall.toFixed(1)}/10)
          </h3>
          <p style={{ fontSize: 13.5 }}>{overview.qa.advice}</p>
        </div>
      )}

      {SCRIPT_FIELDS.map((field) => (
        <Field key={field.key} label={field.label}>
          <textarea
            rows={field.rows}
            value={draft[field.key]}
            onChange={(e) => setDraft((current) => ({ ...current, [field.key]: e.target.value }))}
          />
        </Field>
      ))}

      <div className="wz-actions">
        <button type="button" className="wz-btn" disabled={disabled} onClick={handleSave}>
          {saving ? "Đang lưu..." : "Lưu kịch bản"}
        </button>
        <button type="button" className="wz-btn" disabled={disabled} onClick={handleScore}>
          {scoring ? "Đang gửi..." : "Chấm điểm kịch bản"}
        </button>
        <CostNote kind="longText">Chấm điểm bằng AI</CostNote>
        <button type="button" className="wz-btn wz-btn-primary" disabled={disabled} onClick={() => onGoTo("References")}>
          Tiếp tục
        </button>
      </div>
    </section>
  );
}

// --------------------------------------------------------------------------

// Ordered cheapest-first (miễn phí → chi phí thấp → trả phí) so the free option
// is the default and paid AI video is an explicit opt-in, not the starting point.
const STRATEGY_OPTIONS: { value: ClipPlanStrategy; label: string; hint: string }[] = [
  { value: "AllImages", label: "Tất cả là ảnh tĩnh (rẻ nhất)", hint: "Rẻ nhất - ảnh AI + hiệu ứng zoom nhẹ, gần như $0." },
  { value: "CostOptimized", label: "Tối ưu chi phí (chi phí thấp)", hint: "Video AI chỉ ở hook + cao trào, còn lại ảnh tĩnh - rẻ hơn nhiều so với toàn video." },
  { value: "AllVideo", label: "Tất cả là video AI (trả phí)", hint: "Đẹp nhất, tốn nhất - chỉ dùng khi thực sự cần." },
];

export function GenerateStep({ overview, busy, onRefresh, onJobStarted, onGoTo, onError }: StepProps) {
  const [totalDuration, setTotalDuration] = useState(overview.targetDurationSeconds);
  const [clipDuration, setClipDuration] = useState(8);
  // Default to the free/near-free plan; paid AI video is an opt-in via the select.
  const [strategy, setStrategy] = useState<ClipPlanStrategy>("AllImages");
  const [autoHook, setAutoHook] = useState(false);
  const [planning, setPlanning] = useState(false);
  const [starting, setStarting] = useState(false);
  const [suggestingAll, setSuggestingAll] = useState(false);
  const pricing = usePricing();
  // Bumped once the bulk "suggest-all" background job's busy state clears, so
  // ScenePlanPanel refetches the flow plan (its own prompt/rationale fields
  // aren't in `overview.clips`, so the normal busy-poll refresh alone doesn't
  // surface the new prompts - see ScenePlanPanel's refreshToken prop).
  const [scenePlanRefreshToken, setScenePlanRefreshToken] = useState(0);
  const suggestAllInFlightRef = useRef(false);
  // Surfaced by ScenePlanPanel's own fetch (see onPlanLoaded) purely so the
  // sticky action bar can show the same scene counts / Copy All action
  // without a second request.
  const [flowPlan, setFlowPlan] = useState<FlowGenerationPlan | null>(null);
  const [copiedAll, setCopiedAll] = useState(false);

  // Once the bulk suggest-all job we started finishes (project goes back to
  // idle), trigger ScenePlanPanel's refetch exactly once for that run.
  useEffect(() => {
    if (!busy && suggestAllInFlightRef.current) {
      suggestAllInFlightRef.current = false;
      setScenePlanRefreshToken((t) => t + 1);
    }
  }, [busy]);

  // Auto-hook mode writes its own fixed script, so its estimate can't come
  // from the storyboard-based one in `overview`; fetch a mode-specific one.
  const [flowEstimate, setFlowEstimate] = useState<GenerationEstimate | null>(null);

  useEffect(() => {
    if (!autoHook) {
      setFlowEstimate(null);
      return;
    }
    let cancelled = false;
    wizardApi
      .getEstimate(overview.id, "googleflow")
      .then((e) => {
        if (!cancelled) setFlowEstimate(e);
      })
      .catch(() => {
        if (!cancelled) setFlowEstimate(null);
      });
    return () => {
      cancelled = true;
    };
  }, [autoHook, overview.id]);

  const clipCount = Math.max(1, Math.round(totalDuration / Math.max(1, clipDuration)));
  const hasPlan = overview.clips.length > 0;
  const estimate = autoHook ? flowEstimate ?? overview.estimate : overview.estimate;

  // The "Ảnh mẫu" step must be resolved first (unless auto-hook, which makes
  // its own references).
  const refResolved = (s: { status: string }) => s.status === "Approved" || s.status === "Skipped";
  const referencesResolved =
    refResolved(overview.references.character) && refResolved(overview.references.environment);

  const handlePlan = async () => {
    setPlanning(true);
    onError(null);
    try {
      await clipPlanApi.generate(overview.id, { clipCount, clipDurationSeconds: clipDuration, strategy });
      onRefresh();
    } catch (err) {
      onError(describeApiError(err, "Không chia được clip."));
    } finally {
      setPlanning(false);
    }
  };

  const handleStart = async () => {
    setStarting(true);
    onError(null);
    try {
      // autoHook -> Google Flow pipeline with its own script; otherwise the
      // standard pipeline over the user's clip plan.
      await pipelineApi.generateAssets(overview.id, autoHook ? "googleflow" : "standard", autoHook);
      onJobStarted();
      onGoTo("Preview");
      onRefresh();
    } catch (err) {
      onError(describeApiError(err, "Không bắt đầu dựng được."));
    } finally {
      setStarting(false);
    }
  };

  const disabled = busy || planning || starting || suggestingAll;
  const withinBudget = autoHook || overview.credits.withinBudget;
  // A scene left on "Animation / Motion" (or another type the app can't
  // auto-generate) is not skip-flagged would make the Veo run throw
  // mid-batch (SceneAssetGenerator rejects any type other than AiVideo/
  // AiImage) and abort the whole job, even after earlier paid clips in the
  // same run already succeeded. Block starting until the user either
  // changes the scene's type or checks "Đã có video / Bỏ qua Veo API".
  const hasUnsupportedScene =
    !autoHook && (flowPlan?.scenes.some((s) => s.generationType === "STATIC" && !s.skipGeneration) ?? false);
  const canStart =
    !disabled && withinBudget && (autoHook || (hasPlan && referencesResolved && !hasUnsupportedScene));

  const copyAllPrompts = async () => {
    if (!flowPlan?.copyAllText) return;
    try {
      await navigator.clipboard.writeText(flowPlan.copyAllText);
      setCopiedAll(true);
      window.setTimeout(() => setCopiedAll(false), 1500);
    } catch {
      onError("Trình duyệt chặn copy - hãy mở danh sách cảnh bên trên và copy thủ công.");
    }
  };

  const videoSceneCount = flowPlan?.scenes.filter((s) => s.generationType === "AI_VIDEO" && !s.skipGeneration).length ?? 0;
  const imageSceneCount = flowPlan?.scenes.filter((s) => s.generationType !== "AI_VIDEO" && !s.skipGeneration).length ?? 0;
  const unpromptedSceneCount = flowPlan?.scenes.filter((s) => s.isUnprompted).length ?? 0;

  // Bulk-suggests a prompt for every scene that doesn't have one yet - one
  // billable (text-only) AI call per unprompted scene, run sequentially as a
  // background job on the server (same ReserveJobAsync "busy" lock as
  // handleStart/handlePlan's siblings elsewhere in the wizard), so this only
  // triggers the job and lets the existing progress poll/busy-refresh handle
  // the rest.
  const handleSuggestAllPrompts = async (includePrompted = false) => {
    if (includePrompted) {
      const count = flowPlan?.scenes.length ?? overview.clips.length;
      const total = formatUsdEstimate(pricing.textCallUsd * count);
      const ok = window.confirm(
        `Gợi ý lại prompt cho TẤT CẢ ${count} cảnh (kể cả cảnh đã có prompt)?\n\n` +
          `• Chi phí ước tính ≈ ${formatUsdEstimate(pricing.textCallUsd)} × ${count} cảnh ≈ ${total}.\n` +
          "• Prompt bạn đã tự sửa tay sẽ bị ghi đè.\n" +
          "• Mỗi cảnh sẽ có cỡ cảnh, góc máy đổi theo cảnh trước, và AI tự xác định cảnh nào có nhân vật.",
      );
      if (!ok) return;
    }
    setSuggestingAll(true);
    onError(null);
    try {
      await storyboardApi.suggestAllPrompts(overview.id, { includePrompted });
      suggestAllInFlightRef.current = true;
      onJobStarted();
      onRefresh();
    } catch (err) {
      onError(describeApiError(err, "Không tạo được gợi ý cho tất cả cảnh."));
    } finally {
      setSuggestingAll(false);
    }
  };

  return (
    <>
      {!autoHook && (
        <section className="wz-card">
          <h2>Chia video thành các clip</h2>
          <p className="wz-sub">
            Mỗi clip được dựng riêng rồi ghép lại. Clip 8 giây là mức mô hình video xử lý tốt nhất - chỉ đổi nếu bạn
            biết rõ mình cần gì.
          </p>

          <div className="wz-row">
            <Field label="Tổng độ dài (giây)">
              <input
                type="number"
                min={clipDuration}
                max={180}
                value={totalDuration}
                onChange={(e) => setTotalDuration(Number(e.target.value))}
              />
            </Field>
            <Field label="Độ dài mỗi clip (giây)">
              <input
                type="number"
                min={1}
                max={60}
                value={clipDuration}
                onChange={(e) => setClipDuration(Number(e.target.value))}
              />
            </Field>
            <Field label="Số clip">
              <input type="number" value={clipCount} readOnly />
            </Field>
          </div>

          <Field label="Cách phân bổ chi phí" hint={STRATEGY_OPTIONS.find((s) => s.value === strategy)?.hint}>
            <select value={strategy} onChange={(e) => setStrategy(e.target.value as ClipPlanStrategy)}>
              {STRATEGY_OPTIONS.map((s) => (
                <option key={s.value} value={s.value}>
                  {s.label}
                </option>
              ))}
            </select>
          </Field>

          <div className="wz-actions">
            <button type="button" className="wz-btn" disabled={disabled} onClick={handlePlan}>
              {planning ? "Đang chia..." : hasPlan ? "Chia lại" : "Chia clip"}
            </button>
          </div>
        </section>
      )}

      {!autoHook && hasPlan && (
        <>
          {/* Primary content - one unified scene list: media-type selector,
              always-visible narration/prompt copy actions, and an "Advanced"
              disclosure per card for the Veo-specific controls (model tier via
              the selector, camera, skip-generation, prompt edit, upload). */}
          <ScenePlanPanel
            contentProjectId={overview.id}
            clips={overview.clips}
            disabled={disabled}
            onChanged={onRefresh}
            onPlanLoaded={setFlowPlan}
            refreshToken={scenePlanRefreshToken}
          />

          <div className="wz-sticky-bar">
            <span className="wz-sticky-bar-summary">
              {flowPlan
                ? `${videoSceneCount} Video AI · ${imageSceneCount} Ảnh · ~${formatDuration(estimate.totalSeconds)}`
                : `${overview.clips.length} cảnh · ~${formatDuration(estimate.totalSeconds)}`}
            </span>
            {unpromptedSceneCount > 0 && (
              <button
                type="button"
                className="wz-btn wz-btn-sm"
                disabled={disabled}
                onClick={() => handleSuggestAllPrompts()}
                title="Tạo gợi ý prompt AI cho mọi cảnh còn thiếu prompt - cảnh đã có prompt được giữ nguyên."
              >
                {suggestingAll ? "Đang gửi..." : `✨ Tạo gợi ý cho ${unpromptedSceneCount} cảnh còn thiếu`}
              </button>
            )}
            {unpromptedSceneCount > 0 && (
              <CostNote kind="text" units={unpromptedSceneCount} unitLabel="cảnh">
                Gợi ý prompt bằng AI
              </CostNote>
            )}
            {flowPlan && flowPlan.scenes.length > 0 && unpromptedSceneCount === 0 && (
              <>
                <button
                  type="button"
                  className="wz-btn wz-btn-sm"
                  disabled={disabled}
                  onClick={() => handleSuggestAllPrompts(true)}
                  title="Chạy lại gợi ý prompt AI cho mọi cảnh (ghi đè prompt cũ) - dùng cho dự án tạo trước khi có cỡ cảnh / nhận diện nhân vật."
                >
                  {suggestingAll ? "Đang gửi..." : `🔁 Gợi ý lại tất cả ${flowPlan.scenes.length} cảnh`}
                </button>
                <CostNote kind="text" units={flowPlan.scenes.length} unitLabel="cảnh" />
              </>
            )}
            {flowPlan?.copyAllText && (
              <button
                type="button"
                className="wz-btn wz-btn-sm"
                onClick={copyAllPrompts}
                title="Các cảnh chưa có prompt AI sẽ không được copy - văn bản đã copy có ghi chú số cảnh bị bỏ qua ở cuối."
              >
                {copiedAll ? "Đã copy ✓" : "📋 Copy tất cả Prompt"}
              </button>
            )}
            <button type="button" className="wz-btn wz-btn-primary wz-btn-sm" onClick={() => onGoTo("Preview")}>
              Tiếp tục → Bước 6
            </button>
          </div>
        </>
      )}

      <details className="wz-advanced">
        <summary>⚡ Dựng tự động bằng Veo API (trả phí, nâng cao)</summary>

        <p className="wz-hint" style={{ marginTop: 0 }}>
          💡 Miễn phí: dùng Google Flow ở trên thay vì phần này. Phần dưới đây gọi Veo API của Google - tính phí thật
          theo giây video.
        </p>

        {overview.googleFlowAvailable && (
          <label style={{ display: "block", fontSize: 14, margin: "0 0 12px" }}>
            <input type="checkbox" checked={autoHook} disabled={disabled} onChange={(e) => setAutoHook(e.target.checked)} />{" "}
            ⚡ Tạo nhanh: bỏ qua kịch bản của tôi, để AI tự viết hook 20 giây (3 cảnh)
          </label>
        )}
        {autoHook && (
          <p className="wz-sub">Sẽ thay kịch bản và clip plan hiện tại bằng một hook ~20 giây do AI viết.</p>
        )}

        {!autoHook && <CreditPanel credits={overview.credits} />}

        <CostEstimate estimate={estimate} title="Chi phí và thời gian ước tính" />

        <Blockers blockers={autoHook ? [] : overview.blockers} />

        {!withinBudget && (
          <p className="wz-error" style={{ marginTop: 8 }}>
            Không thể bắt đầu: kịch bản vượt quá tín dụng còn lại hôm nay. Giảm số clip hoặc đổi chiến lược ở bước ý tưởng.
          </p>
        )}

        {hasUnsupportedScene && (
          <p className="wz-error" style={{ marginTop: 8 }}>
            ⚠️ Có cảnh đang để loại "🪄 Animation / Motion" - ứng dụng chưa tự tạo được loại này. Đổi loại media của
            cảnh đó ở danh sách cảnh bên trên, hoặc tick "Đã có video / Bỏ qua Veo API" nếu bạn đã tự tạo và tải lên
            clip, trước khi Dựng bằng Veo API.
          </p>
        )}

        {!autoHook && !referencesResolved && (
          <p className="wz-hint" style={{ marginTop: 8 }}>
            Cần hoàn tất bước "Ảnh mẫu" trước.{" "}
            <button
              type="button"
              className="wz-btn wz-btn-sm"
              disabled={disabled}
              onClick={() => onGoTo("References")}
            >
              Đến bước Ảnh mẫu
            </button>
          </p>
        )}

        <div className="wz-actions">
          <button type="button" className="wz-btn wz-btn-primary" disabled={!canStart} onClick={handleStart}>
            {starting ? "Đang gửi..." : "Dựng bằng Veo API"}
          </button>
          <CostNote kind="video" usd={estimate.totalCostUsd}>
            {estimate.totalCostUsd > 0
              ? "tổng ước tính cho các cảnh cần tạo (xem bảng chi tiết phía trên)"
              : "Bước này không phát sinh phí video AI (toàn ảnh tĩnh / clip tải lên)"}
          </CostNote>
          {overview.clips.some((c) => c.state === "Ready") && (
            <button type="button" className="wz-btn" disabled={disabled} onClick={() => onGoTo("Preview")}>
              Xem các clip đã có
            </button>
          )}
        </div>

        <p className="wz-hint" style={{ marginTop: 10 }}>
          Quá trình này chạy nền và có thể mất khoảng {formatDuration(estimate.totalSeconds)} (đây là thời gian chờ,
          không phải độ dài video). Bạn có thể đóng tab.
        </p>

        <AttemptsPanel attempts={overview.attempts} />
      </details>
    </>
  );
}

// --------------------------------------------------------------------------

export function PreviewStep({ overview, catalog, busy, onRefresh, onJobStarted, onGoTo, onError }: StepProps) {
  const [rendering, setRendering] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [retryingFailed, setRetryingFailed] = useState(false);

  const allReady = useMemo(
    () => overview.clips.length > 0 && overview.clips.every((clip) => clip.state === "Ready"),
    [overview.clips],
  );

  const readyClipCount = useMemo(() => overview.clips.filter((c) => c.state === "Ready").length, [overview.clips]);
  const workingClipCount = useMemo(() => overview.clips.filter((c) => c.state === "Working").length, [overview.clips]);
  const failedClips = useMemo(() => overview.clips.filter((c) => c.state === "Failed"), [overview.clips]);

  const handleRender = async () => {
    setRendering(true);
    onError(null);
    try {
      await pipelineApi.render(overview.id);
      onJobStarted();
      onRefresh();
    } catch (err) {
      onError(describeApiError(err, "Không ghép được video."));
    } finally {
      setRendering(false);
    }
  };

  const handleMusic = async (file: File | undefined) => {
    if (!file) return;
    setUploading(true);
    onError(null);
    try {
      await wizardApi.uploadMusic(overview.id, file);
      onRefresh();
    } catch (err) {
      onError(describeApiError(err, "Không tải được file nhạc."));
    } finally {
      setUploading(false);
    }
  };

  // One click to retry every failed clip instead of opening each ClipCard in
  // turn - reuses the exact same regenerate call each clip's own "Tạo lại
  // clip này" makes (same defaults: no forced voice redo, no narration
  // change), just fired for all of them at once.
  const handleRetryFailedClips = async () => {
    if (failedClips.length === 0) return;
    setRetryingFailed(true);
    onError(null);
    try {
      await Promise.all(
        failedClips.map((clip) => wizardApi.regenerateClip(overview.id, clip.id, { regenerateVoice: false })),
      );
      onJobStarted();
      onRefresh();
    } catch (err) {
      onError(describeApiError(err, "Không thử lại được một số clip lỗi."));
    } finally {
      setRetryingFailed(false);
    }
  };

  const applyCaptionPreset = async (captionPresetId: string) => {
    await wizardApi.applyPresets(overview.id, { captionPresetId });
    onRefresh();
  };

  const disabled = busy || rendering || uploading;

  return (
    <>
      <FlowImportPanel contentProjectId={overview.id} disabled={disabled} onChanged={onRefresh} />

      <section className="wz-card">
        <CompositionStatusPanel composition={overview.composition} />
      </section>

      <section className="wz-card">
        <h2>Xem trước từng clip</h2>
        <p className="wz-sub">
          Không ưng clip nào thì tạo lại đúng clip đó - các clip khác giữ nguyên, không mất thêm thời gian và chi phí.
        </p>

        {overview.clips.length > 0 && failedClips.length > 0 && (
          <div className="wz-error" style={{ marginBottom: 14 }}>
            <strong>
              {readyClipCount}/{overview.clips.length} clip xong · {failedClips.length} lỗi
              {workingClipCount > 0 ? ` · ${workingClipCount} đang dựng` : ""}
            </strong>
            <p className="wz-hint" style={{ marginTop: 6 }}>
              Các clip đã xong được giữ nguyên - chỉ {failedClips.length} clip lỗi sẽ được tạo lại.
            </p>
            <div className="wz-actions" style={{ marginTop: 10 }}>
              <button
                type="button"
                className="wz-btn wz-btn-primary wz-btn-sm"
                disabled={disabled || retryingFailed}
                onClick={handleRetryFailedClips}
              >
                {retryingFailed ? "Đang gửi..." : `Thử lại ${failedClips.length} clip lỗi`}
              </button>
            </div>
          </div>
        )}

        <div className="wz-clips">
          {overview.clips.map((clip) => (
            <ClipCard
              key={clip.id}
              contentProjectId={overview.id}
              clip={clip}
              disabled={disabled}
              audioMode={overview.composition.audioMode}
              onRegenerated={() => {
                onJobStarted();
                onRefresh();
              }}
            />
          ))}
        </div>
      </section>

      <AudioModeSelector
        contentProjectId={overview.id}
        value={overview.composition.audioMode}
        disabled={disabled}
        onChanged={onRefresh}
      />

      {overview.composition.audioMode !== "Muted" && (
        <VoiceSettings overview={overview} catalog={catalog} disabled={disabled} onChanged={onRefresh} />
      )}

      <CaptionEditor
        contentProjectId={overview.id}
        presets={catalog.captions}
        selectedPresetId={overview.presets.captionPresetId}
        settings={overview.captions}
        previewUrl={overview.captionPreviewUrl}
        disabled={disabled}
        onApplyPreset={applyCaptionPreset}
        onSaved={onRefresh}
      />

      <section className="wz-card">
        <h2>Nhạc nền</h2>
        <p className="wz-sub">
          Không bắt buộc. File nhạc sẽ được trộn nhỏ phía dưới giọng đọc khi ghép video.
          {overview.hasBackgroundMusic && " Đã có một bản nhạc - tải file mới sẽ thay thế nó."}
        </p>
        <input
          type="file"
          accept="audio/*"
          disabled={disabled}
          onChange={(e) => handleMusic(e.target.files?.[0])}
        />
        {uploading && <p className="wz-hint">Đang tải lên...</p>}
      </section>

      <section className="wz-card">
        <h2>Ghép thành video hoàn chỉnh</h2>
        <Blockers blockers={overview.blockers} />
        <p className="wz-hint" style={{ marginTop: 8 }}>
          Ghép lại không tạo lại clip nào, nên đổi phụ đề hoặc nhạc rồi ghép lại là miễn phí.
        </p>
        <div className="wz-actions">
          <button
            type="button"
            className="wz-btn wz-btn-primary"
            disabled={disabled || !allReady}
            title={allReady ? undefined : "Còn clip chưa dựng xong"}
            onClick={handleRender}
          >
            {rendering ? "Đang gửi..." : overview.finalVideoUrl ? "Ghép lại video" : "Ghép video"}
          </button>
          {overview.finalVideoUrl && (
            <button type="button" className="wz-btn" disabled={disabled} onClick={() => onGoTo("Export")}>
              Đến bước xuất bản
            </button>
          )}
        </div>
      </section>
    </>
  );
}

// --------------------------------------------------------------------------

export function ExportStep({ overview, busy, onGoTo }: StepProps) {
  const v = overview.lastValidation;

  if (!overview.finalVideoUrl) {
    return (
      <section className="wz-card">
        <h2>Xuất bản</h2>
        <p className="wz-sub">Chưa có video hoàn chỉnh. Quay lại bước xem trước để ghép video.</p>
        {v.hasRun && !v.ok && (
          <>
            <p className="wz-error">Lần ghép gần nhất không đạt kiểm tra: {v.summary}</p>
            <ValidationList title="Lỗi kiểm tra" items={v.errors} error />
          </>
        )}
        <button type="button" className="wz-btn" disabled={busy} onClick={() => onGoTo("Preview")}>
          Quay lại xem trước
        </button>
      </section>
    );
  }

  const fileUrl = apiUrl(overview.finalVideoUrl);
  const defaultCaption = overview.script?.hook?.trim() || overview.title;
  const defaultHashtags = [overview.niche, overview.topic]
    .filter((s): s is string => !!s && s.trim().length > 0)
    .flatMap((s) => s.split(/[\s,]+/))
    .filter(Boolean)
    .slice(0, 4)
    .map((w) => `#${w.replace(/[^\p{L}\p{N}]/gu, "")}`)
    .filter((t) => t.length > 1)
    .join(" ");

  return (
    <>
      <section className="wz-card">
        <h2>Video đã xong</h2>

        <div className="wz-validate">
          <StatusBadge tone={v.ok ? "success" : "danger"}>
            {v.ok ? "Đạt kiểm tra cuối" : "Không đạt kiểm tra"}
          </StatusBadge>
          {v.durationSeconds > 0 && <span className="wz-hint">{v.durationSeconds.toFixed(1)}s</span>}
        </div>
        <ValidationList title="Lỗi kiểm tra" items={v.errors} error />
        <ValidationList title="Cảnh báo" items={v.warnings} />

        <video
          controls
          src={fileUrl}
          style={{ width: "100%", maxWidth: 320, aspectRatio: "9 / 16", borderRadius: 10, background: "#000", marginTop: 12 }}
        />

        <div className="wz-actions">
          <a className="wz-btn" href={fileUrl} download={`${overview.title}.mp4`}>
            Tải video về
          </a>
          <button type="button" className="wz-btn" disabled={busy} onClick={() => onGoTo("Preview")}>
            Chỉnh sửa thêm
          </button>
        </div>
      </section>

      <PublishPanel
        contentProjectId={overview.id}
        defaultTitle={overview.title}
        defaultCaption={defaultCaption}
        defaultHashtags={defaultHashtags}
        finalVideoUrl={overview.finalVideoUrl}
      />
    </>
  );
}
