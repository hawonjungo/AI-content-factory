import { useEffect, useState } from "react";
import { storyboardApi, type CameraMovement, type SceneVisualKind, type VideoModelTier, type WizardClip } from "../api/client";

const KIND_LABEL: Record<SceneVisualKind, string> = {
  AiVideo: "🎬 Video AI",
  AiImage: "🖼️ Ảnh tĩnh",
};

const MODEL_OPTIONS: { value: VideoModelTier; label: string }[] = [
  { value: "Lite", label: "Veo 3.1 Lite (With Audio)  — ~$0.64 / 8s" },
  { value: "Fast", label: "Veo 3.1 Fast (With Audio)  — ~$3.20 / 8s" },
];

// One deterministic camera behaviour per clip - the prompt builder turns it
// into a single explicit "Camera:" line.
const CAMERA_OPTIONS: { value: CameraMovement; label: string }[] = [
  { value: "Unspecified", label: "Tự động (đẩy máy vào chậm)" },
  { value: "Static", label: "Máy đứng yên" },
  { value: "SlowPushIn", label: "Đẩy máy vào chậm" },
  { value: "SlowPullOut", label: "Kéo máy ra chậm" },
  { value: "HandheldFollow", label: "Cầm tay bám theo" },
  { value: "SideTracking", label: "Lia ngang song song" },
  { value: "ForwardTracking", label: "Lia tiến theo chủ thể" },
  { value: "OverShoulder", label: "Qua vai chủ thể" },
];

/**
 * One planned clip, with the two cost controls the user asked for:
 *  - video vs still-image (a still costs a fraction of a clip)
 *  - the generation prompt, shown and editable, with a one-click suggestion
 */
