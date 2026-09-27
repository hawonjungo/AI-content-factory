import { useState } from "react";
import { wizardApi, type AudioMode } from "../api/client";

const OPTIONS: { value: AudioMode; label: string; hint: string }[] = [
  {
    value: "Smart",
    label: "Smart / Auto",
    hint: "Có tiếng gốc dùng được thì giữ; cảnh cần lời mà không có tiếng thì tạo AI voice; còn lại im lặng.",
  },
  {
    value: "Original",
    label: "Original Audio",
    hint: "Ưu tiên tiếng gốc ở mọi clip có sẵn. Clip không có tiếng thì dùng AI voice làm dự phòng nếu cần lời.",
  },
  {
    value: "Generated",
    label: "AI Voice",
    hint: "Bỏ qua tiếng gốc, tạo giọng đọc AI cho mọi cảnh có lời (tốn token TTS).",
  },
  {
    value: "Muted",
    label: "Mute",
    hint: "Bỏ toàn bộ âm thanh khỏi video cuối. Phụ đề vẫn hiển thị bình thường.",
  },
];

/**
 * Step 6 Voice option. Persists to the project's composition settings; the
 * render pipeline reads it on the next "Ghép video".
 */
export function AudioModeSelector({
  contentProjectId,
  value,
  disabled,
  onChanged,
}: {
  contentProjectId: string;
  value: AudioMode;
  disabled?: boolean;
  onChanged: () => void;
}) {
  const [saving, setSaving] = useState<AudioMode | null>(null);
  const [error, setError] = useState<string | null>(null);

  const pick = async (mode: AudioMode) => {
    if (mode === value || saving) return;
    setSaving(mode);
    setError(null);
    try {
      await wizardApi.setAudioMode(contentProjectId, mode);
      onChanged();
    } catch (err) {
      console.error("[audio mode] failed to save:", err);
      setError("Không lưu được lựa chọn âm thanh. Vui lòng thử lại.");
    } finally {
      setSaving(null);
    }
  };

  const locked = disabled || saving !== null;

  return (
    <section className="wz-card">
      <h2>Âm thanh / Giọng đọc</h2>
      <p className="wz-sub">
        Xử lý âm thanh theo <strong>từng clip</strong>: clip Google Flow / clip bạn tải lên thường đã có giọng riêng,
        còn ảnh tĩnh thì không. Áp dụng ở lần "Ghép video" tiếp theo. Giọng lồng tiếng dự phòng lấy theo thiết lập
        Giọng đọc ở Bước 2.
      </p>

      <div className="wz-audio-modes">
        {OPTIONS.map((o) => (
          <label key={o.value} className="wz-audio-opt" data-active={value === o.value ? "true" : undefined}>
            <input
              type="radio"
              name="audioMode"
              checked={value === o.value}
              disabled={locked}
              onChange={() => pick(o.value)}
            />
            <span>
              <strong>
                {o.label}
                {saving === o.value ? " (đang lưu...)" : ""}
              </strong>
              <span className="wz-hint" style={{ display: "block", marginTop: 2 }}>
                {o.hint}
              </span>
            </span>
          </label>
        ))}
      </div>

      {(value === "Smart" || value === "Original") && (
        <p className="wz-hint" style={{ marginTop: 10 }}>
          ℹ️ Clip đã có âm thanh → giữ nguyên. Clip không có âm thanh nhưng có lời → lồng tiếng AI (theo giọng cấu hình
          bên dưới). Phụ đề luôn hiển thị ở mọi clip.
        </p>
      )}
      {value === "Muted" && (
        <p className="wz-hint" style={{ marginTop: 10 }}>
          ℹ️ Không âm thanh nào cả — nhưng phụ đề vẫn được ghép bình thường.
        </p>
      )}
      {error && (
        <p className="wz-error" style={{ marginTop: 8 }}>
          {error}
        </p>
      )}
    </section>
  );
}
