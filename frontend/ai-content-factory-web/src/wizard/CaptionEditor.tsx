import { useEffect, useState } from "react";
import {
  apiUrl,
  wizardApi,
  type CaptionAnimation,
  type CaptionPosition,
  type CaptionPreset,
  type CaptionSettings,
} from "../api/client";
import { Field, PresetPicker } from "./components";

const POSITIONS: { value: CaptionPosition; label: string }[] = [
  { value: "Bottom", label: "Dưới" },
  { value: "Center", label: "Giữa" },
  { value: "Top", label: "Trên" },
];

const ANIMATIONS: { value: CaptionAnimation; label: string }[] = [
  { value: "None", label: "Không hiệu ứng" },
  { value: "FadeIn", label: "Mờ dần hiện ra" },
  { value: "PopIn", label: "Nảy lên" },
  { value: "SlideUp", label: "Trượt lên" },
];

/**
 * Captions as a real preset with parameters, not a burn-in on/off checkbox.
 * Saving settings and re-rendering never touches the clips, so trying
 * different caption looks costs nothing.
 */
export function CaptionEditor({
  contentProjectId,
  presets,
  selectedPresetId,
  settings,
  previewUrl,
  disabled,
  onApplyPreset,
  onSaved,
}: {
  contentProjectId: string;
  presets: CaptionPreset[];
  selectedPresetId: string | null;
  settings: CaptionSettings;
  previewUrl: string | null;
  disabled: boolean;
  onApplyPreset: (presetId: string) => Promise<void>;
  onSaved: () => void;
}) {
  const [draft, setDraft] = useState<CaptionSettings>(settings);
  const [saving, setSaving] = useState(false);
  const [previewing, setPreviewing] = useState(false);
  const [localPreview, setLocalPreview] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setDraft(settings);
  }, [settings]);

  const set = <K extends keyof CaptionSettings>(key: K, value: CaptionSettings[K]) =>
    setDraft((current) => ({ ...current, [key]: value }));

  const handleSave = async () => {
    setSaving(true);
    setError(null);
    try {
      await wizardApi.updateCaptions(contentProjectId, draft);
      onSaved();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Không lưu được cài đặt phụ đề.");
    } finally {
      setSaving(false);
    }
  };

  // Preview always saves first: the server renders from the stored settings,
  // so previewing an unsaved draft would show the wrong thing.
  const handlePreview = async () => {
    setPreviewing(true);
    setError(null);
    try {
      await wizardApi.updateCaptions(contentProjectId, draft);
      const result = await wizardApi.previewCaptions(contentProjectId);
      setLocalPreview(result.previewUrl);
      onSaved();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Chưa xem trước được - cần ít nhất một clip hoặc ảnh mẫu.");
    } finally {
      setPreviewing(false);
    }
  };

  const shownPreview = localPreview ?? previewUrl;

  return (
    <section className="wz-card">
      <h2>Phụ đề</h2>
      <p className="wz-sub">
        Đổi kiểu phụ đề rồi ghép lại video không tốn thêm chi phí - các clip được dùng lại nguyên vẹn.
      </p>

      <PresetPicker
        label="Kiểu phụ đề"
        presets={presets}
        selectedId={selectedPresetId}
        disabled={disabled}
        onSelect={(id) => onApplyPreset(id)}
      />

      <label style={{ display: "block", fontSize: 14, marginBottom: 14 }}>
        <input type="checkbox" checked={draft.enabled} onChange={(e) => set("enabled", e.target.checked)} /> Hiện phụ đề
        trên video
      </label>

      {draft.enabled && (
        <>
          <div className="wz-row">
            <Field label="Cỡ chữ">
              <input
                type="number"
                min={12}
                max={160}
                value={draft.fontSizePt}
                onChange={(e) => set("fontSizePt", Number(e.target.value))}
              />
            </Field>
            <Field label="Số từ mỗi lần hiện" hint="Ngắn thì dễ đọc hơn trên điện thoại.">
              <input
                type="number"
                min={1}
                max={20}
                value={draft.maxWordsPerCue}
                onChange={(e) => set("maxWordsPerCue", Number(e.target.value))}
              />
            </Field>
            <Field label="Vị trí">
              <select value={draft.position} onChange={(e) => set("position", e.target.value as CaptionPosition)}>
                {POSITIONS.map((p) => (
                  <option key={p.value} value={p.value}>
                    {p.label}
                  </option>
                ))}
              </select>
            </Field>
          </div>

          <div className="wz-row">
            <Field label="Màu chữ">
              <input type="color" value={draft.primaryColor} onChange={(e) => set("primaryColor", e.target.value)} />
            </Field>
            <Field label="Màu chữ đang đọc">
              <input
                type="color"
                value={draft.highlightColor}
                onChange={(e) => set("highlightColor", e.target.value)}
              />
            </Field>
            <Field label="Màu viền">
              <input type="color" value={draft.outlineColor} onChange={(e) => set("outlineColor", e.target.value)} />
            </Field>
          </div>

          <div className="wz-row">
            <Field label="Độ dày viền">
              <input
                type="number"
                min={0}
                max={20}
                step={0.5}
                value={draft.outlineWidth}
                onChange={(e) => set("outlineWidth", Number(e.target.value))}
              />
            </Field>
            <Field label="Khoảng cách mép (px)">
              <input
                type="number"
                min={0}
                max={900}
                value={draft.marginVerticalPx}
                onChange={(e) => set("marginVerticalPx", Number(e.target.value))}
              />
            </Field>
            <Field label="Hiệu ứng xuất hiện">
              <select value={draft.animation} onChange={(e) => set("animation", e.target.value as CaptionAnimation)}>
                {ANIMATIONS.map((a) => (
                  <option key={a.value} value={a.value}>
                    {a.label}
                  </option>
                ))}
              </select>
            </Field>
          </div>

          <div style={{ display: "flex", gap: 18, flexWrap: "wrap", fontSize: 14, marginBottom: 14 }}>
            <label>
              <input type="checkbox" checked={draft.bold} onChange={(e) => set("bold", e.target.checked)} /> Chữ đậm
            </label>
            <label>
              <input type="checkbox" checked={draft.uppercase} onChange={(e) => set("uppercase", e.target.checked)} />{" "}
              VIẾT HOA
            </label>
            <label>
              <input type="checkbox" checked={draft.karaoke} onChange={(e) => set("karaoke", e.target.checked)} /> Sáng
              từng chữ theo lời đọc
            </label>
          </div>

          {draft.karaoke && (
            <p className="wz-hint" style={{ marginBottom: 14 }}>
              Thời điểm sáng của từng chữ là ước lượng theo độ dài từ, không phải căn khớp chính xác với âm thanh - có
              thể lệch nhẹ ở câu có nhiều dấu ngắt.
            </p>
          )}
        </>
      )}

      {error && <p className="wz-error">{error}</p>}

      <div className="wz-actions">
        <button type="button" className="wz-btn" disabled={saving || disabled} onClick={handleSave}>
          {saving ? "Đang lưu..." : "Lưu cài đặt"}
        </button>
        <button type="button" className="wz-btn" disabled={previewing || disabled} onClick={handlePreview}>
          {previewing ? "Đang vẽ..." : "Xem thử phụ đề"}
        </button>
      </div>

      {shownPreview && (
        <div style={{ marginTop: 16 }}>
          <img className="wz-caption-preview" src={apiUrl(shownPreview)} alt="Xem trước phụ đề" />
        </div>
      )}
    </section>
  );
}
