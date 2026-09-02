import { useEffect, useMemo, useState } from "react";
import {
  apiUrl,
  clipPlanApi,
  contentProjectsApi,
  pipelineApi,
  scriptApi,
  wizardApi,
  type ClipPlanStrategy,
  type CreditStrategyName,
  type GenerationEstimate,
  type IdeaConfig,
  type PresetCatalog,
  type ProjectOverview,
  type UpsertScriptInput,
  type VoiceGenderName,
  type WizardStepName,
} from "../api/client";
import { CaptionEditor } from "./CaptionEditor";
import { ClipCard } from "./ClipCard";
import { ClipPlanEditor } from "./ClipPlanEditor";
import { FlowImportPanel } from "./FlowImportPanel";
import { FlowPlanPanel } from "./FlowPlanPanel";
import { PublishPanel } from "./PublishPanel";
import {
  AttemptsPanel,
  Blockers,
  CompositionStatusPanel,
  CostEstimate,
  CreditPanel,
  Field,
  PresetPicker,
  ValidationList,
  formatDuration,
} from "./components";

const STORY_TYPES = ["Kể chuyện", "Case study", "Phản biện / bóc phốt", "Hướng dẫn", "Danh sách", "So sánh", "Giải thích"];
const HOOK_STYLES = ["Câu hỏi sốc", "Con số gây sốc", "Tuyên bố táo bạo", "Nghịch lý", "Bí ẩn mở màn"];
const EMOTIONS = ["Tò mò", "Kinh ngạc", "Căng thẳng", "Ấm áp", "Hài hước", "Truyền cảm hứng"];
const VOICE_STYLES = ["Tự nhiên", "Điềm tĩnh", "Năng lượng cao", "Kịch tính", "Thì thầm", "Ấm áp"];
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
      onError(err instanceof Error ? err.message : "Không lưu được lựa chọn.");
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

