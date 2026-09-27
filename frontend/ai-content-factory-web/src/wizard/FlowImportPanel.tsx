import { useCallback, useEffect, useState } from "react";
import { describeApiError, flowApi, type FlowImportStatus } from "../api/client";
import { ErrorMessage, Loading, StatusBadge } from "./components";

/**
 * Step 6: import the clips generated in Google Flow. Each clip is validated
 * (readable, non-zero duration, resolution/aspect), matched to its scene and
 * booked; the panel shows which scenes are still missing or invalid so the
 * timeline is never built with a hole.
 */
export function FlowImportPanel({
  contentProjectId,
  disabled,
  onChanged,
}: {
  contentProjectId: string;
  disabled: boolean;
  onChanged: () => void;
}) {
  const [status, setStatus] = useState<FlowImportStatus | null>(null);
  const [busyScene, setBusyScene] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(() => {
    flowApi
      .getImportStatus(contentProjectId)
      .then(setStatus)
      .catch((e) => setError(describeApiError(e, "Không tải được trạng thái import.")));
  }, [contentProjectId]);

  useEffect(load, [load]);

  const importClip = async (sceneId: string, file: File | undefined) => {
    if (!file) return;
    setBusyScene(sceneId);
    setError(null);
    try {
      const result = await flowApi.importClip(contentProjectId, sceneId, file);
      if (!result.accepted) {
        setError(`Cảnh ${result.sceneNumber}: ${result.issues.join("; ")}`);
      } else if (result.warnings.length) {
        setError(`Cảnh ${result.sceneNumber} (đã nhận, có cảnh báo): ${result.warnings.join("; ")}`);
      }
      load();
      onChanged();
    } catch (e) {
      setError(describeApiError(e, "Import thất bại."));
    } finally {
      setBusyScene(null);
    }
  };

  if (!status) return <Loading label="Đang kiểm tra clip Flow..." />;

  return (
    <section className="wz-card">
      <h2>Import clip từ Google Flow</h2>
      <p className="wz-sub">
        {status.importedValid}/{status.scenesNeedingFlow} clip Flow hợp lệ ·{" "}
        {status.readyForRender ? "✅ đủ hình cho mọi cảnh" : `⚠️ còn thiếu ${status.missingOrInvalid}`}
      </p>
      <ErrorMessage message={error} />

      <ul className="wz-compo">
        {status.scenes.map((s) => {
          const ok = s.needsFlowClip ? s.hasClip && s.clipValid : s.issues.length === 0;
          return (
            <li key={s.sceneId} style={{ flexWrap: "wrap" }}>
              <span style={{ minWidth: 120 }}>
                Cảnh {s.sceneNumber} <span className="wz-hint">[{s.generationType}]</span>
              </span>
              <StatusBadge tone={ok ? "success" : "danger"}>
                {s.needsFlowClip
                  ? s.hasClip
                    ? s.clipValid
                      ? "clip OK"
                      : "clip lỗi"
                    : "thiếu clip"
                  : ok
                    ? "OK"
                    : "thiếu hình"}
              </StatusBadge>
              {s.hasClip && s.durationSeconds != null && (
                <span className="wz-hint">
                  {s.durationSeconds.toFixed(1)}s · {s.width}x{s.height}
                </span>
              )}
              {s.needsFlowClip && (
                <label
                  className="wz-btn wz-btn-sm"
                  style={{ cursor: disabled ? "not-allowed" : "pointer", opacity: disabled ? 0.5 : 1, marginLeft: "auto" }}
                >
                  {busyScene === s.sceneId ? "Đang import..." : s.hasClip ? "Thay clip" : "Import clip .mp4"}
                  <input
                    type="file"
                    accept="video/*"
                    hidden
                    disabled={disabled || busyScene !== null}
                    onChange={(e) => {
                      const f = e.target.files?.[0];
                      importClip(s.sceneId, f);
                      e.target.value = "";
                    }}
                  />
                </label>
              )}
              {s.issues.map((i) => (
                <span key={i} className="wz-hint" style={{ flexBasis: "100%", color: "#b42318" }}>
                  {i}
                </span>
              ))}
            </li>
          );
        })}
      </ul>
    </section>
  );
}
