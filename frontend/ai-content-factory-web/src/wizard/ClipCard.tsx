import { useEffect, useState } from "react";
import { apiUrl, wizardApi, type ClipState, type GenerationEstimate, type WizardClip } from "../api/client";
import { formatDuration, formatUsd } from "./components";

const STATE_LABELS: Record<ClipState, string> = {
  NotStarted: "Chưa dựng",
  Working: "Đang dựng",
  Ready: "Sẵn sàng",
  Failed: "Lỗi",
};

const STATE_CLASSES: Record<ClipState, string> = {
  NotStarted: "",
  Working: "wz-badge-working",
  Ready: "wz-badge-ready",
  Failed: "wz-badge-failed",
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
  onRegenerated,
}: {
  contentProjectId: string;
  clip: WizardClip;
  disabled: boolean;
  onRegenerated: () => void;
}) {
  const [open, setOpen] = useState(false);
  const [narration, setNarration] = useState(clip.narration);
  const [regenerateVoice, setRegenerateVoice] = useState(false);
  const [estimate, setEstimate] = useState<GenerationEstimate | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setNarration(clip.narration);
  }, [clip.narration]);

  // Editing the words means the voice-over has to be redone too, otherwise
  // the audio and the captions would say different things.
  const narrationChanged = narration.trim() !== clip.narration.trim();
  const willRedoVoice = regenerateVoice || narrationChanged;

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
        regenerateVoice,
      });
      setOpen(false);
      onRegenerated();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Không tạo lại được clip này.");
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
        <span className={`wz-badge ${STATE_CLASSES[clip.state]}`}>{STATE_LABELS[clip.state]}</span>
        <span className="wz-badge">{clip.visualType === "AiImage" ? "🖼️ Ảnh" : "🎬 Video"}</span>
        {clip.modelTier && <span className="wz-badge">{clip.modelTier}</span>}
        <span className="wz-hint" style={{ marginLeft: "auto" }}>
          {clip.durationSeconds}s
        </span>
      </div>

      <p style={{ fontSize: 13, margin: "0 0 10px" }}>{clip.narration || <em>(không có lời)</em>}</p>

      {clip.skipGeneration ? (
        <p className="wz-hint" style={{ margin: 0 }}>
          🔒 Đã có video tải lên - bỏ chọn "Đã có video" ở bước dựng nếu muốn tạo lại bằng Veo.
        </p>
      ) : !open ? (
        <button type="button" className="wz-btn wz-btn-sm" disabled={disabled} onClick={() => setOpen(true)}>
          Tạo lại clip này
        </button>
      ) : (
        <div>
          <label className="wz-field">
            <span>Lời đọc cho clip này</span>
            <textarea rows={3} value={narration} onChange={(e) => setNarration(e.target.value)} />
          </label>

          <label style={{ display: "block", fontSize: 13, marginBottom: 10 }}>
            <input
              type="checkbox"
              checked={willRedoVoice}
              disabled={narrationChanged}
              onChange={(e) => setRegenerateVoice(e.target.checked)}
            />{" "}
            Đọc lại lời{narrationChanged && " (bắt buộc vì bạn đã sửa lời)"}
          </label>

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
