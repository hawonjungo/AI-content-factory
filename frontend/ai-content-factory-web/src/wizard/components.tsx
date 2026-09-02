import { useEffect, useState, type ReactNode } from "react";
import type {
  CompositionStatus,
  CreditSummary,
  GenerationAttempt,
  GenerationAttemptState,
  GenerationEstimate,
  NamedPreset,
  ValidationSummary,
  WizardProgress,
  WizardStepName,
} from "../api/client";

export const STEP_ORDER: WizardStepName[] = [
  "Template",
  "Idea",
  "Script",
  "References",
  "Generate",
  "Preview",
  "Export",
];

export const STEP_LABELS: Record<WizardStepName, string> = {
  Template: "1. Mẫu",
  Idea: "2. Ý tưởng",
  Script: "3. Kịch bản",
  References: "4. Ảnh mẫu",
  Generate: "5. Dựng video",
  Preview: "6. Xem trước",
  Export: "7. Xuất bản",
};

export function StepNav({
  active,
  reachable,
  onSelect,
}: {
  active: WizardStepName;
  reachable: WizardStepName[];
  onSelect: (step: WizardStepName) => void;
}) {
  return (
    <ol className="wz-steps">
      {STEP_ORDER.map((step) => {
        const enabled = reachable.includes(step);
        return (
          <li key={step} style={{ display: "contents" }}>
            <button
              type="button"
              className="wz-step"
              aria-current={step === active ? "step" : undefined}
              disabled={!enabled}
              onClick={() => onSelect(step)}
              title={enabled ? undefined : "Hoàn thành bước trước đã"}
            >
              {STEP_LABELS[step]}
            </button>
          </li>
        );
      })}
    </ol>
  );
}

export function ProgressPanel({
  progress,
  estimatedTotalSeconds,
}: {
  progress: WizardProgress;
  /** Pre-generation wait estimate (GenerationEstimate.totalSeconds) - only used before enough progress exists for a rate-based ETA. */
  estimatedTotalSeconds?: number;
}) {
  const [elapsedSeconds, setElapsedSeconds] = useState(0);

  // Ticks once a second while busy; resets the moment a run starts.
  useEffect(() => {
    if (!progress.busy) return;
    setElapsedSeconds(0);
    const id = window.setInterval(() => setElapsedSeconds((s) => s + 1), 1000);
    return () => window.clearInterval(id);
  }, [progress.busy, progress.stage]);

  if (!progress.busy) return null;

  const known = progress.totalUnits > 0;
  // Once real progress exists, project the remaining time from the observed
  // rate - far more accurate than the upfront estimate for a run in flight.
  const etaSeconds =
    known && progress.percent > 0
      ? Math.max(0, Math.round((elapsedSeconds / progress.percent) * (100 - progress.percent)))
      : null;

  return (
    <div className="wz-progress" role="status" aria-live="polite">
      <strong>{progress.label || "Đang xử lý"}</strong>
      {known && (
        <>
          {" "}
          <span>
            ({progress.completedUnits}/{progress.totalUnits})
          </span>
          <div className="wz-bar">
            <div style={{ width: `${progress.percent}%` }} />
          </div>
        </>
      )}
      <p className="wz-hint" style={{ marginTop: 8 }}>
        ⏱️ Đã chạy {formatDuration(elapsedSeconds)}
        {etaSeconds !== null
          ? ` - còn khoảng ~${formatDuration(etaSeconds)} nữa`
          : estimatedTotalSeconds
            ? ` - dự kiến tổng cộng khoảng ${formatDuration(estimatedTotalSeconds)}`
            : ""}
        . Việc này chạy trên máy chủ - bạn có thể đóng tab, tiến độ vẫn được giữ.
      </p>
    </div>
  );
}

export function formatDuration(totalSeconds: number): string {
  if (totalSeconds <= 0) return "0 giây";
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = Math.round(totalSeconds % 60);
  if (minutes === 0) return `${seconds} giây`;
  if (seconds === 0) return `${minutes} phút`;
  return `${minutes} phút ${seconds} giây`;
}

