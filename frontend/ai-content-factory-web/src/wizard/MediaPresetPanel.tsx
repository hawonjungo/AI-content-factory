import { useEffect, useState } from "react";
import {
  BUILTIN_PRESETS,
  estimatePresetTotal,
  loadUserPresets,
  observedCreditsByOption,
  optionIdForScene,
  optionById,
  saveUserPresets,
  type MediaPreset,
  type MergedScene,
} from "./mediaPresets";
import { ErrorMessage } from "./components";

/**
 * Step 5's "Default Media Preset" panel: pick a rule table (Video/Image/
 * Animation per scene position) and apply it to every scene that hasn't been
 * manually overridden. Built-in presets are fixed; user presets are saved
 * from the CURRENT per-scene configuration and persisted in localStorage
 * (there is no server-side concept of a preset - this is a pure client
 * convenience over the same per-scene endpoints the card selector uses).
 */
export function MediaPresetPanel({
  scenes,
  overrides,
  disabled,
  applying,
  onApplyPreset,
  onResetOverrides,
}: {
  scenes: MergedScene[];
  overrides: Set<string>;
  disabled: boolean;
  applying: boolean;
  onApplyPreset: (preset: MediaPreset) => void;
  onResetOverrides: (preset: MediaPreset) => void;
}) {
  const [userPresets, setUserPresets] = useState<MediaPreset[]>(() => loadUserPresets());
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    saveUserPresets(userPresets);
  }, [userPresets]);

  const allPresets = [...BUILTIN_PRESETS, ...userPresets];
  const selected = allPresets.find((p) => p.id === selectedId) ?? null;
  const credits = observedCreditsByOption(scenes);
  const liveTotal = scenes.reduce((sum, s) => sum + s.estimatedCredits, 0);
  const overrideCount = scenes.filter((s) => overrides.has(s.sceneId)).length;

  const saveCurrentAsPreset = () => {
    setError(null);
    const name = window.prompt("Tên preset mới:");
    if (!name || !name.trim()) return;
    if (scenes.length === 0) {
      setError("Chưa có cảnh nào để lưu thành preset.");
      return;
    }
    const rules = scenes.map((s) => {
      const optionId = optionIdForScene(s.visualTypeRaw, s.modelTier);
      const o = optionById(optionId);
      return { visualType: o.visualType, modelTier: o.modelTier };
    });
    const preset: MediaPreset = { id: `user:${Date.now()}`, name: name.trim(), builtin: false, rules };
    setUserPresets((cur) => [...cur, preset]);
    setSelectedId(preset.id);
  };

  const renamePreset = (preset: MediaPreset) => {
    if (preset.builtin) return;
    const name = window.prompt("Đổi tên preset:", preset.name);
    if (!name || !name.trim()) return;
    setUserPresets((cur) => cur.map((p) => (p.id === preset.id ? { ...p, name: name.trim() } : p)));
  };

  const deletePreset = (preset: MediaPreset) => {
    if (preset.builtin) return;
    if (!window.confirm(`Xoá preset "${preset.name}"?`)) return;
    setUserPresets((cur) => cur.filter((p) => p.id !== preset.id));
    if (selectedId === preset.id) setSelectedId(null);
  };

  const busy = disabled || applying;

  return (
    <section className="wz-card">
      <h2>Bộ mặc định loại media</h2>
      <p className="wz-sub">
        Áp một bộ Video / Ảnh / Animation cho toàn bộ cảnh theo thứ tự - cảnh nào bạn đã tự đổi thủ công ở dưới sẽ được
        giữ nguyên, không bị ghi đè.
      </p>

      <ErrorMessage message={error} />

      <div className="wz-preset-scroll">
        {allPresets.map((preset) => {
          const total = estimatePresetTotal(scenes, preset, overrides, credits);
          return (
            <button
              key={preset.id}
              type="button"
              className="wz-preset-chip"
              aria-pressed={selectedId === preset.id}
              disabled={busy}
              onClick={() => setSelectedId(preset.id)}
            >
              <strong>{preset.name}</strong>
              <span className="wz-hint">{total === null ? "~ chưa rõ credit" : `~${total} credit`}</span>
              {!preset.builtin && <span className="wz-badge">tuỳ chỉnh</span>}
            </button>
          );
        })}
      </div>

      <p className="wz-hint" style={{ margin: "10px 0" }}>
        Tổng credit hiện tại (theo cấu hình đang áp dụng thật sự): <strong>{liveTotal}</strong>
        {overrideCount > 0 && ` · ${overrideCount} cảnh đã đổi thủ công (Override)`}
      </p>

      <div className="wz-actions" style={{ marginTop: 0 }}>
        <button
          type="button"
          className="wz-btn wz-btn-primary wz-btn-sm"
          disabled={busy || !selected}
          onClick={() => selected && onApplyPreset(selected)}
          title={selected ? undefined : "Chọn một preset ở trên trước"}
        >
          {applying ? "Đang áp dụng..." : "Áp dụng preset"}
        </button>
        <button
          type="button"
          className="wz-btn wz-btn-sm"
          disabled={busy || !selected || overrideCount === 0}
          onClick={() => selected && onResetOverrides(selected)}
          title={
            !selected
              ? "Chọn một preset ở trên trước"
              : overrideCount === 0
                ? "Chưa có cảnh nào bị đổi thủ công"
                : "Bỏ mọi Override rồi áp lại preset đang chọn cho toàn bộ cảnh"
          }
        >
          Reset Override & áp lại
        </button>
        <button type="button" className="wz-btn wz-btn-sm" disabled={busy} onClick={saveCurrentAsPreset}>
          Lưu cấu hình hiện tại thành preset
        </button>
        {selected && !selected.builtin && (
          <>
            <button type="button" className="wz-btn wz-btn-sm" disabled={busy} onClick={() => renamePreset(selected)}>
              Đổi tên
            </button>
            <button
              type="button"
              className="wz-btn wz-btn-sm wz-btn-danger"
              disabled={busy}
              onClick={() => deletePreset(selected)}
            >
              Xoá
            </button>
          </>
        )}
      </div>
    </section>
  );
}
