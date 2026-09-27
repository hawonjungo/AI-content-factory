import { useEffect, useMemo, useRef, useState } from "react";
import {
  API_BASE_URL,
  apiUrl,
  describeApiError,
  flowApi,
  storyboardApi,
  type CameraMovement,
  type FlowGenerationPlan,
  type ShotSize,
  type WizardClip,
} from "../api/client";
import { CostNote, ErrorMessage, Loading, StatusBadge } from "./components";
import { MediaPresetPanel } from "./MediaPresetPanel";
import { StockPicker } from "./StockPicker";
import {
  MEDIA_OPTIONS,
  loadOverrides,
  mergeScenes,
  optionById,
  optionForRule,
  optionIdForScene,
  readCachedPrompt,
  ruleForIndex,
  saveOverrides,
  writeCachedPrompt,
  type MediaOptionId,
  type MediaPreset,
  type MergedScene,
} from "./mediaPresets";

/**
 * Local HTTP "companion service" from automation/google-flow-runner
 * (`npm run serve`, see that package's README for the full contract). Must
 * match that package's GFR_SERVE_PORT env var (default 4545). This is an
 * optional, purely local dev tool - it is never reachable if the user hasn't
 * started it, and the manual copy-prompt workflow above never depends on it.
 */
const AUTOMATION_SERVICE_URL = "http://127.0.0.1:4545";

type AutomationRunState = "idle" | "running" | "completed" | "failed";

interface AutomationSceneStatus {
  state: string;
  message?: string;
}

interface AutomationRunResult {
  completed: number;
  skipped: number;
  failed: number;
  failedScenes: number[];
}

interface AutomationStatusResponse {
  runState: AutomationRunState;
  startedAt: string | null;
  updatedAt: string | null;
  /** Keyed by sceneNumber-as-string, matching data-scene-index in this file. */
  scenes: Record<string, AutomationSceneStatus>;
  result: AutomationRunResult | null;
  error?: string;
}

const AUTOMATION_STATE_LABELS: Record<string, string> = {
  PENDING: "Chờ xử lý",
  GENERATING: "Đang tạo",
  WAITING_RENDER: "Đang dựng",
  DOWNLOADING: "Đang tải",
  VALIDATING: "Đang kiểm tra",
  IMPORTING: "Đang nhập",
  DONE: "✓ Xong",
  SKIPPED: "Đã có sẵn",
  FAILED: "✕ Lỗi",
};

interface ManualTool {
  key: string;
  name: string;
  domain: string;
  url: string;
  dailyCredits: number;
}

/**
 * Manual, human-in-the-loop video tools the app never calls. Every prompt
 * offered here comes from the same provider-agnostic VideoPromptBuilder
 * template, so "Copy Prompt" is identical no matter which tool the user
 * picks. Google Flow is the primary/default workflow; the rest are offered
 * as secondary alternatives, not equal-weight options.
 */
const MANUAL_TOOLS: ManualTool[] = [
  { key: "flow", name: "Google Flow", domain: "labs.google/flow", url: "https://labs.google/flow", dailyCredits: 50 },
  { key: "kling", name: "Kling AI", domain: "klingai.com", url: "https://klingai.com", dailyCredits: 66 },
  { key: "pika", name: "Pika Labs", domain: "pika.art", url: "https://pika.art", dailyCredits: 150 },
  { key: "leonardo", name: "Leonardo AI", domain: "leonardo.ai", url: "https://leonardo.ai", dailyCredits: 150 },
];
const FLOW_TOOL = MANUAL_TOOLS[0];
const OTHER_TOOLS = MANUAL_TOOLS.slice(1);

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

const SHOT_OPTIONS: { value: ShotSize; label: string }[] = [
  { value: "Unspecified", label: "Không ghi (để trống)" },
  { value: "ExtremeWide", label: "Toàn cảnh rất rộng" },
  { value: "Wide", label: "Toàn cảnh" },
  { value: "Medium", label: "Trung cảnh" },
  { value: "MediumCloseUp", label: "Cận trung" },
  { value: "CloseUp", label: "Cận cảnh" },
  { value: "ExtremeCloseUp", label: "Đặc tả" },
];

/** Clipboard image writes commonly require a supported bitmap type; PNG is the safest universal fallback. */
async function toPngBlob(blob: Blob): Promise<Blob> {
  const bitmap = await createImageBitmap(blob);
  const canvas = document.createElement("canvas");
  canvas.width = bitmap.width;
  canvas.height = bitmap.height;
  const ctx = canvas.getContext("2d");
  if (!ctx) throw new Error("Canvas 2D context unavailable");
  ctx.drawImage(bitmap, 0, 0);
  return await new Promise<Blob>((resolve, reject) => {
    canvas.toBlob((b) => (b ? resolve(b) : reject(new Error("canvas.toBlob failed"))), "image/png");
  });
}

/**
 * Step 5's single scene/clip list: one card per scene with a media-type
 * selector (Video Fast/Lite, Image, Animation), always-visible narration +
 * copy actions, and an "Advanced" disclosure absorbing what used to be the
 * separate ClipPlanEditor (model tier via the selector itself, camera,
 * skip-generation, prompt edit/suggest, upload). Supersedes the old
 * FlowPlanPanel + ClipPlanEditor pair - there is exactly one scene list now.
 */