export function formatUsd(value: number): string {
  return value < 0.01 && value > 0 ? "< $0.01" : `$${value.toFixed(2)}`;
}

/**
 * The cost/time panel shown before anything is spent. The two headline numbers
 * are deliberately separated: how long the finished VIDEO is, versus how long
 * you WAIT for it to build (clips generate one after another). Conflating them
 * is the most common point of confusion.
 *
 * Everything is labelled an estimate on purpose - the numbers come from
 * configured assumptions, not from provider billing. Cost is shown in real
 * USD: Veo bills per second of video generated, there is no free daily
 * allowance that generation stops at.
 */
export function CostEstimate({ estimate, title = "Ước tính" }: { estimate: GenerationEstimate; title?: string }) {
  if (estimate.lineItems.length === 0) {
    return <p className="wz-hint">Không còn gì phải tạo thêm.</p>;
  }

  return (
    <div className="wz-estimate">
      <h3>{title}</h3>

      <div
        style={{
          borderRadius: 8,
          padding: "10px 14px",
          margin: "0 0 12px",
          fontSize: 15,
          background: estimate.totalCostUsd === 0 ? "rgba(26,127,55,0.12)" : "var(--accent-bg)",
          border: `1px solid ${estimate.totalCostUsd === 0 ? "#1a7f37" : "var(--accent-border)"}`,
        }}
      >
        {estimate.totalCostUsd === 0 ? (
          <strong>✅ Miễn phí — không có video/ảnh AI tính phí nào cần tạo ở bước này</strong>
        ) : (
          <strong>💰 Chi phí ước tính: {formatUsd(estimate.totalCostUsd)}</strong>
        )}
      </div>

      <div style={{ display: "flex", gap: 20, flexWrap: "wrap", margin: "0 0 12px", fontSize: 14 }}>
        <span>
          🎬 Video hoàn chỉnh: <strong>{formatDuration(estimate.outputVideoSeconds)}</strong>
        </span>
        <span>
          ⏱️ Thời gian chờ tạo: <strong>~{formatDuration(estimate.totalSeconds)}</strong>
        </span>
      </div>

      <table>
        <thead>
          <tr>
            <td style={{ fontWeight: 600 }}>Bước</td>
            <td style={{ fontWeight: 600 }}>Chờ</td>
            <td style={{ fontWeight: 600 }}>Chi phí</td>
          </tr>
        </thead>
        <tbody>
          {estimate.lineItems.map((item) => (
            <tr key={item.label}>
              <td>
                {item.label}
                {item.quantity > 1 && ` ×${item.quantity}`}
              </td>
              <td>{formatDuration(item.estimatedSeconds)}</td>
              <td>{item.estimatedCostUsd > 0 ? formatUsd(item.estimatedCostUsd) : "miễn phí"}</td>
            </tr>
          ))}
          <tr>
            <td>Tổng</td>
            <td>~{formatDuration(estimate.totalSeconds)}</td>
            <td>{formatUsd(estimate.totalCostUsd)}</td>
          </tr>
        </tbody>
      </table>

      <p className="wz-hint">
        Video AI (Veo) tính phí thật theo mỗi giây được tạo ra — đây là API trả phí, <em>không</em> phải hạn mức
        credit miễn phí. Ảnh tĩnh AI rẻ hơn nhiều (gần $0). Lồng tiếng, viết kịch bản và ghép video nằm trong hạn mức
        miễn phí của Gemini. Muốn giảm chi phí: đổi bớt cảnh sang ảnh tĩnh, giảm số clip, hoặc tự tạo vài clip ở
        labs.google/flow rồi tải lên (mục "Tải clip có sẵn").
        {estimate.monthlyBudgetRemainingUsd !== null &&
          ` Trần chi phí tháng (cấu hình) còn ${formatUsd(estimate.monthlyBudgetRemainingUsd)}.`}
      </p>

      {!estimate.withinBudget && (
        <p className="wz-error" style={{ marginTop: 10 }}>
          Chi phí ước tính ({formatUsd(estimate.totalCostUsd)}) cao hơn trần chi phí tháng còn lại (chỉnh{" "}
          <code>Budget:MonthlyLimitUsd</code>).
        </p>
      )}
    </div>
  );
}