export function IdeaStep({ overview, catalog, busy, onRefresh, onJobStarted, onGoTo, onError }: StepProps) {
  const [title, setTitle] = useState(overview.title);
  const [topic, setTopic] = useState(overview.topic ?? "");
  const [niche, setNiche] = useState(overview.niche ?? "");
  const [duration, setDuration] = useState(overview.targetDurationSeconds);
  const [idea, setIdea] = useState<IdeaConfig>(overview.ideaConfig ?? EMPTY_IDEA);
  const [voiceId, setVoiceId] = useState<string | null>(overview.presets.voicePresetId ?? null);
  const [submitting, setSubmitting] = useState<null | "save" | "generate">(null);

  useEffect(() => {
    setTitle(overview.title);
    setTopic(overview.topic ?? "");
    setNiche(overview.niche ?? "");
    setDuration(overview.targetDurationSeconds);
    setIdea(overview.ideaConfig ?? EMPTY_IDEA);
    setVoiceId(overview.presets.voicePresetId ?? null);
  }, [overview.id]); // eslint-disable-line react-hooks/exhaustive-deps

  const gender: VoiceGenderName = idea.voiceGender ?? "Unspecified";
  const voicesForGender = catalog.voices.filter(
    (v) => gender === "Unspecified" || v.gender === gender || v.gender === "Unspecified",
  );

  const patchIdea = (p: Partial<IdeaConfig>) => setIdea((cur) => ({ ...cur, ...p }));

  const persist = async () => {
    await contentProjectsApi.update(overview.id, {
      title: title.trim(),
      topic: topic.trim() || undefined,
      niche: niche.trim() || undefined,
      targetDurationSeconds: duration,
      ideaConfig: idea,
    });
    if (voiceId && voiceId !== overview.presets.voicePresetId) {
      await wizardApi.applyPresets(overview.id, { voicePresetId: voiceId });
    }
  };

  const handleSave = async () => {
    setSubmitting("save");
    onError(null);
    try {
      await persist();
      onRefresh();
    } catch (err) {
      onError(err instanceof Error ? err.message : "Không lưu được cấu hình.");
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
      onError(err instanceof Error ? err.message : "Không tạo được kịch bản.");
    } finally {
      setSubmitting(null);
    }
  };

  const disabled = busy || submitting !== null;

  return (
    <section className="wz-card">
      <h2>Ý tưởng video</h2>
      <p className="wz-sub">Càng cụ thể thì kịch bản càng sắc. Một câu mô tả rõ ràng tốt hơn một từ khoá chung chung.</p>

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

      <h3>Giọng đọc</h3>
      <Field label="Giới tính giọng">
        <div className="wz-radios">
          {(["Unspecified", "Male", "Female"] as VoiceGenderName[]).map((g) => (
            <label key={g}>
              <input
                type="radio"
                name="voiceGender"
                checked={gender === g}
                onChange={() => patchIdea({ voiceGender: g })}
              />{" "}
              {g === "Unspecified" ? "Không chọn" : g === "Male" ? "Nam" : "Nữ"}
            </label>
          ))}
        </div>
      </Field>
      <div className="wz-row">
        <Field label="Giọng cụ thể">
          <select value={voiceId ?? ""} onChange={(e) => setVoiceId(e.target.value || null)}>
            <option value="">(theo mẫu)</option>
            {voicesForGender.map((v) => (
              <option key={v.id} value={v.id}>
                {v.name}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Phong cách đọc">
          <select value={idea.voiceStyle ?? ""} onChange={(e) => patchIdea({ voiceStyle: e.target.value || undefined })}>
            <option value="">(theo giọng)</option>
            {VOICE_STYLES.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </select>
        </Field>
      </div>
      <div className="wz-row">
        <Field label={`Tốc độ nói (${(idea.speakingRate ?? 1).toFixed(2)}×)`} hint="Nếu nhà cung cấp hỗ trợ.">
          <input
            type="range"
            min={0.5}
            max={1.5}
            step={0.05}
            value={idea.speakingRate ?? 1}
            onChange={(e) => patchIdea({ speakingRate: Number(e.target.value) })}
          />
        </Field>
        <Field label="Ngôn ngữ / giọng vùng" hint="BCP-47, ví dụ vi-VN, en-US. Nếu hỗ trợ.">
          <input
            type="text"
            value={idea.narrationLanguage ?? ""}
            onChange={(e) => patchIdea({ narrationLanguage: e.target.value || undefined })}
          />
        </Field>
      </div>

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

export function ScriptStep({ overview, busy, onRefresh, onJobStarted, onGoTo, onError }: StepProps) {
  const [draft, setDraft] = useState<UpsertScriptInput>(EMPTY_SCRIPT);
  const [saving, setSaving] = useState(false);
  const [scoring, setScoring] = useState(false);

  useEffect(() => {
    if (!overview.script) {
      setDraft(EMPTY_SCRIPT);
      return;
    }
    const { hook, introduction, body, escalation, payoff, callToAction } = overview.script;
    setDraft({ hook, introduction, body, escalation, payoff, callToAction });
  }, [overview.script]);

  const handleSave = async () => {
    setSaving(true);
    onError(null);
    try {
      await scriptApi.upsert(overview.id, draft);
      onRefresh();
    } catch (err) {
      onError(err instanceof Error ? err.message : "Không lưu được kịch bản.");
    } finally {
      setSaving(false);
    }
  };

  const handleScore = async () => {
    setScoring(true);
    onError(null);
    try {
      await scriptApi.upsert(overview.id, draft);
      await pipelineApi.runQa(overview.id);
      onJobStarted();
      onRefresh();
    } catch (err) {
      onError(err instanceof Error ? err.message : "Không chấm điểm được kịch bản.");
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
        <button type="button" className="wz-btn wz-btn-primary" disabled={disabled} onClick={() => onGoTo("References")}>
          Tiếp tục
        </button>
      </div>
    </section>
  );
}

// --------------------------------------------------------------------------

const STRATEGY_OPTIONS: { value: ClipPlanStrategy; label: string; hint: string }[] = [
  { value: "CostOptimized", label: "Tối ưu chi phí", hint: "Video AI chỉ ở hook + cao trào, còn lại ảnh tĩnh - rẻ hơn nhiều so với toàn video." },
  { value: "AllVideo", label: "Tất cả là video AI", hint: "Đẹp nhất, tốn nhất." },
  { value: "AllImages", label: "Tất cả là ảnh tĩnh", hint: "Rẻ nhất - ảnh AI + hiệu ứng zoom nhẹ." },
];

export function GenerateStep({ overview, busy, onRefresh, onJobStarted, onGoTo, onError }: StepProps) {
  const [totalDuration, setTotalDuration] = useState(overview.targetDurationSeconds);
  const [clipDuration, setClipDuration] = useState(8);
  const [strategy, setStrategy] = useState<ClipPlanStrategy>("CostOptimized");
  const [autoHook, setAutoHook] = useState(false);
  const [planning, setPlanning] = useState(false);
  const [starting, setStarting] = useState(false);

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
      onError(err instanceof Error ? err.message : "Không chia được clip.");
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
      onError(err instanceof Error ? err.message : "Không bắt đầu dựng được.");
    } finally {
      setStarting(false);
    }
  };

  const disabled = busy || planning || starting;
  const withinBudget = autoHook || overview.credits.withinBudget;
  const canStart = !disabled && withinBudget && (autoHook || (hasPlan && referencesResolved));

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

          {hasPlan && (
            <div style={{ marginTop: 16 }}>
              <ClipPlanEditor
                contentProjectId={overview.id}
                clips={overview.clips}
                disabled={disabled}
                onChanged={onRefresh}
              />
            </div>
          )}
        </section>
      )}

      {!autoHook && hasPlan && <FlowPlanPanel contentProjectId={overview.id} />}

      <section className="wz-card">
        <h2>Bắt đầu dựng</h2>

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
            {starting ? "Đang gửi..." : "Bắt đầu dựng video"}
          </button>
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
      </section>
    </>
  );
}

// --------------------------------------------------------------------------

export function PreviewStep({ overview, catalog, busy, onRefresh, onJobStarted, onGoTo, onError }: StepProps) {
  const [rendering, setRendering] = useState(false);
  const [uploading, setUploading] = useState(false);

  const allReady = useMemo(
    () => overview.clips.length > 0 && overview.clips.every((clip) => clip.state === "Ready"),
    [overview.clips],
  );

  const handleRender = async () => {
    setRendering(true);
    onError(null);
    try {
      await pipelineApi.render(overview.id);
      onJobStarted();
      onRefresh();
    } catch (err) {
      onError(err instanceof Error ? err.message : "Không ghép được video.");
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
      onError(err instanceof Error ? err.message : "Không tải được file nhạc.");
    } finally {
      setUploading(false);
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

        <div className="wz-clips">
          {overview.clips.map((clip) => (
            <ClipCard
              key={clip.id}
              contentProjectId={overview.id}
              clip={clip}
              disabled={disabled}
              onRegenerated={() => {
                onJobStarted();
                onRefresh();
              }}
            />
          ))}
        </div>
      </section>

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
          <span className={`wz-badge ${v.ok ? "wz-badge-ready" : "wz-badge-failed"}`}>
            {v.ok ? "Đạt kiểm tra cuối" : "Không đạt kiểm tra"}
          </span>
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
          <a className="wz-btn wz-btn-primary" href={fileUrl} download={`${overview.title}.mp4`}>
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
      />
    </>
  );
}
