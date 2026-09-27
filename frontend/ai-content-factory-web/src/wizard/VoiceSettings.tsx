import { useEffect, useState } from "react";
import {
  wizardApi,
  type IdeaConfig,
  type PresetCatalog,
  type ProjectOverview,
  type VoiceGenderName,
} from "../api/client";
import { Field, formatUsdEstimate, usePricing } from "./components";

const VOICE_STYLES = ["Tự nhiên", "Điềm tĩnh", "Năng lượng cao", "Kịch tính", "Thì thầm", "Ấm áp"];

/**
 * Step 6 voice configuration - moved here from Step 2 because voice is a
 * production/export setting. Reuses the existing project voice abstraction:
 * the voice preset is a preset id (wizardApi.applyPresets), the rest lives in
 * the project's ideaConfig (contentProjectsApi.update). Used as the AI voice
 * for "AI Voice" mode and as the fallback for "Smart" / "Original".
 */
export function VoiceSettings({
  overview,
  catalog,
  disabled,
  onChanged,
}: {
  overview: ProjectOverview;
  catalog: PresetCatalog;
  disabled?: boolean;
  onChanged: () => void;
}) {
  const [voiceId, setVoiceId] = useState<string | null>(overview.presets.voicePresetId ?? null);
  const [idea, setIdea] = useState<IdeaConfig>(overview.ideaConfig ?? {});
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [savedAt, setSavedAt] = useState(0);

  useEffect(() => {
    setVoiceId(overview.presets.voicePresetId ?? null);
    setIdea(overview.ideaConfig ?? {});
  }, [overview.id]); // eslint-disable-line react-hooks/exhaustive-deps

  const gender: VoiceGenderName = idea.voiceGender ?? "Unspecified";
  const voicesForGender = catalog.voices.filter(
    (v) => gender === "Unspecified" || v.gender === gender || v.gender === "Unspecified",
  );

  const patch = (p: Partial<IdeaConfig>) => setIdea((cur) => ({ ...cur, ...p }));
  const pricing = usePricing();
  const selectedFreeVoice = catalog.voices.find((v) => v.id === voiceId)?.isFree === true;

  const dirty =
    (voiceId ?? null) !== (overview.presets.voicePresetId ?? null) ||
    JSON.stringify(idea) !== JSON.stringify(overview.ideaConfig ?? {});

  const save = async () => {
    setSaving(true);
    setError(null);
    try {
      // One call: persists the voice config AND invalidates the old TTS tracks
      // so the next "Ghép video" regenerates narration with this voice.
      await wizardApi.setVoiceSettings(overview.id, {
        voicePresetId: voiceId ?? undefined,
        voiceGender: idea.voiceGender ?? "Unspecified",
        voiceStyle: idea.voiceStyle ?? undefined,
        speakingRate: idea.speakingRate ?? undefined,
        narrationLanguage: idea.narrationLanguage ?? undefined,
      });
      setSavedAt(Date.now());
      onChanged();
    } catch (err) {
      console.error("[voice settings] save failed:", err);
      setError("Không lưu được cấu hình giọng đọc. Vui lòng thử lại.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <section className="wz-card">
      <h2>Giọng đọc AI</h2>
      <p className="wz-sub">
        Dùng cho chế độ "AI Voice", và làm giọng dự phòng cho "Smart" / "Original Audio" khi một clip không có tiếng.
        Không ảnh hưởng tới phụ đề. <strong>Sau khi lưu, bấm "Ghép video" lại</strong> để tạo lại lồng tiếng theo
        giọng mới.
      </p>

      <Field label="Giới tính giọng">
        <div className="wz-radios">
          {(["Unspecified", "Male", "Female"] as VoiceGenderName[]).map((g) => (
            <label key={g}>
              <input
                type="radio"
                name="voiceGender"
                checked={gender === g}
                disabled={disabled || saving}
                onChange={() => patch({ voiceGender: g })}
              />{" "}
              {g === "Unspecified" ? "Không chọn" : g === "Male" ? "Nam" : "Nữ"}
            </label>
          ))}
        </div>
      </Field>

      <div className="wz-row">
        <Field
          label="Giọng cụ thể"
          hint={
            selectedFreeVoice
              ? `Giọng 🆓 chạy trên máy bạn (Kokoro) - $0, chỉ đọc tiếng Anh${
                  idea.narrationLanguage && !idea.narrationLanguage.toLowerCase().startsWith("en")
                    ? " ⚠️ ngôn ngữ đang là " + idea.narrationLanguage + " nên sẽ bị từ chối"
                    : ""
                }. Cần chạy trước: docker compose --profile free-tts up -d kokoro`
              : `Giọng Gemini: ≈ ${formatUsdEstimate(pricing.ttsUsdPer1000Chars)} / 1.000 ký tự lời đọc.`
          }
        >
          <select
            value={voiceId ?? ""}
            disabled={disabled || saving}
            onChange={(e) => setVoiceId(e.target.value || null)}
          >
            <option value="">(mặc định theo mẫu)</option>
            {voicesForGender.map((v) => (
              <option key={v.id} value={v.id}>
                {v.name} — {v.description}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Phong cách đọc">
          <select
            value={idea.voiceStyle ?? ""}
            disabled={disabled || saving}
            onChange={(e) => patch({ voiceStyle: e.target.value || undefined })}
          >
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
        <Field label={`Tốc độ nói (${(idea.speakingRate ?? 1).toFixed(2)}×)`}>
          <input
            type="range"
            min={0.5}
            max={1.5}
            step={0.05}
            value={idea.speakingRate ?? 1}
            disabled={disabled || saving}
            onChange={(e) => patch({ speakingRate: Number(e.target.value) })}
          />
        </Field>
        <Field label="Ngôn ngữ / giọng vùng" hint="BCP-47, ví dụ vi-VN, en-US.">
          <input
            type="text"
            value={idea.narrationLanguage ?? ""}
            disabled={disabled || saving}
            onChange={(e) => patch({ narrationLanguage: e.target.value || undefined })}
          />
        </Field>
      </div>

      <div className="wz-actions" style={{ marginTop: 8 }}>
        <button
          type="button"
          className="wz-btn wz-btn-sm wz-btn-primary"
          disabled={disabled || saving || !dirty}
          onClick={save}
        >
          {saving ? "Đang lưu..." : "Lưu giọng đọc"}
        </button>
        {!dirty && savedAt > 0 && (
          <span className="wz-hint">Đã lưu ✓ — bấm "Ghép video" để áp dụng giọng mới.</span>
        )}
      </div>
      {error && (
        <p className="wz-error" style={{ marginTop: 8 }}>
          {error}
        </p>
      )}
    </section>
  );
}