export function PresetPicker<T extends NamedPreset>({
  label,
  presets,
  selectedId,
  onSelect,
  disabled,
  extra,
}: {
  label: string;
  presets: T[];
  selectedId: string | null | undefined;
  onSelect: (id: string) => void;
  disabled?: boolean;
  extra?: (preset: T) => ReactNode;
}) {
  return (
    <section style={{ marginBottom: 22 }}>
      <h3>{label}</h3>
      <div className="wz-presets">
        {presets.map((preset) => (
          <button
            key={preset.id}
            type="button"
            className="wz-preset"
            aria-pressed={preset.id === selectedId}
            disabled={disabled}
            onClick={() => onSelect(preset.id)}
          >
            <strong>{preset.name}</strong>
            {preset.description}
            {extra?.(preset)}
          </button>
        ))}
      </div>
    </section>
  );
}

export function Field({
  label,
  hint,
  children,
}: {
  label: string;
  hint?: string;
  children: ReactNode;
}) {
  return (
    <label className="wz-field">
      <span>{label}</span>
      {children}
      {hint && <span className="wz-hint">{hint}</span>}
    </label>
  );
}

export function Blockers({ blockers }: { blockers: string[] }) {
  if (blockers.length === 0) return null;
  return (
    <ul className="wz-blockers">
      {blockers.map((blocker) => (
        <li key={blocker}>{blocker}</li>
      ))}
    </ul>
  );
}

// ---------------------------------------------------------------------------
// Credit budget (Section 2): 50/day, plan 20 + 10 + 10 + 10. Warns and blocks
// before generation when the storyboard would cost more than what is left.
// ---------------------------------------------------------------------------