export function ScenePlanPanel({
  contentProjectId,
  clips,
  disabled,
  onChanged,
  onPlanLoaded,
  refreshToken,
}: {
  contentProjectId: string;
  clips: WizardClip[];
  disabled: boolean;
  onChanged: () => void;
  /** Lets the parent step's sticky action bar reuse this fetch's counts/copy-all text without a second request. */
  onPlanLoaded?: (plan: FlowGenerationPlan) => void;
  /**
   * Bump this (e.g. a counter) to force a fresh GET of the flow plan without
   * remounting the panel. Needed because per-scene prompt fields
   * (flowVideoPrompt/imagePrompt/isUnprompted) live on the cached `plan`
   * state here, not on the `clips` prop - so a background job that writes
   * prompts server-side (bulk "suggest-all") doesn't otherwise get picked up
   * just because the project overview poll refreshes `clips`.
   */
  refreshToken?: number;
}) {
  const [plan, setPlan] = useState<FlowGenerationPlan | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [copied, setCopied] = useState<string | null>(null);
  const [overrides, setOverrides] = useState<Set<string>>(() => new Set());
  const [busySceneId, setBusySceneId] = useState<string | null>(null);
  const [applyingPreset, setApplyingPreset] = useState(false);
  const [automationStarting, setAutomationStarting] = useState(false);
  const [automationRunState, setAutomationRunState] = useState<AutomationRunState>("idle");
  const [automationScenes, setAutomationScenes] = useState<Record<string, AutomationSceneStatus>>({});
  const [automationResult, setAutomationResult] = useState<AutomationRunResult | null>(null);

  // In-memory prompt cache (see mediaPresets.ts for the sessionStorage-backed
  // fallback) - keyed "sceneId:visualType", populated right before a
  // visual-type switch so a scene switched back to a type it was on earlier
  // in this session gets its prompt back, working around
  // Scene.SetVisualType clearing GenerationPrompt server-side on purpose.
  const promptCacheRef = useRef<Map<string, string>>(new Map());

  // Automation polling (google-flow-runner companion service) - interval id +
  // a guard so a completed run only triggers one refetchPlan()/onChanged(),
  // not one per subsequent poll while we settle on the final status.
  const automationPollRef = useRef<number | null>(null);
  const automationRefetchedRef = useRef(false);

  const stopAutomationPolling = () => {
    if (automationPollRef.current !== null) {
      window.clearInterval(automationPollRef.current);
      automationPollRef.current = null;
    }
  };

  useEffect(() => {
    setOverrides(loadOverrides(contentProjectId));
  }, [contentProjectId]);

  // Reset automation UI state and stop any in-flight polling whenever the
  // project changes, and always clean up the interval on unmount.
  useEffect(() => {
    setAutomationRunState("idle");
    setAutomationScenes({});
    setAutomationResult(null);
    automationRefetchedRef.current = false;
    stopAutomationPolling();
    return () => {
      stopAutomationPolling();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [contentProjectId]);

  const refetchPlan = async () => {
    const p = await flowApi.getPlan(contentProjectId);
    setPlan(p);
    onPlanLoaded?.(p);
    return p;
  };

  useEffect(() => {
    let cancelled = false;
    flowApi
      .getPlan(contentProjectId)
      .then((p) => {
        if (!cancelled) {
          setPlan(p);
          onPlanLoaded?.(p);
        }
      })
      .catch((e) => {
        if (!cancelled) setError(describeApiError(e, "Không tải được kế hoạch cảnh."));
      });
    return () => {
      cancelled = true;
    };
    // onPlanLoaded is a useState setter at the one call site - stable identity.
  }, [contentProjectId, onPlanLoaded]);

  // Skip the very first render so this doesn't duplicate the mount fetch
  // above - only refetch on a genuine bump from the parent (e.g. once its
  // bulk "suggest-all" job's busy state clears).
  const isFirstRefreshRef = useRef(true);
  useEffect(() => {
    if (isFirstRefreshRef.current) {
      isFirstRefreshRef.current = false;
      return;
    }
    refetchPlan().catch((e) => setError(describeApiError(e, "Không tải lại được kế hoạch cảnh.")));
    // refetchPlan is redefined each render but not stable-identified; only the token should trigger this.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [refreshToken]);

  const mergedScenes = useMemo(() => (plan ? mergeScenes(plan, clips) : []), [plan, clips]);

  const markOverride = (sceneId: string) => {
    setOverrides((cur) => {
      const next = new Set(cur);
      next.add(sceneId);
      saveOverrides(contentProjectId, next);
      return next;
    });
  };

  /** Core API sequence for one scene's media-type switch - no busy/error/refetch bookkeeping, so bulk preset apply can call it in a loop without a UI refresh per scene. */
  const switchSceneMedia = async (scene: MergedScene, optionId: MediaOptionId) => {
    const option = optionById(optionId);

    const currentPrompt = scene.generationPrompt?.trim();
    if (currentPrompt) {
      const key = `${scene.sceneId}:${scene.visualTypeRaw}`;
      promptCacheRef.current.set(key, currentPrompt);
      writeCachedPrompt(scene.sceneId, scene.visualTypeRaw, currentPrompt);
    }

    await storyboardApi.setVisualType(contentProjectId, scene.sceneId, option.visualType);
    if (option.visualType === "AiVideo" && option.modelTier) {
      await storyboardApi.setModelTier(contentProjectId, scene.sceneId, option.modelTier);
    }

    const cachedKey = `${scene.sceneId}:${option.visualType}`;
    const cachedPrompt = promptCacheRef.current.get(cachedKey) ?? readCachedPrompt(scene.sceneId, option.visualType);
    if (cachedPrompt) {
      await storyboardApi.setPrompt(contentProjectId, scene.sceneId, cachedPrompt);
    }
  };

  const runSceneOp = async (sceneId: string, fn: () => Promise<unknown>, fallback: string) => {
    setBusySceneId(sceneId);
    setError(null);
    try {
      await fn();
      await refetchPlan();
      onChanged();
    } catch (err) {
      setError(describeApiError(err, fallback));
    } finally {
      setBusySceneId(null);
    }
  };

  const uploadScene = async (scene: MergedScene, file: File) => {
    setBusySceneId(scene.sceneId);
    setError(null);
    try {
      const result = await storyboardApi.uploadSceneVideo(contentProjectId, scene.sceneId, file);
      if (!result.accepted) {
        setError(`Cảnh ${result.sceneNumber}: ${result.issues.join("; ")}`);
      } else if (result.warnings.length) {
        setError(`Cảnh ${result.sceneNumber} (đã nhận, có cảnh báo): ${result.warnings.join("; ")}`);
      }
      await refetchPlan();
      onChanged();
    } catch (err) {
      setError(describeApiError(err, "Không tải lên được clip."));
    } finally {
      setBusySceneId(null);
    }
  };

  const changeSceneMedia = (sceneId: string, optionId: MediaOptionId) => {
    const scene = mergedScenes.find((s) => s.sceneId === sceneId);
    if (!scene) return;
    runSceneOp(
      sceneId,
      async () => {
        await switchSceneMedia(scene, optionId);
        markOverride(sceneId);
      },
      "Không đổi được loại media cho cảnh này.",
    );
  };

  const applyPreset = async (preset: MediaPreset, overridesSnapshot: Set<string> = overrides) => {
    setApplyingPreset(true);
    setError(null);
    try {
      const ordered = mergedScenes;
      for (let i = 0; i < ordered.length; i++) {
        const scene = ordered[i];
        if (overridesSnapshot.has(scene.sceneId)) continue;
        const rule = ruleForIndex(preset, i);
        if (!rule) continue;
        const option = optionForRule(rule);
        if (!option) continue;
        const currentOptionId = optionIdForScene(scene.visualTypeRaw, scene.modelTier);
        if (currentOptionId === option.id) continue;
        await switchSceneMedia(scene, option.id);
      }
      await refetchPlan();
      onChanged();
    } catch (err) {
      setError(describeApiError(err, "Không áp dụng được preset cho toàn bộ cảnh."));
    } finally {
      setApplyingPreset(false);
    }
  };

  const resetOverrides = async (preset: MediaPreset) => {
    const empty = new Set<string>();
    setOverrides(empty);
    saveOverrides(contentProjectId, empty);
    await applyPreset(preset, empty);
  };

  const flash = (key: string) => {
    setCopied(key);
    window.setTimeout(() => setCopied((c) => (c === key ? null : c)), 1500);
  };

  const copyText = async (key: string, text: string) => {
    try {
      await navigator.clipboard.writeText(text);
      flash(key);
    } catch {
      setError("Trình duyệt chặn copy - hãy chọn và copy thủ công.");
    }
  };

  const copyImage = async (key: string, url: string) => {
    try {
      const res = await fetch(apiUrl(url));
      const blob = await res.blob();
      try {
        await navigator.clipboard.write([new ClipboardItem({ [blob.type]: blob })]);
      } catch {
        const pngBlob = await toPngBlob(blob);
        await navigator.clipboard.write([new ClipboardItem({ "image/png": pngBlob })]);
      }
      flash(key);
    } catch {
      try {
        await navigator.clipboard.writeText(apiUrl(url));
        const message =
          "Trình duyệt không cho copy ảnh trực tiếp - đã copy LINK ảnh dạng chữ, không phải ảnh thật. " +
          "Dán link này vào Google Flow sẽ KHÔNG đính kèm được ảnh. Hãy mở link, tải ảnh về, rồi tải lên Flow thủ công.";
        window.alert(message);
        setError(message);
      } catch {
        setError("Không copy được ảnh.");
      }
    }
  };

  /** One GET /status poll. Stops itself and surfaces an error on a real network failure or a terminal runState. */
  const pollAutomationStatus = async () => {
    let res: Response;
    try {
      res = await fetch(`${AUTOMATION_SERVICE_URL}/status?project=${encodeURIComponent(contentProjectId)}`);
    } catch {
      stopAutomationPolling();
      setError(
        "Mất kết nối tới automation service cục bộ trong khi đang theo dõi tiến trình (http://127.0.0.1:4545). " +
          "Automation có thể vẫn đang chạy trong terminal `npm run serve` - hãy kiểm tra ở đó, hoặc dùng cách copy prompt thủ công ở trên.",
      );
      return;
    }

    if (!res.ok) {
      stopAutomationPolling();
      setError(`Automation service trả về lỗi khi theo dõi tiến trình (HTTP ${res.status}).`);
      return;
    }

    const data = (await res.json()) as AutomationStatusResponse;
    setAutomationScenes(data.scenes);
    setAutomationRunState(data.runState);
    setAutomationResult(data.result);

    if (data.runState === "completed") {
      stopAutomationPolling();
      if (!automationRefetchedRef.current && data.result && data.result.completed > 0) {
        automationRefetchedRef.current = true;
        await refetchPlan();
        onChanged();
      }
    } else if (data.runState === "failed") {
      stopAutomationPolling();
      if (data.error) setError(data.error);
    }
  };

  const startAutomationPolling = () => {
    stopAutomationPolling();
    void pollAutomationStatus();
    automationPollRef.current = window.setInterval(() => {
      void pollAutomationStatus();
    }, 3000);
  };

  const runAutomation = async () => {
    setError(null);
    setAutomationStarting(true);
    try {
      const controller = new AbortController();
      const timeoutId = window.setTimeout(() => controller.abort(), 2000);
      let healthy = false;
      try {
        const healthRes = await fetch(`${AUTOMATION_SERVICE_URL}/health`, { signal: controller.signal });
        healthy = healthRes.ok;
      } catch {
        healthy = false;
      } finally {
        window.clearTimeout(timeoutId);
      }

      if (!healthy) {
        setError(
          "Không kết nối được tới automation service cục bộ (http://127.0.0.1:4545). Hãy chạy `npm run serve` " +
            "trong thư mục automation/google-flow-runner trước, hoặc dùng cách copy prompt thủ công ở trên.",
        );
        return;
      }

      let runRes: Response;
      try {
        runRes = await fetch(`${AUTOMATION_SERVICE_URL}/run`, {
          method: "POST",
          headers: { "Content-Type": "application/json", "X-Google-Flow-Runner-UI": "1" },
          // baseUrl: the exact API this web app itself is talking to (VITE_API_BASE_URL,
          // defaults to the docker-compose port) - avoids the companion service falling
          // back to its own default (the `dotnet run` dev port), which is wrong whenever
          // this app is pointed at docker or any other deployment.
          body: JSON.stringify({ projectId: contentProjectId, baseUrl: API_BASE_URL }),
        });
      } catch {
        setError(
          "Không gọi được automation service cục bộ để bắt đầu chạy. Hãy kiểm tra `npm run serve` vẫn đang chạy, " +
            "hoặc dùng cách copy prompt thủ công ở trên.",
        );
        return;
      }

      // 202 (started) and 409 (already running for this project) both mean a run is active now - start polling either way.
      if (runRes.status !== 202 && runRes.status !== 409) {
        let detail = "";
        try {
          const body = (await runRes.json()) as { error?: string };
          if (body?.error) detail = `: ${body.error}`;
        } catch {
          // ignore unparsable body
        }
        setError(`Không bắt đầu được automation Google Flow (HTTP ${runRes.status})${detail}.`);
        return;
      }

      automationRefetchedRef.current = false;
      setAutomationResult(null);
      setAutomationRunState("running");
      startAutomationPolling();
    } finally {
      setAutomationStarting(false);
    }
  };

  if (error && !plan) return <ErrorMessage message={error} />;
  if (!plan) return <Loading label="Đang lập kế hoạch cảnh..." />;

  const videoCount = mergedScenes.filter((s) => s.generationType === "AI_VIDEO" && !s.skipGeneration).length;
  const imageCount = mergedScenes.filter((s) => s.generationType === "AI_IMAGE" && !s.skipGeneration).length;
  const motionCount = mergedScenes.filter((s) => s.generationType === "STATIC" && !s.skipGeneration).length;
  const readyCount = mergedScenes.filter((s) => s.skipGeneration).length;

  return (
    <>
      <div className="wz-plan-summary">
        <span>
          <strong>{videoCount}</strong> cảnh Video AI
        </span>
        <span>
          <strong>{imageCount}</strong> cảnh Ảnh tĩnh
        </span>
        {motionCount > 0 && (
          <span>
            <strong>{motionCount}</strong> cảnh Animation
          </span>
        )}
        {readyCount > 0 && (
          <span>
            <strong>{readyCount}</strong> đã có clip
          </span>
        )}
      </div>

      <section className="wz-hero">
        <h2>🎬 Miễn phí với Google Flow</h2>
        <p className="wz-sub">
          Copy prompt đã soạn sẵn cho từng cảnh, dán vào Google Flow để tự tạo clip - $0. Xong thì quay lại{" "}
          <strong>Bước 6</strong> để import và ghép video.
        </p>
        <p className="wz-hint">
          Ước tính mức dùng ngân sách AI chung của tài khoản (Veo + ảnh + Flow):{" "}
          <strong>
            {plan.usedCredits}/{plan.dailyBudgetCredits}
          </strong>{" "}
          · kế hoạch cho các cảnh dưới đây ước tính <strong>{plan.plannedCredits}</strong> credit. Đây <em>không</em>{" "}
          phải số dư thực tế trên labs.google/flow - ứng dụng không kết nối tới tài khoản Google Flow của bạn nên
          không biết số dư thật.
        </p>
        {!plan.withinBudget && (
          <p className="wz-error" style={{ marginTop: 8 }}>
            ⚠️ Kế hoạch ước tính vượt ngân sách AI còn lại hôm nay ({plan.remainingCredits} credit). Đây chỉ ảnh hưởng
            tới các bước app tự tạo (Veo) - tự tạo clip trong Google Flow vẫn hoàn toàn miễn phí và không bị chặn.
          </p>
        )}
        <div className="wz-actions" style={{ marginTop: 12 }}>
          <a className="wz-btn" href={FLOW_TOOL.url} target="_blank" rel="noreferrer">
            Mở Google Flow ↗
          </a>
        </div>

        <details style={{ marginTop: 12 }}>
          <summary className="wz-hint" style={{ cursor: "pointer", userSelect: "none" }}>
            ⚙️ Tự động hoá Google Flow (nâng cao)
          </summary>
          <div style={{ marginTop: 8, display: "flex", flexDirection: "column", gap: 6 }}>
            <p className="wz-hint" style={{ margin: 0 }}>
              Có một công cụ chạy trên máy bạn (Playwright, dùng trình duyệt đã đăng nhập của bạn) có thể đọc prompt
              từng cảnh, tự dán vào Google Flow, tải clip về và import lại vào dự án này. Cách thủ công (Copy Prompt ở
              trên) vẫn là cách chính - đây chỉ là lựa chọn nâng cao. Cần chạy công cụ đồng hành một lần trên máy bạn
              trước (<code>npm run serve</code> trong thư mục <code>automation/google-flow-runner</code>).
            </p>

            <div style={{ display: "flex", gap: 8, alignItems: "center", flexWrap: "wrap" }}>
              <button
                type="button"
                className="wz-btn wz-btn-sm wz-btn-primary"
                disabled={automationStarting || automationRunState === "running"}
                onClick={runAutomation}
              >
                {automationRunState === "running"
                  ? "⏳ Đang chạy tự động hoá..."
                  : automationStarting
                    ? "Đang kiểm tra kết nối..."
                    : "▶ Chạy tự động hoá Google Flow"}
              </button>
              <CostNote kind="free">dùng credit Google Flow của chính bạn - app không tính phí</CostNote>
            </div>

            {automationRunState !== "idle" && (
              <p className="wz-hint" style={{ margin: 0 }}>
                {automationRunState === "running" &&
                  "Đang chạy - theo dõi tiến trình từng cảnh ở nhãn trạng thái trên mỗi thẻ cảnh bên dưới."}
                {automationRunState === "completed" &&
                  (automationResult
                    ? `Hoàn tất: ${automationResult.completed} xong, ${automationResult.skipped} đã có sẵn, ${automationResult.failed} lỗi${
                        automationResult.failedScenes.length > 0
                          ? ` (cảnh ${automationResult.failedScenes.join(", ")})`
                          : ""
                      }.`
                    : "Hoàn tất.")}
                {automationRunState === "failed" && "Automation dừng vì lỗi - xem chi tiết ở thông báo lỗi phía trên."}
              </p>
            )}
          </div>
        </details>
      </section>

      <ErrorMessage message={error} />

      <MediaPresetPanel
        scenes={mergedScenes}
        overrides={overrides}
        disabled={disabled}
        applying={applyingPreset}
        onApplyPreset={(preset) => applyPreset(preset)}
        onResetOverrides={resetOverrides}
      />

      <h3>Danh sách cảnh</h3>
      {mergedScenes.map((scene, index) => (
        <SceneCard
          key={scene.sceneId}
          scene={scene}
          previousScene={index > 0 ? mergedScenes[index - 1] : null}
          overridden={overrides.has(scene.sceneId)}
          disabled={disabled || applyingPreset || busySceneId !== null}
          busy={busySceneId === scene.sceneId}
          copied={copied}
          flowUsage={{ used: plan.usedCredits, remaining: plan.remainingCredits, budget: plan.dailyBudgetCredits }}
          automationStatus={automationScenes[String(scene.sceneNumber)]}
          onChangeMedia={(optionId) => changeSceneMedia(scene.sceneId, optionId)}
          onCopyText={copyText}
          onCopyImage={copyImage}
          onSetCamera={(cam) =>
            runSceneOp(
              scene.sceneId,
              () => storyboardApi.setCameraMovement(contentProjectId, scene.sceneId, cam),
              "Không đổi được máy quay.",
            )
          }
          onSetShot={(shot) =>
            runSceneOp(
              scene.sceneId,
              () => storyboardApi.setShotSize(contentProjectId, scene.sceneId, shot),
              "Không đổi được cỡ cảnh.",
            )
          }
          onSetCharacterOnScreen={(value) =>
            runSceneOp(
              scene.sceneId,
              () => storyboardApi.setCharacterOnScreen(contentProjectId, scene.sceneId, value),
              "Không đổi được lựa chọn nhân vật.",
            )
          }
          onSetSkip={(skip) =>
            runSceneOp(
              scene.sceneId,
              () => storyboardApi.setSkipGeneration(contentProjectId, scene.sceneId, skip),
              "Không đổi được trạng thái bỏ qua.",
            )
          }
          onSuggestPrompt={() =>
            runSceneOp(
              scene.sceneId,
              () => storyboardApi.suggestPrompt(contentProjectId, scene.sceneId),
              "Không tạo được gợi ý prompt.",
            )
          }
          onSavePrompt={(prompt) =>
            runSceneOp(
              scene.sceneId,
              () => storyboardApi.setPrompt(contentProjectId, scene.sceneId, prompt),
              "Không lưu được prompt.",
            )
          }
          onUpload={(file) => uploadScene(scene, file)}
          onImportStock={(videoId) =>
            runSceneOp(
              scene.sceneId,
              async () => {
                const result = await storyboardApi.importStockVideo(contentProjectId, scene.sceneId, videoId);
                if (!result.accepted) throw new Error(`Video không hợp lệ: ${result.issues.join("; ")}`);
              },
              "Không nhập được video stock.",
            )
          }
          onUploadFirstFrame={(file) =>
            runSceneOp(
              scene.sceneId,
              () => storyboardApi.uploadFirstFrame(contentProjectId, scene.sceneId, file),
              "Không tải lên được ảnh khung đầu.",
            )
          }
          onUsePreviousLastFrame={() =>
            runSceneOp(
              scene.sceneId,
              () => storyboardApi.usePreviousClipLastFrame(contentProjectId, scene.sceneId),
              "Không lấy được khung cuối của cảnh trước.",
            )
          }
          onGenerateKeyframe={() =>
            runSceneOp(
              scene.sceneId,
              () => storyboardApi.generateKeyframe(contentProjectId, scene.sceneId),
              "Không tạo được Keyframe.",
            )
          }
          onApproveKeyframe={() =>
            runSceneOp(
              scene.sceneId,
              () => storyboardApi.approveKeyframe(contentProjectId, scene.sceneId),
              "Không duyệt được Keyframe.",
            )
          }
          onSaveMotionPrompt={(motionPrompt) =>
            runSceneOp(
              scene.sceneId,
              () => storyboardApi.setMotionPrompt(contentProjectId, scene.sceneId, motionPrompt),
              "Không lưu được motion prompt.",
            )
          }
          onGenerateVideoFromKeyframe={() =>
            runSceneOp(
              scene.sceneId,
              () => storyboardApi.generateVideoFromKeyframe(contentProjectId, scene.sceneId),
              "Không tạo được video từ Keyframe.",
            )
          }
        />
      ))}
    </>
  );
}

// ---------------------------------------------------------------------------

function SceneCard({
  scene,
  previousScene,
  overridden,
  disabled,
  busy,
  copied,
  flowUsage,
  automationStatus,
  onChangeMedia,
  onCopyText,
  onCopyImage,
  onSetCamera,
  onSetShot,
  onSetCharacterOnScreen,
  onSetSkip,
  onSuggestPrompt,
  onSavePrompt,
  onUpload,
  onUploadFirstFrame,
  onUsePreviousLastFrame,
  onImportStock,
  onGenerateKeyframe,
  onApproveKeyframe,
  onSaveMotionPrompt,
  onGenerateVideoFromKeyframe,
}: {
  scene: MergedScene;
  /** The scene right before this one (null for the first) - for "continue from its last frame". */
  previousScene: MergedScene | null;
  overridden: boolean;
  disabled: boolean;
  busy: boolean;
  copied: string | null;
  flowUsage: { used: number; remaining: number; budget: number };
  /** This scene's live google-flow-runner automation status, if a run has reported one (keyed by sceneNumber upstream). */
  automationStatus?: { state: string; message?: string };
  onChangeMedia: (optionId: MediaOptionId) => void;
  onCopyText: (key: string, text: string) => void;
  onCopyImage: (key: string, url: string) => void;
  onSetCamera: (cam: CameraMovement) => void;
  onSetShot: (shot: ShotSize) => void;
  onSetCharacterOnScreen: (value: boolean | null) => void;
  onSetSkip: (skip: boolean) => void;
  onSuggestPrompt: () => void;
  onSavePrompt: (prompt: string) => void;
  onUpload: (file: File) => void;
  onUploadFirstFrame: (file: File) => void;
  onImportStock: (videoId: string) => void;
  onUsePreviousLastFrame: () => void;
  onGenerateKeyframe: () => void;
  onApproveKeyframe: () => void;
  onSaveMotionPrompt: (motionPrompt: string) => void;
  onGenerateVideoFromKeyframe: () => void;
}) {
  const [expanded, setExpanded] = useState(false);
  const [promptDraft, setPromptDraft] = useState(scene.generationPrompt ?? "");
  const [motionDraft, setMotionDraft] = useState(scene.motionPrompt);

  useEffect(() => {
    setPromptDraft(scene.generationPrompt ?? "");
  }, [scene.generationPrompt]);

  useEffect(() => {
    setMotionDraft(scene.motionPrompt);
  }, [scene.motionPrompt]);

  const promptDirty = promptDraft.trim() !== (scene.generationPrompt ?? "").trim();
  const motionPromptDirty = motionDraft.trim() !== scene.motionPrompt.trim();

  const optionId = optionIdForScene(scene.visualTypeRaw, scene.modelTier);
  const isVideo = optionId === "videoFast" || optionId === "videoLite";
  const isMotion = optionId === "motion";
  const kind = isVideo ? "video" : isMotion ? "motion" : "image";

  const promptKey = `${scene.sceneId}-prompt`;
  const refKey = `${scene.sceneId}-ref`;
  const envRefKey = `${scene.sceneId}-env-ref`;
  // The header "Copy Prompt" always copies the fully-composed, ready-to-paste
  // text for the scene's current type - flowVideoPrompt (video/motion action
  // description) or imagePrompt (still image) - both already incorporate any
  // hand-edited prompt below (see FlowGenerationPlanService.ResolveAction).
  const primaryPromptText = kind === "image" ? scene.imagePrompt : scene.flowVideoPrompt;
  // isUnprompted only ever empties flowVideoPrompt (video/motion) - ComposeImagePrompt
  // always falls back to narration, so an AI_IMAGE scene's imagePrompt is never
  // emptied by this flag. The `!primaryPromptText` half of the check below stays
  // general so any other genuinely-empty prompt still disables the button.
  const isUnpromptedVideo = kind !== "image" && scene.isUnprompted;
  const nothingToCopy = !primaryPromptText;

  return (
    <div
      className="wz-scene-card"
      data-kind={kind}
      data-testid="scene-card"
      data-scene-index={scene.sceneNumber}
      data-media-type={scene.generationType}
    >
      <div className="wz-scene-head">
        <strong style={{ fontSize: 13 }}>Cảnh {scene.sceneNumber}</strong>
        <span className="wz-badge">{scene.durationSeconds}s</span>

        <select
          className="wz-media-select"
          data-testid="scene-media-type"
          data-scene-index={scene.sceneNumber}
          value={optionId}
          disabled={disabled}
          onChange={(e) => onChangeMedia(e.target.value as MediaOptionId)}
        >
          {MEDIA_OPTIONS.map((o) => (
            <option key={o.id} value={o.id}>
              {o.label}
            </option>
          ))}
        </select>

        <span
          data-testid="scene-automation-status"
          data-scene-index={scene.sceneNumber}
          data-status={automationStatus?.state ?? "idle"}
          className="wz-badge"
          hidden={!automationStatus}
          title={automationStatus?.state === "FAILED" ? automationStatus.message : undefined}
        >
          {automationStatus ? AUTOMATION_STATE_LABELS[automationStatus.state] ?? automationStatus.state : null}
        </span>

        {overridden && (
          <span className="wz-override-tag" title="Đã đổi thủ công - Áp dụng preset sẽ không ghi đè cảnh này">
            Override
          </span>
        )}

        {scene.estimatedCredits > 0 && <span className="wz-badge">{scene.estimatedCredits} cr</span>}

        {isUnpromptedVideo && (
          <StatusBadge
            tone="danger"
            title="Cảnh chưa có prompt AI - mở 'Nâng cao ▾' rồi bấm 'Tạo gợi ý' trước khi copy."
          >
            ⚠️ chưa có prompt
          </StatusBadge>
        )}

        {isVideo &&
          (scene.recommendFreeTool ? (
            <StatusBadge
              tone="success"
              title="Không phát hiện nhân vật xuất hiện trên khung hình - dùng AI video free thoải mái."
            >
              ✅ free an toàn
            </StatusBadge>
          ) : (
            <StatusBadge
              tone="warning"
              title="Nhân vật xuất hiện/hành động ở cảnh này - nên dùng Ảnh mẫu để giữ liền mạch câu chuyện."
            >
              🎭 cần Ảnh mẫu
            </StatusBadge>
          ))}
      </div>

      <p className="wz-scene-desc">{scene.narration || <em>(không có lời)</em>}</p>

      <div className="wz-actions" style={{ marginTop: 0 }}>
        <button
          type="button"
          className="wz-btn wz-btn-sm wz-btn-primary"
          data-testid="scene-copy-prompt"
          data-scene-index={scene.sceneNumber}
          disabled={busy || nothingToCopy}
          title={
            isUnpromptedVideo
              ? "Cảnh chưa có prompt - mở 'Nâng cao ▾' rồi bấm 'Tạo gợi ý' trước khi copy."
              : nothingToCopy
                ? "Chưa có nội dung prompt để copy."
                : undefined
          }
          onClick={() => onCopyText(promptKey, primaryPromptText)}
        >
          {copied === promptKey ? "Đã copy ✓" : "Copy Prompt"}
        </button>
        {scene.characterReferenceImages.length > 0
          ? scene.characterReferenceImages.map((c) => {
              const charKey = `${scene.sceneId}-char-${c.name}`;
              return (
                <button
                  key={charKey}
                  type="button"
                  className="wz-btn wz-btn-sm"
                  disabled={busy}
                  onClick={() => onCopyImage(charKey, c.url)}
                >
                  {copied === charKey ? "Đã copy ✓" : `Copy ${c.name} Ref`}
                </button>
              );
            })
          : scene.characterReferenceImageUrl && (
              <button
                type="button"
                className="wz-btn wz-btn-sm"
                disabled={busy}
                onClick={() => onCopyImage(refKey, scene.characterReferenceImageUrl!)}
              >
                {copied === refKey ? "Đã copy ✓" : "Copy Character Ref"}
              </button>
            )}
        {scene.environmentReferenceImageUrl && (
          <button
            type="button"
            className="wz-btn wz-btn-sm"
            disabled={busy}
            onClick={() => onCopyImage(envRefKey, scene.environmentReferenceImageUrl!)}
          >
            {copied === envRefKey ? "Đã copy ✓" : "Copy Environment Ref"}
          </button>
        )}
        <button type="button" className="wz-btn wz-btn-sm" onClick={() => setExpanded((v) => !v)}>
          {expanded ? "Thu gọn ▴" : "Nâng cao ▾"}
        </button>
      </div>

      {expanded && (
        <div style={{ marginTop: 10, display: "flex", flexDirection: "column", gap: 8 }}>
          {isMotion && (
            <p className="wz-hint" style={{ margin: 0, color: "#b8860b" }}>
              🪄 Loại "Animation / Motion" chỉ hỗ trợ đường thủ công (tự tạo ở ngoài rồi tải lên) - ứng dụng không tự
              gọi Veo cho loại này.
            </p>
          )}

          {isVideo && (
            <div
              data-testid="scene-flow-two-step"
              style={{ border: "1px solid var(--border)", borderRadius: 6, padding: 8, display: "flex", flexDirection: "column", gap: 8 }}
            >
              <div style={{ display: "flex", gap: 8, alignItems: "center", flexWrap: "wrap" }}>
                <strong style={{ fontSize: 13 }}>🎞️ Google Flow 2 bước: ảnh khung đầu → video</strong>
                <CostNote kind="free">dùng credit Google Flow của bạn</CostNote>
              </div>
              <p className="wz-hint" style={{ margin: 0 }}>
                Nên dùng cho cảnh có nhân vật: khóa đúng nhân vật, bối cảnh, ánh sáng trong một ảnh trước, rồi mới cho
                chuyển động - video ít bị "bịa" hơn nhiều so với tạo thẳng từ chữ.
              </p>
              <ol className="wz-hint" style={{ margin: 0, paddingLeft: 18 }}>
                <li>Copy <strong>prompt ảnh khung đầu</strong> (kèm ảnh nhân vật ở trên) → trong Flow tạo <em>ảnh</em>.</li>
                <li>Tải ảnh đó lên đây - hoặc dùng khung cuối của cảnh trước để hai clip nối liền.</li>
                <li>
                  Trong Flow chọn <em>Frames to Video</em>, đính kèm ảnh khung đầu, dán <strong>prompt chuyển động</strong>.
                </li>
                <li>Tải clip về rồi bấm "Tải clip có sẵn (.mp4)" bên dưới.</li>
              </ol>

              <div className="wz-actions" style={{ marginTop: 0 }}>
                <button
                  type="button"
                  className="wz-btn wz-btn-sm"
                  disabled={busy || !scene.firstFramePrompt}
                  title={scene.firstFramePrompt ? undefined : "Cảnh chưa có prompt - bấm 'Tạo gợi ý' trước."}
                  onClick={() => onCopyText(`${scene.sceneId}-first-frame`, scene.firstFramePrompt)}
                >
                  {copied === `${scene.sceneId}-first-frame` ? "Đã copy ✓" : "1. Copy prompt ảnh khung đầu"}
                </button>
                <label
                  className="wz-btn wz-btn-sm"
                  style={{ cursor: disabled || scene.skipGeneration ? "not-allowed" : "pointer", opacity: disabled || scene.skipGeneration ? 0.5 : 1 }}
                  title={scene.skipGeneration ? "Cảnh đã có clip - bỏ chọn 'Đã có video' trước." : undefined}
                >
                  2. Tải ảnh khung đầu lên
                  <input
                    type="file"
                    accept="image/png,image/jpeg"
                    hidden
                    disabled={disabled || scene.skipGeneration}
                    onChange={(e) => {
                      const f = e.target.files?.[0];
                      if (f) onUploadFirstFrame(f);
                      e.target.value = "";
                    }}
                  />
                </label>
                {previousScene && (
                  <button
                    type="button"
                    className="wz-btn wz-btn-sm"
                    disabled={disabled || scene.skipGeneration || !previousScene.hasExistingVideo}
                    title={
                      previousScene.hasExistingVideo
                        ? `Lấy khung hình cuối của clip cảnh ${previousScene.sceneNumber} (xử lý trên máy, không gọi AI).`
                        : `Cảnh ${previousScene.sceneNumber} chưa có clip video.`
                    }
                    onClick={() => {
                      if (
                        !scene.keyframeImageUrl ||
                        window.confirm("Thay ảnh khung đầu hiện tại bằng khung cuối của cảnh trước?")
                      ) {
                        onUsePreviousLastFrame();
                      }
                    }}
                  >
                    …hoặc dùng khung cuối cảnh {previousScene.sceneNumber}
                  </button>
                )}
              </div>

              {scene.keyframeImageUrl && (
                <div style={{ display: "flex", gap: 10, alignItems: "flex-start", flexWrap: "wrap" }}>
                  <img
                    src={apiUrl(scene.keyframeImageUrl)}
                    alt="Ảnh khung đầu"
                    style={{ width: 110, aspectRatio: "9 / 16", objectFit: "cover", borderRadius: 6, border: "1px solid var(--border)" }}
                  />
                  <div className="wz-actions" style={{ marginTop: 0, flexDirection: "column", alignItems: "flex-start" }}>
                    <a className="wz-btn wz-btn-sm" href={apiUrl(scene.keyframeImageUrl)} target="_blank" rel="noreferrer" download>
                      Mở / tải ảnh khung đầu ↗
                    </a>
                    <button
                      type="button"
                      className="wz-btn wz-btn-sm wz-btn-primary"
                      disabled={busy || !scene.motionPrompt}
                      onClick={() => onCopyText(`${scene.sceneId}-motion`, scene.motionPrompt)}
                    >
                      {copied === `${scene.sceneId}-motion` ? "Đã copy ✓" : "3. Copy prompt chuyển động"}
                    </button>
                  </div>
                </div>
              )}
            </div>
          )}

          {isVideo && (
            <div style={{ border: "1px solid var(--border)", borderRadius: 6, padding: 8, display: "flex", flexDirection: "column", gap: 8 }}>
              <strong style={{ fontSize: 13 }}>🖼️ Keyframe → Video (tùy chọn, tạo trực tiếp trong app)</strong>
              <p className="wz-hint" style={{ margin: 0 }}>
                Tạo ảnh mẫu (Keyframe) cho cảnh này, duyệt xong mới tạo video 8 giây từ ảnh đó - giữ đúng nhân vật/bối
                cảnh hơn là để Veo tự bịa. Khác với "Copy Prompt" ở trên (dán tay vào Google Flow): đây là ứng dụng tự
                gọi API để tạo, sẽ tốn chi phí AI.
              </p>

              {scene.keyframeImageUrl && (
                <img
                  src={apiUrl(scene.keyframeImageUrl)}
                  alt="Keyframe"
                  style={{ width: 140, height: 140, objectFit: "cover", borderRadius: 6, border: "1px solid var(--border)" }}
                />
              )}

              <div className="wz-actions" style={{ marginTop: 0 }}>
                {(scene.keyframeStatus === "None" || scene.keyframeStatus === "Failed") && (
                  <button type="button" className="wz-btn wz-btn-sm" disabled={busy} onClick={onGenerateKeyframe}>
                    {busy ? "Đang tạo..." : "Tạo Keyframe"}
                  </button>
                )}
                {scene.keyframeStatus === "Generating" && <StatusBadge tone="working">Đang tạo Keyframe...</StatusBadge>}
                {scene.keyframeStatus === "Generated" && (
                  <>
                    <button type="button" className="wz-btn wz-btn-sm wz-btn-primary" disabled={busy} onClick={onApproveKeyframe}>
                      Duyệt Keyframe
                    </button>
                    <button type="button" className="wz-btn wz-btn-sm" disabled={busy} onClick={onGenerateKeyframe}>
                      Tạo lại
                    </button>
                  </>
                )}
                {scene.keyframeStatus === "Approved" && (
                  <>
                    <StatusBadge tone="success">Đã duyệt Keyframe</StatusBadge>
                    <button
                      type="button"
                      className="wz-btn wz-btn-sm"
                      disabled={busy}
                      onClick={() => {
                        if (window.confirm("Tạo lại Keyframe sẽ thay thế ảnh đã duyệt và cần duyệt lại. Tiếp tục?")) {
                          onGenerateKeyframe();
                        }
                      }}
                    >
                      Tạo lại
                    </button>
                  </>
                )}
                <CostNote kind="image" />
              </div>

              {scene.keyframeStatus === "Failed" && <ErrorMessage message="Tạo Keyframe thất bại - thử lại." />}

              {scene.keyframeApproved && (
                <>
                  <label className="wz-field" style={{ marginBottom: 0 }}>
                    <span>Prompt chuyển động (motion prompt) - trống = hệ thống tự soạn khi tạo video</span>
                    <textarea
                      rows={3}
                      value={motionDraft}
                      onChange={(e) => setMotionDraft(e.target.value)}
                      disabled={disabled}
                    />
                  </label>
                  <div className="wz-actions" style={{ marginTop: 0 }}>
                    <button
                      type="button"
                      className="wz-btn wz-btn-sm"
                      disabled={disabled || !motionPromptDirty}
                      onClick={() => onSaveMotionPrompt(motionDraft.trim())}
                    >
                      Lưu motion prompt
                    </button>
                    <button
                      type="button"
                      className="wz-btn wz-btn-sm wz-btn-primary"
                      disabled={busy}
                      onClick={onGenerateVideoFromKeyframe}
                    >
                      {busy ? "Đang tạo video..." : "🎬 Tạo video 8 giây từ Keyframe"}
                    </button>
                    <CostNote kind="video" />
                  </div>
                  {scene.previewUrl && scene.visualTypeRaw === "AiVideo" && (
                    <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
                      <span className="wz-hint" style={{ margin: 0 }}>
                        Video hiện tại của cảnh (có thể là video tạo trước đó, không nhất thiết vừa tạo từ Keyframe này):
                      </span>
                      <video
                        controls
                        preload="metadata"
                        src={apiUrl(scene.previewUrl)}
                        style={{ width: 200, borderRadius: 6, border: "1px solid var(--border)" }}
                      />
                    </div>
                  )}
                </>
              )}
            </div>
          )}

          {isUnpromptedVideo ? (
            <p
              className="wz-hint"
              data-testid="scene-unprompted-warning"
              data-scene-index={scene.sceneNumber}
              style={{ margin: 0, color: "#b8860b", fontWeight: 600 }}
            >
              ⚠️ Cảnh này chưa có prompt AI - bấm nút "Tạo gợi ý" bên dưới trước khi copy.
            </p>
          ) : (
            <label className="wz-field" style={{ marginBottom: 0 }}>
              <span>{kind === "image" ? "Prompt ảnh (đã soạn sẵn)" : "Prompt video/motion (đã soạn sẵn)"}</span>
              <textarea
                readOnly
                rows={4}
                value={primaryPromptText}
                data-testid="scene-prompt"
                data-scene-index={scene.sceneNumber}
                style={{ width: "100%", boxSizing: "border-box" }}
              />
            </label>
          )}

          {isMotion && scene.imagePrompt !== primaryPromptText && (
            <label className="wz-field" style={{ marginBottom: 0 }}>
              <span>Prompt ảnh nền (base image)</span>
              <div style={{ display: "flex", gap: 8 }}>
                <textarea readOnly rows={3} value={scene.imagePrompt} style={{ width: "100%", boxSizing: "border-box" }} />
                <button
                  type="button"
                  className="wz-btn wz-btn-sm"
                  disabled={busy}
                  onClick={() => onCopyText(`${scene.sceneId}-image`, scene.imagePrompt)}
                >
                  {copied === `${scene.sceneId}-image` ? "✓" : "Copy"}
                </button>
              </div>
            </label>
          )}

          <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
            <label className="wz-field" style={{ marginBottom: 0, flex: "1 1 180px" }}>
              <span>Cỡ cảnh (khung hình)</span>
              <select value={scene.shotSize} disabled={disabled} onChange={(e) => onSetShot(e.target.value as ShotSize)}>
                {SHOT_OPTIONS.map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.label}
                  </option>
                ))}
              </select>
            </label>
            <label className="wz-field" style={{ marginBottom: 0, flex: "1 1 220px" }}>
              <span>Nhân vật chính có trong khung hình?</span>
              <select
                value={scene.characterOnScreen === null ? "auto" : scene.characterOnScreen ? "yes" : "no"}
                disabled={disabled}
                onChange={(e) => onSetCharacterOnScreen(e.target.value === "auto" ? null : e.target.value === "yes")}
              >
                <option value="auto">Tự động{scene.characterOnScreen === null ? ` (hiện: ${scene.characterRequired ? "có" : "không"})` : ""}</option>
                <option value="yes">Có - gắn ảnh tham chiếu nhân vật</option>
                <option value="no">Không - không gắn ảnh nhân vật</option>
              </select>
            </label>
          </div>

          {isVideo && (
            <label className="wz-field" style={{ marginBottom: 0 }}>
              <span>Chuyển động máy quay</span>
              <select
                value={scene.cameraMovement}
                disabled={disabled}
                onChange={(e) => onSetCamera(e.target.value as CameraMovement)}
              >
                {CAMERA_OPTIONS.map((c) => (
                  <option key={c.value} value={c.value}>
                    {c.label}
                  </option>
                ))}
              </select>
            </label>
          )}

          {(scene.characterReferenceImageUrl ||
            scene.characterReferenceImages.length > 0 ||
            scene.environmentReferenceImageUrl ||
            scene.referenceImageUrls.length > 0) && (
            <div style={{ display: "flex", gap: 8, alignItems: "center", flexWrap: "wrap" }}>
              {scene.referenceImageUrls.map((u) => (
                <img key={u} src={apiUrl(u)} alt="ref" className="wz-scene-ref" />
              ))}
            </div>
          )}

          <label style={{ display: "block", fontSize: 13, margin: 0 }}>
            <input type="checkbox" checked={scene.skipGeneration} disabled={disabled} onChange={(e) => onSetSkip(e.target.checked)} />{" "}
            Đã có video / Bỏ qua Veo API
            {scene.skipGeneration && " — cảnh này sẽ không được tạo và không tính chi phí"}
          </label>

          {scene.allocationRationale && <p className="wz-hint" style={{ margin: 0 }}>ⓘ {scene.allocationRationale}</p>}

          <label className="wz-field" style={{ marginBottom: 0 }}>
            <span>Prompt hành động (chỉnh được - trống = hệ thống tự viết khi dựng)</span>
            <textarea
              rows={3}
              value={promptDraft}
              onChange={(e) => setPromptDraft(e.target.value)}
              disabled={disabled}
            />
          </label>

          <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
            <button type="button" className="wz-btn wz-btn-sm" disabled={disabled} onClick={onSuggestPrompt}>
              {busy ? "Đang xử lý..." : "Tạo gợi ý"}
            </button>
            <CostNote kind="text">Gợi ý prompt bằng AI</CostNote>
            <button
              type="button"
              className="wz-btn wz-btn-sm wz-btn-primary"
              disabled={disabled || !promptDirty}
              onClick={() => onSavePrompt(promptDraft.trim())}
            >
              Lưu prompt
            </button>
            <label className="wz-btn wz-btn-sm" style={{ cursor: disabled ? "not-allowed" : "pointer", opacity: disabled ? 0.5 : 1 }}>
              {scene.skipGeneration ? "Thay clip khác (.mp4)" : "Tải clip có sẵn (.mp4)"}
              <input
                type="file"
                accept="video/*"
                hidden
                disabled={disabled}
                onChange={(e) => {
                  const f = e.target.files?.[0];
                  if (f) onUpload(f);
                  e.target.value = "";
                }}
              />
            </label>
          </div>

          <details>
            <summary className="wz-btn wz-btn-sm" style={{ display: "inline-block", cursor: "pointer", userSelect: "none" }}>
              🆓 Video stock miễn phí (Pexels)
            </summary>
            <div style={{ marginTop: 8 }}>
              <StockPicker prompt={scene.generationPrompt} disabled={disabled || busy} onImport={onImportStock} />
            </div>
          </details>

          {isVideo && (
            <details>
              <summary className="wz-btn wz-btn-sm" style={{ display: "inline-block", cursor: "pointer", userSelect: "none" }}>
                Công cụ AI free khác (Kling, Pika, Leonardo)
              </summary>
              <div style={{ marginTop: 8, display: "flex", flexDirection: "column", gap: 8 }}>
                <p className="wz-hint" style={{ margin: 0 }}>
                  Cùng một prompt, dán vào trang khác nếu muốn thử. Hạn mức miễn phí/ngày dưới đây là số công bố của
                  từng trang - ứng dụng không theo dõi được mức bạn đã dùng trên các trang này.
                </p>
                {OTHER_TOOLS.map((tool) => (
                  <div key={tool.key} style={{ border: "1px solid var(--border)", borderRadius: 6, padding: 8 }}>
                    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "baseline", gap: 8, flexWrap: "wrap" }}>
                      <strong style={{ fontSize: 13 }}>{tool.name}</strong>
                      <span className="wz-hint">{tool.domain}</span>
                    </div>
                    <p className="wz-hint" style={{ margin: "2px 0 6px" }}>
                      Hạn mức miễn phí công bố: {tool.dailyCredits}cr/ngày.
                    </p>
                    <a className="wz-btn wz-btn-sm" href={tool.url} target="_blank" rel="noreferrer">
                      Mở {tool.domain} ↗
                    </a>
                  </div>
                ))}
              </div>
            </details>
          )}

          {isVideo && (
            <p className="wz-hint" style={{ margin: 0 }}>
              Đã dùng {flowUsage.used}/{flowUsage.budget} credit ước tính hôm nay (ngân sách AI chung) - còn{" "}
              {flowUsage.remaining}.
            </p>
          )}
        </div>
      )}
    </div>
  );
}
