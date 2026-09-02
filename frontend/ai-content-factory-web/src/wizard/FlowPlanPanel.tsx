import { useEffect, useState } from "react";
import { apiUrl, flowApi, type FlowGenerationPlan } from "../api/client";

const FLOW_URL = "https://labs.google/flow";

/**
 * Step 5: the Google Flow Generation Plan. The app never calls Flow - it lays
 * out, per scene, the prompt, the reference images, the recommended model and
 * the credit cost, plus a one-click "copy all prompts" so the user can run the
 * video-generation step in Flow deliberately.
 */
export function FlowPlanPanel({ contentProjectId }: { contentProjectId: string }) {
  const [plan, setPlan] = useState<FlowGenerationPlan | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [copied, setCopied] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    flowApi
      .getPlan(contentProjectId)
      .then((p) => {
        if (!cancelled) setPlan(p);
      })
      .catch((e) => {
        if (!cancelled) setError(e instanceof Error ? e.message : "Không tải được kế hoạch Flow.");
      });
    return () => {
      cancelled = true;
    };
  }, [contentProjectId]);

  const copy = async (key: string, text: string) => {
    try {
      await navigator.clipboard.writeText(text);
      setCopied(key);
      window.setTimeout(() => setCopied((c) => (c === key ? null : c)), 1500);
    } catch {
      setError("Trình duyệt chặn copy - hãy chọn và copy thủ công.");
    }
  };

  if (error) return <p className="wz-error">{error}</p>;
  if (!plan) return <p className="wz-hint">Đang lập kế hoạch Flow...</p>;

  // Scenes the user already has a clip for are not part of the Flow work.
  const videoScenes = plan.scenes.filter((s) => s.generationType === "AI_VIDEO" && !s.skipGeneration);

  return (
    <section className="wz-card">
      <h2>Kế hoạch tạo video trong Google Flow</h2>
      <p className="wz-sub">
        Ứng dụng <strong>không tự gọi</strong> Google Flow. Bạn tự tạo clip trong Flow bằng các prompt bên dưới, rồi
        import lại ở bước 6.
      </p>

      <div className="wz-kv" style={{ marginBottom: 8 }}>
        <span><span className="wz-kv-k">Ngân sách Flow/ngày</span><span className="wz-kv-v">{plan.dailyBudgetCredits}</span></span>
        <span><span className="wz-kv-k">Đã dùng</span><span className="wz-kv-v">{plan.usedCredits}</span></span>
        <span><span className="wz-kv-k">Còn lại</span><span className="wz-kv-v">{plan.remainingCredits}</span></span>
        <span><span className="wz-kv-k">Kế hoạch này</span><span className="wz-kv-v">{plan.plannedCredits}</span></span>
        <span><span className="wz-kv-k">Cảnh cần Flow</span><span className="wz-kv-v">{plan.scenesRequiringFlow}</span></span>
      </div>
      {!plan.withinBudget && (
        <p className="wz-error">
          ⚠️ Kế hoạch cần {plan.plannedCredits} credit nhưng Flow chỉ còn {plan.remainingCredits} hôm nay. Đổi bớt cảnh
          sang ảnh tĩnh hoặc chờ reset ngày mới.
        </p>
      )}

      <div className="wz-actions" style={{ marginBottom: 4 }}>
        <a className="wz-btn wz-btn-primary" href={FLOW_URL} target="_blank" rel="noreferrer">
          Mở Google Flow ↗
        </a>
        <button type="button" className="wz-btn" disabled={videoScenes.length === 0} onClick={() => copy("all", plan.copyAllText)}>
          {copied === "all" ? "Đã copy ✓" : "Copy tất cả (để lưu/xem lại)"}
        </button>
      </div>
      {videoScenes.length > 0 && (
        <p className="wz-hint" style={{ marginTop: 0 }}>
          Mỗi cảnh bên dưới là <strong>một clip riêng</strong> - Flow chỉ tạo được một clip mỗi lần "Generate". Dùng nút
          "Copy prompt" của từng cảnh và dán từng cái một, đừng dán gộp nhiều cảnh vào cùng một lần tạo.
        </p>
      )}

      {videoScenes.map((s) => (
        <div key={s.sceneId} className="wz-card" style={{ padding: 12, marginTop: 10 }}>
          <div style={{ display: "flex", gap: 8, alignItems: "center", flexWrap: "wrap", marginBottom: 6 }}>
            <strong style={{ fontSize: 13 }}>Cảnh {s.sceneNumber}</strong>
            <span className="wz-badge">{s.recommendedModel}</span>
            <span className="wz-badge">{s.estimatedCredits} cr</span>
            {s.characterRequired && <span className="wz-badge">có nhân vật</span>}
            <span className="wz-hint" style={{ marginLeft: "auto" }}>ưu tiên {s.priority}/100</span>
          </div>
          <textarea readOnly rows={5} value={s.flowVideoPrompt} style={{ width: "100%", boxSizing: "border-box" }} />
          <div style={{ display: "flex", gap: 8, alignItems: "center", flexWrap: "wrap", marginTop: 6 }}>
            <button type="button" className="wz-btn wz-btn-sm" onClick={() => copy(s.sceneId, s.flowVideoPrompt)}>
              {copied === s.sceneId ? "Đã copy ✓" : "Copy prompt"}
            </button>
            {s.referenceImageUrls.map((u) => (
              <img key={u} src={apiUrl(u)} alt="ref" style={{ height: 44, borderRadius: 6, border: "1px solid var(--border)" }} />
            ))}
          </div>
          {s.rationale && <p className="wz-hint" style={{ marginTop: 6 }}>ⓘ {s.rationale}</p>}
        </div>
      ))}

      {plan.scenes.some((s) => s.generationType !== "AI_VIDEO") && (
        <p className="wz-hint" style={{ marginTop: 10 }}>
          Cảnh ảnh tĩnh (
          {plan.scenes.filter((s) => s.generationType !== "AI_VIDEO").map((s) => s.sceneNumber).join(", ")}
          ) do ứng dụng tự tạo, không cần Flow.
        </p>
      )}
    </section>
  );
}