export function CreditPanel({ credits }: { credits: CreditSummary }) {
  const rows: [string, string][] = [
    ["Ngân sách/ngày", `${credits.dailyBudget}`],
    ["Đã dùng hôm nay", `${credits.used}`],
    ["Đang giữ chỗ", `${credits.reserved}`],
    ["Còn lại", `${credits.remaining}`],
    ["Ước tính dự án này", `${credits.estimatedForProject}`],
  ];

  return (
    <div className="wz-credits">
      <h3>Tín dụng AI</h3>
      <div className="wz-kv">
        {rows.map(([k, v]) => (
          <span key={k}>
            <span className="wz-kv-k">{k}</span>
            <span className="wz-kv-v">{v}</span>
          </span>
        ))}
      </div>
      {credits.failedToday > 0 && (
        <p className="wz-hint" style={{ marginTop: 6 }}>
          {credits.failedToday} lần tạo thất bại hôm nay (không tốn credit — xem chi tiết bên dưới).
        </p>
      )}
      {!credits.withinBudget && (
        <p className="wz-error" style={{ marginTop: 8 }}>
          ⚠️ Kịch bản hiện tại cần ~{credits.estimatedForProject} credit nhưng chỉ còn {credits.remaining}. Hãy đổi bớt
          cảnh sang ảnh tĩnh, giảm số clip, hoặc dùng "Tiết kiệm" ở bước ý tưởng.
        </p>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Generation attempts (Sections 3 & 5): every attempt with a meaningful state
// and, on failure, the actual error - never hidden.
// ---------------------------------------------------------------------------

const ATTEMPT_LABEL: Record<GenerationAttemptState, string> = {
  Pending: "Chờ",
  Generating: "Đang tạo",
  Completed: "Xong",
  Failed: "Lỗi",
  Retrying: "Thử lại",
  Validated: "Đã kiểm tra",
};

const ATTEMPT_CLASS: Record<GenerationAttemptState, string> = {
  Pending: "",
  Generating: "wz-badge-working",
  Completed: "wz-badge-ready",
  Failed: "wz-badge-failed",
  Retrying: "wz-badge-working",
  Validated: "wz-badge-ready",
};

export function AttemptsPanel({ attempts }: { attempts: GenerationAttempt[] }) {
  if (attempts.length === 0) return null;

  return (
    <div className="wz-attempts">
      <h3>Lần tạo AI ({attempts.length})</h3>
      <ul>
        {attempts.map((a) => (
          <li key={a.id}>
            <span className={`wz-badge ${ATTEMPT_CLASS[a.state]}`}>{ATTEMPT_LABEL[a.state]}</span>
            <span className="wz-attempt-what">
              {a.kind}
              {a.sceneNumber ? ` · cảnh ${a.sceneNumber}` : ""} · {a.model}
              {a.modelTier ? ` (${a.modelTier})` : ""}
              {a.attemptNumber > 1 ? ` · lần ${a.attemptNumber}` : ""}
            </span>
            <span className="wz-attempt-cost">
              {a.actualCredits ?? a.estimatedCredits} cr
              {a.audioDurationSeconds ? ` · ${a.audioDurationSeconds.toFixed(1)}s` : ""}
            </span>
            {a.failureReason && <span className="wz-attempt-error">{a.failureReason}</span>}
          </li>
        ))}
      </ul>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Step 6 status board (Section 6): narration / captions / composition / render
// / validation. Narration is mandatory - it is called out first.
// ---------------------------------------------------------------------------

function statusTone(value: string): string {
  if (["ready", "passed", "completed", "burned"].includes(value)) return "wz-badge-ready";
  if (["running", "partial", "pending"].includes(value)) return "wz-badge-working";
  if (["missing", "failed"].includes(value)) return "wz-badge-failed";
  return "";
}

export function CompositionStatusPanel({ composition }: { composition: CompositionStatus }) {
  const rows: [string, string, string][] = [
    [
      "Lời đọc (bắt buộc)",
      composition.narrationStatus,
      `${composition.scenesWithNarration}/${composition.scenesExpectingNarration} cảnh · ${composition.narrationSeconds.toFixed(1)}s`,
    ],
    ["Phụ đề", composition.captionStatus, ""],
    ["Ghép hình", composition.compositionStatus, ""],
    ["Kết xuất", composition.renderStatus, ""],
    ["Kiểm tra cuối", composition.validationStatus, ""],
  ];

  return (
    <div className="wz-compo">
      <h3>Trạng thái âm thanh / phụ đề / kết xuất</h3>
      <ul>
        {rows.map(([k, v, extra]) => (
          <li key={k}>
            <span>{k}</span>
            <span className={`wz-badge ${statusTone(v)}`}>{v}</span>
            {extra && <span className="wz-hint">{extra}</span>}
          </li>
        ))}
      </ul>
      {composition.narrationStatus === "missing" && (
        <p className="wz-error">
          Chưa có lời đọc — video không thể hoàn tất khi thiếu tiếng người dẫn. Hãy dựng lại clip để tạo lời đọc.
        </p>
      )}
      {composition.validationErrors.length > 0 && (
        <ValidationList title="Lỗi kiểm tra" items={composition.validationErrors} error />
      )}
      {composition.validationWarnings.length > 0 && (
        <ValidationList title="Cảnh báo" items={composition.validationWarnings} />
      )}
    </div>
  );
}

export function ValidationList({ title, items, error }: { title: string; items: string[]; error?: boolean }) {
  if (items.length === 0) return null;
  return (
    <div style={{ marginTop: 8 }}>
      <strong style={{ fontSize: 13 }}>{title}</strong>
      <ul className={error ? "wz-blockers" : "wz-hint"} style={{ marginTop: 4 }}>
        {items.map((i) => (
          <li key={i}>{i}</li>
        ))}
      </ul>
    </div>
  );
}

export function ValidationBadge({ validation }: { validation: ValidationSummary }) {
  if (!validation.hasRun) {
    return <span className="wz-badge wz-badge-working">Chưa kiểm tra</span>;
  }
  return (
    <span className={`wz-badge ${validation.ok ? "wz-badge-ready" : "wz-badge-failed"}`}>
      {validation.ok ? "Đạt kiểm tra" : "Không đạt"}
    </span>
  );
}
