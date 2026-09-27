import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import "../ui/wizard.css";
import {
  describeApiError,
  storiesApi,
  type ContinuityIssueCategory,
  type ContinuityValidationResponse,
  type StoryEpisodeOutlineResponse,
  type StoryEpisodeResponse,
  type StoryResponse,
  type StoryStateResponse,
} from "../api/client";
import { CostNote, ErrorMessage, Field, Loading, StatusBadge, ValidationList } from "./components";

const CATEGORY_LABEL: Record<ContinuityIssueCategory, string> = {
  Character: "Nhân vật",
  Location: "Bối cảnh",
  Timeline: "Mốc thời gian",
  Object: "Vật phẩm",
  Plot: "Cốt truyện",
  Other: "Khác",
};

type EpisodeStep = "context" | "outline-generate" | "outline-review" | "script-generate" | "script-review";

const STEP_ORDER: EpisodeStep[] = ["context", "outline-generate", "outline-review", "script-generate", "script-review"];

const STEP_LABELS: Record<EpisodeStep, string> = {
  context: "1. Bối cảnh",
  "outline-generate": "2. Tạo dàn ý",
  "outline-review": "3. Xem dàn ý",
  "script-generate": "4. Viết kịch bản",
  "script-review": "5. Kịch bản & Lưu",
};

/**
 * New-episode workflow for a Story: review continuity context, generate an
 * outline, generate a script, then finalize (which runs continuity
 * validation and, only on success, marks the episode Completed and advances
 * StoryState). Once finalized, the user can create/open the linked
 * ContentProject to continue into the standalone video pipeline
 * (WizardPage) via storiesApi.createOrOpenVideo.
 */
