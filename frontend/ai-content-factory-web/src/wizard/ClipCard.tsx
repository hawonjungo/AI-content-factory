import { useEffect, useState } from "react";
import {
  apiUrl,
  clipCheckApi,
  describeApiError,
  wizardApi,
  type ClipCheckResult,
  type AudioMode,
  type ClipState,
  type GenerationEstimate,
  type WizardClip,
} from "../api/client";
import { CostNote, StatusBadge, formatDuration, formatUsd, type StatusTone } from "./components";

const STATE_LABELS: Record<ClipState, string> = {
  NotStarted: "Chưa dựng",
  Working: "Đang dựng",
  Ready: "Sẵn sàng",
  Failed: "Lỗi",
};

const CHECK_LABEL: Record<string, string> = { pass: "Đạt", warn: "Cần xem lại", fail: "Không đạt" };
const CHECK_TONE: Record<string, StatusTone> = { pass: "success", warn: "warning", fail: "danger" };

const STATE_TONE: Record<ClipState, StatusTone> = {
  NotStarted: "neutral",
  Working: "working",
  Ready: "success",
  Failed: "danger",
};

/**
 * One clip: preview it, edit what it says, and rerun just this clip. The
 * per-clip estimate is fetched lazily when the panel is opened, so a project
 * with 10 clips doesn't fire 10 estimate requests on load.
 */