function ClipRow({
  contentProjectId,
  clip,
  disabled,
  onChanged,
}: {
  contentProjectId: string;
  clip: WizardClip;
  disabled: boolean;
  onChanged: () => void;
}) {
  const [prompt, setPrompt] = useState(clip.generationPrompt ?? "");
  const [busy, setBusy] = useState<null | "type" | "suggest" | "save" | "upload" | "model" | "skip" | "camera">(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setPrompt(clip.generationPrompt ?? "");
  }, [clip.generationPrompt]);

  const dirty = prompt.trim() !== (clip.generationPrompt ?? "").trim();

  // A clip the user already has a video for is locked out of every generation
  // control until they clear the flag.
  const locked = clip.skipGeneration;

  const run = async (kind: "type" | "suggest" | "save" | "upload" | "model" | "skip" | "camera", fn: () => Promise<unknown>) => {
    setBusy(kind);
    setError(null);
    try {
      await fn();
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Thao tác không thành công.");
    } finally {
      setBusy(null);
    }
  };

  const otherKind: SceneVisualKind = clip.visualType === "AiVideo" ? "AiImage" : "AiVideo";

  return (
    <li className="wz-card" style={{ marginBottom: 10, padding: 12 }}>
      <div style={{ display: "flex", gap: 8, alignItems: "center", flexWrap: "wrap", marginBottom: 6 }}>
        <strong style={{ fontSize: 13 }}>Clip {clip.number}</strong>
        <span className="wz-badge">{KIND_LABEL[clip.visualType]}</span>
        {clip.modelTier && <span className="wz-badge">{clip.modelTier}</span>}
        <span className="wz-hint">ưu tiên video {clip.aiVideoPriority}/100</span>
        <button
          type="button"
          className="wz-btn wz-btn-sm"
          disabled={disabled || busy !== null || locked}
          onClick={() =>
            run("type", () => storyboardApi.setVisualType(contentProjectId, clip.id, otherKind))
          }
          style={{ marginLeft: "auto" }}
        >
          {busy === "type" ? "..." : `Đổi sang ${KIND_LABEL[otherKind]}`}
        </button>
      </div>

      {clip.visualType === "AiVideo" && (
        <label className="wz-field" style={{ marginBottom: 6 }}>
          <span>Mô hình video cho clip này</span>
          <select
            value={(clip.modelTier as VideoModelTier | null) ?? "Lite"}
            disabled={disabled || busy !== null || locked}
            onChange={(e) =>
              run("model", () =>
                storyboardApi.setModelTier(contentProjectId, clip.id, e.target.value as VideoModelTier),
              )
            }
          >
            {MODEL_OPTIONS.map((m) => (
              <option key={m.value} value={m.value}>
                {m.label}
              </option>
            ))}
          </select>
        </label>
      )}

      {clip.visualType === "AiVideo" && (
        <label className="wz-field" style={{ marginBottom: 6 }}>
          <span>Chuyển động máy quay</span>
          <select
            value={clip.cameraMovement}
            disabled={disabled || busy !== null || locked}
            onChange={(e) =>
              run("camera", () =>
                storyboardApi.setCameraMovement(contentProjectId, clip.id, e.target.value as CameraMovement),
              )
            }
          >
            {CAMERA_OPTIONS.map((c) => (
              <option key={c.value} value={c.value}>
                {c.label}
              </option>
            ))}
          </select>
        </label>
      )}

      <label style={{ display: "block", fontSize: 13, margin: "0 0 8px" }}>
        <input
          type="checkbox"
          checked={clip.skipGeneration}
          disabled={disabled || busy !== null}
          onChange={(e) =>
            run("skip", () => storyboardApi.setSkipGeneration(contentProjectId, clip.id, e.target.checked))
          }
        />{" "}
        Đã có video / Bỏ qua Veo API
        {clip.skipGeneration && " — clip này sẽ không được tạo và không tính chi phí"}
      </label>

      {clip.allocationRationale && (
        <p className="wz-hint" style={{ margin: "0 0 6px" }}>
          ⓘ {clip.allocationRationale}
        </p>
      )}

      <p style={{ fontSize: 13, margin: "0 0 8px" }}>{clip.narration || <em>(không có lời)</em>}</p>

      <label className="wz-field" style={{ marginBottom: 6 }}>
        <span>Prompt tạo hình (chỉnh được)</span>
        <textarea
          rows={3}
          value={prompt}
          placeholder="Để trống = hệ thống tự viết prompt khi dựng"
          onChange={(e) => setPrompt(e.target.value)}
          disabled={disabled || busy !== null || locked}
        />
      </label>

      {error && <p className="wz-error">{error}</p>}

      <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
        <button
          type="button"
          className="wz-btn wz-btn-sm"
          disabled={disabled || busy !== null || locked}
          onClick={() => run("suggest", () => storyboardApi.suggestPrompt(contentProjectId, clip.id))}
        >
          {busy === "suggest" ? "Đang nghĩ..." : "Tạo gợi ý"}
        </button>
        <button
          type="button"
          className="wz-btn wz-btn-sm wz-btn-primary"
          disabled={disabled || busy !== null || !dirty || locked}
          onClick={() => run("save", () => storyboardApi.setPrompt(contentProjectId, clip.id, prompt.trim()))}
        >
          {busy === "save" ? "Đang lưu..." : "Lưu prompt"}
        </button>
        <label className="wz-btn wz-btn-sm" style={{ cursor: "pointer", opacity: disabled || busy ? 0.5 : 1 }}>
          {busy === "upload" ? "Đang tải..." : locked ? "Thay clip khác (.mp4)" : "Tải clip có sẵn (.mp4)"}
          <input
            type="file"
            accept="video/*"
            hidden
            disabled={disabled || busy !== null}
            onChange={(e) => {
              const f = e.target.files?.[0];
              if (f) run("upload", () => storyboardApi.uploadSceneVideo(contentProjectId, clip.id, f));
              e.target.value = "";
            }}
          />
        </label>
      </div>
      <p className="wz-hint" style={{ marginTop: 6 }}>
        Muốn $0 tuyệt đối: tự tạo clip ở{" "}
        <a href="https://labs.google/flow" target="_blank" rel="noreferrer">
          labs.google/flow
        </a>{" "}
        rồi tải lên đây.
      </p>
    </li>
  );
}

export function ClipPlanEditor({
  contentProjectId,
  clips,
  disabled,
  onChanged,
}: {
  contentProjectId: string;
  clips: WizardClip[];
  disabled: boolean;
  onChanged: () => void;
}) {
  if (clips.length === 0) return null;

  return (
    <div>
      <h3>Từng clip</h3>
      <p className="wz-hint" style={{ marginBottom: 10 }}>
        Đổi cảnh phụ sang ảnh tĩnh để tiết kiệm credit; giữ video cho hook và cao trào. Sửa prompt nếu muốn kiểm soát
        hình ảnh.
      </p>
      <ol style={{ listStyle: "none", padding: 0, margin: 0 }}>
        {clips.map((clip) => (
          <ClipRow
            key={clip.id}
            contentProjectId={contentProjectId}
            clip={clip}
            disabled={disabled}
            onChanged={onChanged}
          />
        ))}
      </ol>
    </div>
  );
}