function StoryEpisodeWizard() {
  const { id, episodeId } = useParams<{ id: string; episodeId: string }>();
  const navigate = useNavigate();

  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [story, setStory] = useState<StoryResponse | null>(null);
  const [episode, setEpisode] = useState<StoryEpisodeResponse | null>(null);
  const [previousEpisode, setPreviousEpisode] = useState<StoryEpisodeResponse | null>(null);
  const [storyState, setStoryState] = useState<StoryStateResponse | null>(null);

  const [step, setStep] = useState<EpisodeStep>("context");

  const [outline, setOutline] = useState<StoryEpisodeOutlineResponse | null>(null);
  // Local-only editable copy - see the notice rendered in the review step for why.
  const [outlineDraft, setOutlineDraft] = useState<StoryEpisodeOutlineResponse | null>(null);
  const [generatingOutline, setGeneratingOutline] = useState(false);
  const [outlineError, setOutlineError] = useState<string | null>(null);

  // Editable copy of the script - "Lưu chỉnh sửa" persists it via
  // storiesApi.updateEpisodeScript (no AI call).
  const [scriptDraft, setScriptDraft] = useState("");
  const [generatingScript, setGeneratingScript] = useState(false);
  const [scriptError, setScriptError] = useState<string | null>(null);

  const [savingScript, setSavingScript] = useState(false);
  const [saveScriptError, setSaveScriptError] = useState<string | null>(null);

  const [validating, setValidating] = useState(false);
  const [validateError, setValidateError] = useState<string | null>(null);

  const [finalizing, setFinalizing] = useState(false);
  const [finalizeError, setFinalizeError] = useState<string | null>(null);

  const [creatingVideo, setCreatingVideo] = useState(false);
  const [videoError, setVideoError] = useState<string | null>(null);

  // Result of the last "Kiểm tra tính nhất quán" (validate) OR "Hoàn tất tập
  // phim" (finalize) call - whichever ran most recently. Cleared (stale) by
  // any action that changes the underlying script (regenerate, manual save)
  // so the user never acts on a result for content that is no longer current.
  const [continuityResult, setContinuityResult] = useState<ContinuityValidationResponse | null>(null);

  useEffect(() => {
    if (!id || !episodeId) return;
    setLoading(true);
    setLoadError(null);

    (async () => {
      const [s, ep, st] = await Promise.all([
        storiesApi.getById(id),
        storiesApi.getEpisode(id, episodeId),
        storiesApi.getState(id),
      ]);
      setStory(s);
      setEpisode(ep);
      setStoryState(st);
      setScriptDraft(ep.script ?? "");

      if (ep.previousEpisodeId) {
        try {
          const prev = await storiesApi.getEpisode(id, ep.previousEpisodeId);
          setPreviousEpisode(prev);
        } catch {
          // Context-only, best effort - not fatal to the rest of the page.
        }
      }

      try {
        const existingOutline = await storiesApi.getOutline(id, episodeId);
        if (existingOutline) {
          setOutline(existingOutline);
          setOutlineDraft(existingOutline);
        }
      } catch {
        // Real fetch errors here just mean the outline panel starts empty -
        // the user can still generate one.
      }

      setStep(ep.status === "Scripted" || ep.status === "Completed" ? "script-review" : "context");
    })()
      .catch((err) => setLoadError(describeApiError(err, "Không tải được dữ liệu tập phim.")))
      .finally(() => setLoading(false));
  }, [id, episodeId]);

  const handleGenerateOutline = async () => {
    if (!id || !episodeId) return;
    setGeneratingOutline(true);
    setOutlineError(null);
    try {
      const result = await storiesApi.generateOutline(id, episodeId);
      setOutline(result);
      setOutlineDraft(result);
      setStep("outline-review");
    } catch (err) {
      setOutlineError(describeApiError(err, "Không tạo được dàn ý. Story này có thể chưa có Story Bible."));
    } finally {
      setGeneratingOutline(false);
    }
  };

  /**
   * Also used as the "Tạo lại tập phim" action on the script-review step -
   * this DISCARDS the current script (AI-generated or hand-edited), so once
   * a script already exists it requires explicit confirmation first.
   */
  const handleGenerateScript = async () => {
    if (!id || !episodeId) return;
    if (
      episode?.script &&
      !window.confirm("Tạo lại sẽ thay thế kịch bản hiện tại (kể cả phần bạn đã tự chỉnh sửa). Tiếp tục?")
    ) {
      return;
    }
    setGeneratingScript(true);
    setScriptError(null);
    try {
      const updated = await storiesApi.generateScript(id, episodeId);
      setEpisode(updated);
      setScriptDraft(updated.script ?? "");
      setContinuityResult(null);
      setFinalizeError(null);
      setStep("script-review");
    } catch (err) {
      setScriptError(describeApiError(err, "Không tạo được kịch bản. Tập này có thể chưa có dàn ý."));
    } finally {
      setGeneratingScript(false);
    }
  };

  const handleSaveScript = async () => {
    if (!id || !episodeId) return;
    setSavingScript(true);
    setSaveScriptError(null);
    try {
      const updated = await storiesApi.updateEpisodeScript(id, episodeId, scriptDraft);
      setEpisode(updated);
      setScriptDraft(updated.script ?? "");
      setContinuityResult(null);
      setFinalizeError(null);
    } catch (err) {
      setSaveScriptError(describeApiError(err, "Không lưu được kịch bản đã chỉnh sửa."));
    } finally {
      setSavingScript(false);
    }
  };

  const handleValidate = async () => {
    if (!id || !episodeId) return;
    setValidating(true);
    setValidateError(null);
    try {
      const result = await storiesApi.validateEpisode(id, episodeId);
      setContinuityResult(result);
    } catch (err) {
      setValidateError(describeApiError(err, "Không kiểm tra được tính nhất quán."));
    } finally {
      setValidating(false);
    }
  };

  /**
   * Only ever called once `continuityResult` is known locally (from a prior
   * validate, or a prior finalize attempt) so we never proceed past unseen
   * warnings without an explicit confirm - see the warnings branch below.
   */
  const handleFinalize = async () => {
    if (!id || !episodeId || !continuityResult) return;
    if (
      continuityResult.warnings.length > 0 &&
      !window.confirm(
        `Có ${continuityResult.warnings.length} cảnh báo tính nhất quán (không nghiêm trọng). Bỏ qua cảnh báo và hoàn tất tập phim?`,
      )
    ) {
      return;
    }
    setFinalizing(true);
    setFinalizeError(null);
    try {
      const result = await storiesApi.finalizeEpisode(id, episodeId);
      setContinuityResult(result);
      if (result.valid && result.episode) {
        setEpisode(result.episode);
      }
    } catch (err) {
      setFinalizeError(describeApiError(err, "Không thực hiện được kiểm tra & lưu tập phim."));
    } finally {
      setFinalizing(false);
    }
  };

  const handleCreateOrOpenVideo = async () => {
    if (!id || !episodeId) return;
    setCreatingVideo(true);
    setVideoError(null);
    try {
      const result = await storiesApi.createOrOpenVideo(id, episodeId);
      navigate(`/projects/${result.contentProjectId}`);
    } catch (err) {
      setVideoError(describeApiError(err, "Không tạo/mở được video cho tập này."));
    } finally {
      setCreatingVideo(false);
    }
  };

  if (loading) {
    return (
      <main className="wz">
        <Loading />
      </main>
    );
  }

  if (loadError || !story || !episode) {
    return (
      <main className="wz">
        <ErrorMessage message={loadError ?? "Không tìm thấy tập phim."} />
        {id && (
          <p className="wz-hint">
            <Link to={`/stories/${id}`}>← Về trang câu chuyện</Link>
          </p>
        )}
      </main>
    );
  }

  const reachable = new Set<EpisodeStep>(["context", "outline-generate"]);
  if (outline) {
    reachable.add("outline-review");
    reachable.add("script-generate");
  }
  if (episode.script) {
    reachable.add("script-review");
  }

  const isFinalized = episode.status === "Completed";

  return (
    <main className="wz">
      <p className="wz-hint">
        <Link to={`/stories/${story.id}`}>← {story.title}</Link>
      </p>
      <h1>
        Tập {episode.episodeNumber}: {episode.title}
      </h1>

      <ol className="wz-steps">
        {STEP_ORDER.map((s) => (
          <li key={s} style={{ display: "contents" }}>
            <button
              type="button"
              className="wz-step"
              aria-current={s === step ? "step" : undefined}
              disabled={!reachable.has(s)}
              onClick={() => setStep(s)}
              title={reachable.has(s) ? undefined : "Hoàn thành bước trước đã"}
            >
              {STEP_LABELS[s]}
            </button>
          </li>
        ))}
      </ol>

      {step === "context" && (
        <section style={{ marginTop: 20 }}>
          <h2>Bối cảnh truyện trước khi viết tập mới</h2>
          <p className="wz-hint">
            Xem lại trước khi tạo dàn ý - việc tạo dàn ý và kịch bản ở các bước sau sẽ gọi AI và tốn token.
          </p>

          <div className="wz-credits">
            <div className="wz-kv">
              {previousEpisode && (
                <span>
                  <span className="wz-kv-k">Tập trước</span>
                  <span className="wz-kv-v">
                    Tập {previousEpisode.episodeNumber}: {previousEpisode.title}
                  </span>
                </span>
              )}
              {storyState?.currentLocation && (
                <span>
                  <span className="wz-kv-k">Bối cảnh hiện tại</span>
                  <span className="wz-kv-v">{storyState.currentLocation}</span>
                </span>
              )}
              {storyState?.currentObjective && (
                <span>
                  <span className="wz-kv-k">Mục tiêu hiện tại</span>
                  <span className="wz-kv-v">{storyState.currentObjective}</span>
                </span>
              )}
              {storyState?.nextPlannedDestination && (
                <span>
                  <span className="wz-kv-k">Điểm đến tiếp theo</span>
                  <span className="wz-kv-v">{storyState.nextPlannedDestination}</span>
                </span>
              )}
            </div>

            {story.characters.length > 0 && (
              <div className="wz-story-block" style={{ marginTop: 10 }}>
                <h4>Nhân vật</h4>
                <ul>
                  {story.characters.map((c) => (
                    <li key={c.id}>
                      {c.name}
                      {c.description ? ` — ${c.description}` : ""}
                    </li>
                  ))}
                </ul>
              </div>
            )}

            {storyState?.characterStates && (
              <div className="wz-story-block" style={{ marginTop: 10 }}>
                <h4>Trạng thái nhân vật</h4>
                <p>{storyState.characterStates}</p>
              </div>
            )}

            {storyState && storyState.openStoryThreads.length > 0 && (
              <div className="wz-story-block" style={{ marginTop: 10 }}>
                <h4>Mạch truyện còn mở</h4>
                <ul>
                  {storyState.openStoryThreads.map((t) => (
                    <li key={t}>{t}</li>
                  ))}
                </ul>
              </div>
            )}
          </div>

          <div className="wz-actions">
            <button type="button" className="wz-btn wz-btn-primary" onClick={() => setStep("outline-generate")}>
              Tiếp tục → Tạo dàn ý
            </button>
          </div>
        </section>
      )}

      {step === "outline-generate" && (
        <section style={{ marginTop: 20 }}>
          <h2>Tạo dàn ý tập phim</h2>
          <p className="wz-hint">
            AI sẽ dựa trên Story Bible, trạng thái truyện hiện tại và tập trước (nếu có) để viết dàn ý cho tập này.
          </p>
          <ErrorMessage message={outlineError} />
          <div className="wz-actions">
            <button type="button" className="wz-btn wz-btn-primary" disabled={generatingOutline} onClick={handleGenerateOutline}>
              {generatingOutline ? "Đang tạo dàn ý..." : outline ? "Tạo lại dàn ý" : "Tạo dàn ý"}
            </button>
            <CostNote kind="text" />
          </div>
        </section>
      )}

      {step === "outline-review" && outlineDraft && (
        <section style={{ marginTop: 20 }}>
          <h2>Xem & chỉnh sửa dàn ý</h2>
          <p className="wz-hint">
            Chỉnh sửa ở đây chỉ để bạn tham khảo trong phiên làm việc này - hệ thống hiện chưa hỗ trợ lưu dàn ý đã
            chỉnh sửa lên máy chủ. Nội dung được lưu chính thức là kịch bản ở bước tiếp theo.
          </p>

          <Field label="Tiêu đề tập">
            <input
              type="text"
              value={outlineDraft.title ?? ""}
              onChange={(e) => setOutlineDraft({ ...outlineDraft, title: e.target.value })}
            />
          </Field>
          <Field label="Mục tiêu (Objective)">
            <textarea
              rows={2}
              value={outlineDraft.objective ?? ""}
              onChange={(e) => setOutlineDraft({ ...outlineDraft, objective: e.target.value })}
            />
          </Field>
          <Field label="Mở đầu (Setup)">
            <textarea
              rows={2}
              value={outlineDraft.setup ?? ""}
              onChange={(e) => setOutlineDraft({ ...outlineDraft, setup: e.target.value })}
            />
          </Field>
          <Field label="Xung đột (Conflict)">
            <textarea
              rows={2}
              value={outlineDraft.conflict ?? ""}
              onChange={(e) => setOutlineDraft({ ...outlineDraft, conflict: e.target.value })}
            />
          </Field>
          <Field label="Cao trào (Escalation)">
            <textarea
              rows={2}
              value={outlineDraft.escalation ?? ""}
              onChange={(e) => setOutlineDraft({ ...outlineDraft, escalation: e.target.value })}
            />
          </Field>
          <Field label="Giải quyết (Resolution)">
            <textarea
              rows={2}
              value={outlineDraft.resolution ?? ""}
              onChange={(e) => setOutlineDraft({ ...outlineDraft, resolution: e.target.value })}
            />
          </Field>
          <Field label="Cliffhanger">
            <textarea
              rows={2}
              value={outlineDraft.cliffhanger ?? ""}
              onChange={(e) => setOutlineDraft({ ...outlineDraft, cliffhanger: e.target.value })}
            />
          </Field>

          {outlineDraft.majorBeats.length > 0 && (
            <div className="wz-story-block">
              <h4>Các nhịp truyện chính</h4>
              <ul>
                {outlineDraft.majorBeats.map((b, i) => (
                  <li key={i}>{b}</li>
                ))}
              </ul>
            </div>
          )}
          {outlineDraft.continuityRequirements.length > 0 && (
            <div className="wz-story-block">
              <h4>Yêu cầu nhất quán</h4>
              <ul>
                {outlineDraft.continuityRequirements.map((b, i) => (
                  <li key={i}>{b}</li>
                ))}
              </ul>
            </div>
          )}
          {outlineDraft.scenesRequired.length > 0 && (
            <div className="wz-story-block">
              <h4>Cảnh cần có</h4>
              <ul>
                {outlineDraft.scenesRequired.map((b, i) => (
                  <li key={i}>{b}</li>
                ))}
              </ul>
            </div>
          )}

          <div className="wz-actions">
            <button type="button" className="wz-btn" onClick={() => setStep("outline-generate")}>
              ← Tạo lại dàn ý
            </button>
            <button type="button" className="wz-btn wz-btn-primary" onClick={() => setStep("script-generate")}>
              Tiếp tục → Viết kịch bản
            </button>
          </div>
        </section>
      )}

      {step === "script-generate" && (
        <section style={{ marginTop: 20 }}>
          <h2>Viết kịch bản</h2>
          <p className="wz-hint">AI viết kịch bản đầy đủ cho tập này dựa trên dàn ý, Story Bible và trạng thái truyện.</p>
          <ErrorMessage message={scriptError} />
          <div className="wz-actions">
            <button type="button" className="wz-btn wz-btn-primary" disabled={generatingScript} onClick={handleGenerateScript}>
              {generatingScript ? "Đang viết kịch bản..." : episode.script ? "Viết lại kịch bản" : "Viết kịch bản"}
            </button>
            <CostNote kind="text" />
          </div>
        </section>
      )}

      {step === "script-review" && (
        <section style={{ marginTop: 20 }}>
          <h2>Xem & chỉnh sửa kịch bản</h2>

          {!isFinalized && (
            <p className="wz-hint">
              Chỉnh sửa kịch bản trực tiếp bên dưới rồi bấm "Lưu chỉnh sửa" để lưu lên máy chủ - kịch bản đã lưu là
              bản chính thức dùng khi kiểm tra tính nhất quán và hoàn tất tập phim.
            </p>
          )}

          <Field label="Kịch bản">
            <textarea
              rows={16}
              value={scriptDraft}
              readOnly={isFinalized}
              onChange={(e) => setScriptDraft(e.target.value)}
            />
          </Field>

          {isFinalized ? (
            <div className="wz-empty" style={{ marginTop: 12 }}>
              <strong>✅ Tập phim đã được lưu và hoàn tất.</strong>
              <p className="wz-hint">Kiểm tra tính nhất quán đã đạt - trạng thái truyện đã được cập nhật cho tập tiếp theo.</p>
              <p className="wz-hint">
                Tiếp tục dựng video cho tập này bằng kịch bản vừa hoàn tất, hoặc mở lại video đã tạo trước đó.
              </p>
              <ErrorMessage message={videoError} />
              <div className="wz-actions">
                <button
                  type="button"
                  className="wz-btn wz-btn-primary"
                  disabled={creatingVideo}
                  onClick={handleCreateOrOpenVideo}
                >
                  {creatingVideo ? "Đang xử lý..." : episode.contentProjectId ? "Mở video" : "Tạo video"}
                </button>
                <Link className="wz-btn" to={`/stories/${story.id}`}>
                  ← Về trang câu chuyện
                </Link>
              </div>
            </div>
          ) : (
            <>
              <ErrorMessage message={saveScriptError} />
              <div className="wz-actions">
                <button
                  type="button"
                  className="wz-btn"
                  disabled={savingScript || scriptDraft === (episode.script ?? "")}
                  onClick={handleSaveScript}
                >
                  {savingScript ? "Đang lưu..." : "Lưu chỉnh sửa"}
                </button>
                <button type="button" className="wz-btn" disabled={generatingScript} onClick={handleGenerateScript}>
                  {generatingScript ? "Đang tạo lại..." : "Tạo lại tập phim"}
                </button>
                <CostNote kind="text" />
              </div>
              <ErrorMessage message={scriptError} />

              <div className="wz-actions" style={{ marginTop: 10 }}>
                <button type="button" className="wz-btn" disabled={validating} onClick={handleValidate}>
                  {validating ? "Đang kiểm tra..." : "Kiểm tra tính nhất quán"}
                </button>
                <CostNote kind="text" />
              </div>
              <ErrorMessage message={validateError} />

              {continuityResult && (
                <div className="wz-story-block" style={{ marginTop: 12 }}>
                  <p>
                    <strong>Điểm nhất quán:</strong> {Math.round(continuityResult.score * 100)}%
                  </p>

                  {continuityResult.criticalIssues.length > 0 && (
                    <div style={{ marginTop: 8 }}>
                      <StatusBadge tone="danger">
                        Lỗi nghiêm trọng ({continuityResult.criticalIssues.length})
                      </StatusBadge>
                      <ValidationList
                        title="Chi tiết lỗi nghiêm trọng"
                        items={continuityResult.criticalIssues.map(
                          (i) => `[${CATEGORY_LABEL[i.category]}] ${i.message}`,
                        )}
                        error
                      />
                    </div>
                  )}

                  {continuityResult.warnings.length > 0 && (
                    <div style={{ marginTop: 8 }}>
                      <StatusBadge tone="warning">Cảnh báo ({continuityResult.warnings.length})</StatusBadge>
                      <ValidationList
                        title="Chi tiết cảnh báo"
                        items={continuityResult.warnings.map((i) => `[${CATEGORY_LABEL[i.category]}] ${i.message}`)}
                      />
                    </div>
                  )}

                  {continuityResult.criticalIssues.length === 0 && continuityResult.warnings.length === 0 && (
                    <p className="wz-hint">Không phát hiện vấn đề tính nhất quán nào.</p>
                  )}

                  {continuityResult.criticalIssues.length > 0 ? (
                    <p className="wz-error" style={{ marginTop: 8 }}>
                      Còn lỗi nghiêm trọng - hãy chỉnh sửa kịch bản thủ công hoặc tạo lại tập phim rồi kiểm tra lại
                      trước khi hoàn tất.
                    </p>
                  ) : (
                    <div className="wz-actions" style={{ marginTop: 8 }}>
                      <button
                        type="button"
                        className="wz-btn wz-btn-primary"
                        disabled={finalizing}
                        onClick={handleFinalize}
                      >
                        {finalizing
                          ? "Đang hoàn tất..."
                          : continuityResult.warnings.length > 0
                            ? "Bỏ qua cảnh báo và tiếp tục"
                            : "Hoàn tất tập phim"}
                      </button>
                      <CostNote kind="text" />
                    </div>
                  )}
                </div>
              )}

              <ErrorMessage message={finalizeError} />
            </>
          )}
        </section>
      )}
    </main>
  );
}

export default StoryEpisodeWizard;