export function ClipCard({
  contentProjectId,
  clip,
  disabled,
  audioMode,
  onRegenerated,
}: {
  contentProjectId: string;
  clip: WizardClip;
  disabled: boolean;
  /** When not "Generated", this clip has no TTS voice track - the voice controls are hidden. */
  audioMode: AudioMode;
  onRegenerated: () => void;
}) {
  // Muted has no voice track at all. Smart may add TTS for this clip (only if it
  // has no original audio - the renderer decides), so the voice controls stay.
  const usesVoice = audioMode !== "Muted";
  const [open, setOpen] = useState(false);
  const [narration, setNarration] = useState(clip.narration);
  const [regenerateVoice, setRegenerateVoice] = useState(false);
  const [estimate, setEstimate] = useState<GenerationEstimate | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [checking, setChecking] = useState(false);
  const [checkError, setCheckError] = useState<string | null>(null);
  // The server's stored result for the CURRENT clip (overview), or the answer just received.
  const [check, setCheck] = useState<ClipCheckResult | null>(clip.clipCheck ?? null);

  useEffect(() => {
    setCheck(clip.clipCheck ?? null);
  }, [clip.clipCheck]);

  const canCheck = clip.hasExistingVideo && !!clip.previewUrl && clip.visualType !== "AiImage";

  const runCheck = async (force: boolean) => {
    setChecking(true);
    setCheckError(null);
    try {
      setCheck(await clipCheckApi.check(contentProjectId, clip.id, force));
    } catch (err) {
      setCheckError(describeApiError(err, "Không kiểm tra được clip."));
    } finally {
      setChecking(false);
    }
  };

  useEffect(() => {
    setNarration(clip.narration);
  }, [clip.narration]);

  // Editing the words means the voice-over has to be redone too, otherwise
  // the audio and the captions would say different things. Only relevant when
  // the project actually uses a generated voice.
  const narrationChanged = narration.trim() !== clip.narration.trim();
  const willRedoVoice = usesVoice && (regenerateVoice || narrationChanged);

  useEffect(() => {
    if (!open) return;
    let cancelled = false;

    wizardApi
      .getClipEstimate(contentProjectId, clip.id, willRedoVoice)
      .then((result) => {
        if (!cancelled) setEstimate(result);
      })
      .catch(() => {
        if (!cancelled) setEstimate(null);
      });

    return () => {
      cancelled = true;
    };
  }, [open, willRedoVoice, contentProjectId, clip.id]);

  const handleRegenerate = async () => {
    setSubmitting(true);
    setError(null);
    try {
      await wizardApi.regenerateClip(contentProjectId, clip.id, {
        narration: narrationChanged ? narration.trim() : undefined,
        regenerateVoice: usesVoice && regenerateVoice,
      });
      setOpen(false);
      onRegenerated();
    } catch (err) {
      setError(describeApiError(err, "Không tạo lại được clip này."));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <article className="wz-clip">
      {clip.previewUrl ? (
        clip.visualType === "AiImage" ? (
          <img src={apiUrl(clip.previewUrl)} alt={`Clip ${clip.number}`} />
        ) : (
          <video controls preload="metadata" src={apiUrl(clip.previewUrl)} />
        )
      ) : (
        <div className="wz-clip-empty">
          {clip.state === "Working" ? "Đang dựng clip này..." : "Chưa có hình cho clip này"}
        </div>
      )}

      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 6, flexWrap: "wrap" }}>
        <strong style={{ fontSize: 14 }}>Clip {clip.number}</strong>
        <StatusBadge tone={STATE_TONE[clip.state]}>{STATE_LABELS[clip.state]}</StatusBadge>
        <span className="wz-badge">{clip.visualType === "AiImage" ? "🖼️ Ảnh" : "🎬 Video"}</span>
        {clip.modelTier && <span className="wz-badge">{clip.modelTier}</span>}
        <span className="wz-hint" style={{ marginLeft: "auto" }}>
          {clip.durationSeconds}s
        </span>
      </div>

      <p style={{ fontSize: 13, margin: "0 0 10px" }}>{clip.narration || <em>(không có lời)</em>}</p>

      {clip.relevantReferenceLabels.length > 0 && (
        <div style={{ display: "flex", alignItems: "center", gap: 6, marginBottom: 10, flexWrap: "wrap" }}>
          <span className="wz-hint">Ảnh tham chiếu:</span>
          {clip.relevantReferenceLabels.map((label) => (
            <span key={label} className="wz-badge">
              {label}
            </span>
          ))}
        </div>
      )}

      {canCheck && (
        <div className="wz-clip-check" style={{ marginBottom: 10 }}>
          {check ? (
            <div
              style={{
                border: "1px solid var(--border)",
                borderRadius: 6,
                padding: 8,
                fontSize: 13,
                display: "flex",
                flexDirection: "column",
                gap: 4,
              }}
            >
              <div style={{ display: "flex", gap: 6, alignItems: "center", flexWrap: "wrap" }}>
                <StatusBadge tone={CHECK_TONE[check.verdict] ?? "warning"}>
                  {CHECK_LABEL[check.verdict] ?? "Cần xem lại"} · {check.score}/100
                </StatusBadge>
                {check.characterMatches !== null && (
                  <span className="wz-badge">{check.characterMatches ? "✓ đúng nhân vật" : "✕ sai nhân vật"}</span>
                )}
                <span className="wz-badge">{check.actionMatches ? "✓ đúng hành động" : "✕ sai hành động"}</span>
              </div>
              <span>{check.summary}</span>
              {check.issues.length > 0 && (
                <ul style={{ margin: 0, paddingLeft: 18 }}>
                  {check.issues.map((issue) => (
                    <li key={issue}>{issue}</li>
                  ))}
                </ul>
              )}
              <div className="wz-actions" style={{ marginTop: 4 }}>
                <button type="button" className="wz-btn wz-btn-sm" disabled={disabled || checking} onClick={() => runCheck(true)}>
                  {checking ? "Đang kiểm tra..." : "Kiểm tra lại"}
                </button>
                <CostNote kind="clipCheck" />
              </div>
            </div>
          ) : (
            <div className="wz-actions" style={{ marginTop: 0 }}>
              <button type="button" className="wz-btn wz-btn-sm" disabled={disabled || checking} onClick={() => runCheck(false)}>
                {checking ? "Đang kiểm tra..." : "🔍 Kiểm tra clip bằng AI"}
              </button>
              <CostNote kind="clipCheck">AI so clip với ảnh tham chiếu và mô tả cảnh</CostNote>
            </div>
          )}
          {checkError && <p className="wz-error">{checkError}</p>}
        </div>
      )}

      {clip.skipGeneration ? (
        <p className="wz-hint" style={{ margin: 0 }}>
          🔒 Đã có video tải lên - bỏ chọn "Đã có video" ở bước dựng nếu muốn tạo lại bằng Veo.
        </p>
      ) : !open ? (
        <div className="wz-actions" style={{ marginTop: 0 }}>
          <button type="button" className="wz-btn wz-btn-sm" disabled={disabled} onClick={() => setOpen(true)}>
            Tạo lại clip này
          </button>
          <CostNote kind={clip.visualType === "AiImage" ? "image" : "video"}>
            {clip.visualType === "AiImage" ? "Tạo lại 1 ảnh AI" : "Tạo lại 1 clip Veo (mở ra để xem ước tính chính xác)"}
          </CostNote>
        </div>
      ) : (
        <div>
          <label className="wz-field">
            <span>Lời đọc cho clip này</span>
            <textarea rows={3} value={narration} onChange={(e) => setNarration(e.target.value)} />
          </label>

          {usesVoice ? (
            <label style={{ display: "block", fontSize: 13, marginBottom: 10 }}>
              <input
                type="checkbox"
                checked={willRedoVoice}
                disabled={narrationChanged}
                onChange={(e) => setRegenerateVoice(e.target.checked)}
              />{" "}
              Đọc lại lời{narrationChanged && " (bắt buộc vì bạn đã sửa lời)"}
              {audioMode === "Smart" && (
                <span className="wz-hint" style={{ display: "block" }}>
                  Chỉ áp dụng nếu clip này không có âm thanh gốc.
                </span>
              )}
            </label>
          ) : (
            <p className="wz-hint" style={{ marginBottom: 10 }}>
              Dự án đang tắt tiếng — clip này không có lồng tiếng AI. Sửa lời chỉ đổi phụ đề.
            </p>
          )}

          {estimate && (
            <p className="wz-hint" style={{ marginBottom: 10 }}>
              Ước tính: {formatUsd(estimate.totalCostUsd)} · khoảng {formatDuration(estimate.totalSeconds)}
            </p>
          )}

          {error && <p className="wz-error">{error}</p>}

          <div style={{ display: "flex", gap: 8 }}>
            <button
              type="button"
              className="wz-btn wz-btn-sm wz-btn-primary"
              disabled={submitting || disabled}
              onClick={handleRegenerate}
            >
              {submitting ? "Đang gửi..." : "Bắt đầu tạo lại"}
            </button>
            <button type="button" className="wz-btn wz-btn-sm" onClick={() => setOpen(false)} disabled={submitting}>
              Huỷ
            </button>
          </div>
        </div>
      )}
    </article>
  );
}
