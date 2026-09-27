import { useCallback, useEffect, useRef, useState, type ReactElement } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import "../ui/wizard.css";
import {
  contentProjectsApi,
  describeApiError,
  presetsApi,
  wizardApi,
  type ContentIdeaSuggestion,
  type PresetCatalog,
  type ProjectOverview,
  type WizardStepName,
} from "../api/client";
import { ExportStep, GenerateStep, IdeaStep, PreviewStep, ScriptStep, TemplateStep, type StepProps } from "./steps";
import { ContentIdeaSuggestions } from "./ContentIdeaSuggestions";
import { ReferencesStep } from "./ReferencesStep";
import { CostNote, ErrorMessage, Field, Loading, PresetPicker, ProgressPanel, StepNav } from "./components";

/** New projects have no language picker; the API defaults to this on create. */
const NEW_PROJECT_LANGUAGE = "en";

const POLL_INTERVAL_MS = 3000;

/**
 * Hangfire's queue poll interval is 15s, so a freshly enqueued job can take
 * that long to report itself as running. Without this grace window the wizard
 * would look idle right after the user pressed the button.
 */
const JOB_PICKUP_GRACE_MS = 45_000;

const STEP_COMPONENTS: Record<WizardStepName, (props: StepProps) => ReactElement> = {
  Template: TemplateStep,
  Idea: IdeaStep,
  Script: ScriptStep,
  References: ReferencesStep,
  Generate: GenerateStep,
  Preview: PreviewStep,
  Export: ExportStep,
};

function WizardPage() {
  const { id } = useParams<{ id: string }>();

  const [catalog, setCatalog] = useState<PresetCatalog | null>(null);
  const [catalogError, setCatalogError] = useState<string | null>(null);

  useEffect(() => {
    presetsApi
      .getCatalog()
      .then(setCatalog)
      .catch((err) => setCatalogError(describeApiError(err, "Không tải được danh sách mẫu.")));
  }, []);

  if (catalogError) {
    return (
      <main className="wz">
        <ErrorMessage message={catalogError} />
      </main>
    );
  }

  if (!catalog) {
    return (
      <main className="wz">
        <Loading />
      </main>
    );
  }

  return id ? <ExistingProjectWizard contentProjectId={id} catalog={catalog} /> : <NewProjectWizard catalog={catalog} />;
}

// --------------------------------------------------------------------------

/**
 * Before a project exists there is nothing to poll, so this holds the template
 * and idea locally and creates the project in one request when the user is
 * ready - rather than creating an empty project the moment they open the page.
 */
function NewProjectWizard({ catalog }: { catalog: PresetCatalog }) {
  const navigate = useNavigate();

  const [step, setStep] = useState<WizardStepName>("Template");
  const [templateId, setTemplateId] = useState<string | null>(null);
  const [styleId, setStyleId] = useState<string | null>(null);
  const [voiceId, setVoiceId] = useState<string | null>(null);
  const [captionId, setCaptionId] = useState<string | null>(null);

  const [title, setTitle] = useState("");
  const [topic, setTopic] = useState("");
  const [niche, setNiche] = useState("");
  const [duration, setDuration] = useState(45);

  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Drops an AI-suggested idea into the normal fields; from here it is just a
  // user-entered idea and the create flow is unchanged.
  const useSuggestedIdea = (suggestion: ContentIdeaSuggestion) => {
    setError(null);
    setTitle(suggestion.title);
    setNiche(suggestion.niche);
    setTopic(
      suggestion.hook.trim()
        ? `${suggestion.concept.trim()}\n\nMở đầu gợi ý: ${suggestion.hook.trim()}`
        : suggestion.concept.trim(),
    );
  };

  // Picking a template pre-fills the other three and the duration, so the
  // fastest path through the wizard is template -> title -> go.
  const chooseTemplate = (id: string) => {
    const template = catalog.templates.find((t) => t.id === id);
    setTemplateId(id);
    if (!template) return;
    setStyleId(template.defaultStylePresetId);
    setVoiceId(template.defaultVoicePresetId);
    setCaptionId(template.defaultCaptionPresetId);
    setDuration(template.defaultDurationSeconds);
    setNiche((current) => current || template.niche);
  };

  const handleCreate = async () => {
    setSubmitting(true);
    setError(null);
    try {
      const project = await contentProjectsApi.create({
        title: title.trim(),
        topic: topic.trim() || undefined,
        niche: niche.trim() || undefined,
        targetDurationSeconds: duration,
        templateId: templateId ?? undefined,
        stylePresetId: styleId ?? undefined,
        voicePresetId: voiceId ?? undefined,
        captionPresetId: captionId ?? undefined,
      });

      await contentProjectsApi.generate(project.id);
      navigate(`/projects/${project.id}`, { replace: true });
    } catch (err) {
      setError(describeApiError(err, "Không tạo được video."));
      setSubmitting(false);
    }
  };

  return (
    <main className="wz">
      <Link to="/">&larr; Tất cả video</Link>
      <h1>Tạo video mới</h1>

      <StepNav
        active={step}
        reachable={templateId ? ["Template", "Idea"] : ["Template"]}
        onSelect={setStep}
      />

      <ErrorMessage message={error} />

      {step === "Template" ? (
        <section className="wz-card">
          <h2>Chọn kiểu nội dung</h2>
          <p className="wz-sub">Mẫu quyết định cách viết kịch bản và điền sẵn phong cách, giọng đọc, phụ đề.</p>

          <PresetPicker
            label="Mẫu nội dung"
            presets={catalog.templates}
            selectedId={templateId}
            onSelect={chooseTemplate}
            extra={(template) => (
              <span className="wz-hint" style={{ display: "block", marginTop: 5 }}>
                {template.niche} · {template.defaultDurationSeconds}s
              </span>
            )}
          />

          <PresetPicker label="Phong cách hình ảnh" presets={catalog.styles} selectedId={styleId} onSelect={setStyleId} />
          <PresetPicker label="Giọng đọc" presets={catalog.voices} selectedId={voiceId} onSelect={setVoiceId} />
          <PresetPicker label="Kiểu phụ đề" presets={catalog.captions} selectedId={captionId} onSelect={setCaptionId} />

          <div className="wz-actions">
            <button
              type="button"
              className="wz-btn wz-btn-primary"
              disabled={!templateId}
              onClick={() => setStep("Idea")}
            >
              Tiếp tục
            </button>
          </div>
        </section>
      ) : (
        <section className="wz-card">
          <h2>Ý tưởng video</h2>
          <p className="wz-sub">Càng cụ thể thì kịch bản càng sắc.</p>

          <ContentIdeaSuggestions
            language={NEW_PROJECT_LANGUAGE}
            durationSeconds={duration}
            existingIdeas={[title, topic].map((s) => s.trim()).filter((s) => s.length > 0)}
            disabled={submitting}
            onUseIdea={useSuggestedIdea}
          />

          <Field label="Tiêu đề">
            <input type="text" value={title} onChange={(e) => setTitle(e.target.value)} />
          </Field>

          <Field label="Nội dung muốn kể" hint="Ví dụ: “Vì sao mèo hay ngồi trong hộp giấy”.">
            <textarea rows={3} value={topic} onChange={(e) => setTopic(e.target.value)} />
          </Field>

          <div className="wz-row">
            <Field label="Chủ đề / ngách">
              <input type="text" value={niche} onChange={(e) => setNiche(e.target.value)} />
            </Field>
            <Field label="Độ dài video (giây)">
              <input
                type="number"
                min={1}
                max={180}
                value={duration}
                onChange={(e) => setDuration(Number(e.target.value))}
              />
            </Field>
          </div>

          <div className="wz-actions">
            <button
              type="button"
              className="wz-btn wz-btn-primary"
              disabled={submitting || !title.trim()}
              onClick={handleCreate}
            >
              {submitting ? "Đang tạo..." : "Viết kịch bản"}
            </button>
            <CostNote kind="longText">Viết kịch bản bằng AI</CostNote>
            <button type="button" className="wz-btn" disabled={submitting} onClick={() => setStep("Template")}>
              Quay lại
            </button>
          </div>
        </section>
      )}
    </main>
  );
}

// --------------------------------------------------------------------------

function ExistingProjectWizard({ contentProjectId, catalog }: { contentProjectId: string; catalog: PresetCatalog }) {
  const [overview, setOverview] = useState<ProjectOverview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [step, setStep] = useState<WizardStepName | null>(null);

  // Once the user clicks a step in the nav, stop yanking them somewhere else
  // when a background job finishes.
  const pinnedRef = useRef(false);
  const jobStartedAtRef = useRef<number | null>(null);
  const sawBusyRef = useRef(false);

  const [waitingForJob, setWaitingForJob] = useState(false);
  // True once we've waited past the normal pickup window with no sign of the
  // job yet - the run may just be queued behind other work. We keep waiting
  // (never silently revert to an idle-looking screen), just say so plainly
  // instead of leaving "Đang bắt đầu..." up forever unchanged.
  const [jobPickupDelayed, setJobPickupDelayed] = useState(false);

  // Guards against losing in-progress edits: a step component reports its own
  // dirty state here, and both leaving the step (via the step nav or a step's
  // own "continue" button - both go through handleGoTo) and closing the tab
  // check it first.
  const dirtyRef = useRef(false);
  const handleDirtyChange = useCallback((dirty: boolean) => {
    dirtyRef.current = dirty;
  }, []);

  const load = useCallback(async () => {
    const data = await wizardApi.getOverview(contentProjectId);
    setOverview(data);

    if (data.progress.busy) {
      sawBusyRef.current = true;
      setJobPickupDelayed(false);
    } else if (sawBusyRef.current) {
      // The job we were waiting on has finished.
      sawBusyRef.current = false;
      jobStartedAtRef.current = null;
      setWaitingForJob(false);
      setJobPickupDelayed(false);
      pinnedRef.current = false;
    } else if (jobStartedAtRef.current !== null && Date.now() - jobStartedAtRef.current > JOB_PICKUP_GRACE_MS) {
      // Still nothing after the normal pickup window - keep waiting/polling
      // instead of silently going quiet (the job may just be queued behind
      // other work), and say so explicitly so the user isn't left guessing
      // and tempted to resubmit a possibly billable action.
      setJobPickupDelayed(true);
    }

    if (!pinnedRef.current) {
      setStep(data.step);
    }

    return data;
  }, [contentProjectId]);

  useEffect(() => {
    setLoading(true);
    load()
      .catch((err) => setError(describeApiError(err, "Không tải được video này.")))
      .finally(() => setLoading(false));
  }, [load]);

  const busy = (overview?.progress.busy ?? false) || waitingForJob;

  useEffect(() => {
    if (!busy) return;

    const timer = window.setInterval(() => {
      load().catch((err) => setError(describeApiError(err, "Mất kết nối với máy chủ.")));
    }, POLL_INTERVAL_MS);

    return () => window.clearInterval(timer);
  }, [busy, load]);

  const handleJobStarted = useCallback(() => {
    jobStartedAtRef.current = Date.now();
    sawBusyRef.current = false;
    setWaitingForJob(true);
    setJobPickupDelayed(false);
    // Following the server's step again is the point of starting a job.
    pinnedRef.current = false;
  }, []);

  const handleRefresh = useCallback(() => {
    load().catch((err) => setError(describeApiError(err, "Không làm mới được.")));
  }, [load]);

  const handleGoTo = useCallback((next: WizardStepName) => {
    if (
      dirtyRef.current &&
      !window.confirm("Bạn có thay đổi chưa lưu ở bước này. Rời khỏi bước này sẽ mất các thay đổi đó. Tiếp tục?")
    ) {
      return;
    }
    dirtyRef.current = false;
    pinnedRef.current = true;
    setStep(next);
  }, []);

  // Closing/refreshing the tab loses unsaved edits the same way leaving the
  // step does - warn there too.
  useEffect(() => {
    const handler = (e: BeforeUnloadEvent) => {
      if (dirtyRef.current) {
        e.preventDefault();
        e.returnValue = "";
      }
    };
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, []);

  // A step can also change because the server moved us on (not just via
  // handleGoTo, which already clears it) - either way the newly-mounted step
  // starts clean, so the guard should too.
  useEffect(() => {
    dirtyRef.current = false;
  }, [step]);

  if (loading || !overview || !step) {
    return (
      <main className="wz">
        {error ? <ErrorMessage message={error} /> : <Loading />}
      </main>
    );
  }

  // The nav lets the user revisit anything the project's data supports, plus
  // whichever step they are currently looking at. Preview and Export stay open
  // regardless: each panel already handles the "nothing built yet" case on its
  // own, so gating navigation to them only gets in the way.
  const reachable = Array.from(
    new Set<WizardStepName>([...overview.reachableSteps, step, "Preview", "Export"]),
  );
  const StepComponent = STEP_COMPONENTS[step];

  return (
    <main className="wz">
      <Link to="/">&larr; Tất cả video</Link>
      <h1>{overview.title}</h1>
      <p className="wz-sub">
        {overview.presets.templateName ?? "Chưa chọn mẫu"} · {overview.targetDurationSeconds}s · {overview.aspectRatio}
      </p>

      <StepNav active={step} reachable={reachable} onSelect={handleGoTo} />

      <ProgressPanel progress={overview.progress} estimatedTotalSeconds={overview.estimate.totalSeconds} />
      {waitingForJob && !overview.progress.busy && (
        <div className="wz-progress" role="status">
          <strong>{jobPickupDelayed ? "Vẫn đang chờ máy chủ xử lý..." : "Đang bắt đầu..."}</strong>
          {jobPickupDelayed && (
            <p className="wz-hint" style={{ marginTop: 8 }}>
              Việc này đang mất nhiều thời gian hơn bình thường để bắt đầu - có thể máy chủ đang xử lý việc khác.
              Không cần bấm lại nút vừa rồi; hãy đợi thêm hoặc tải lại trang.
            </p>
          )}
        </div>
      )}

      {overview.failed && !busy && (
        <ErrorMessage message="Lần chạy trước bị lỗi. Hãy thử lại bước đang dở - phần đã xong vẫn được giữ." />
      )}
      <ErrorMessage message={error} />

      <StepComponent
        overview={overview}
        catalog={catalog}
        busy={busy}
        onRefresh={handleRefresh}
        onJobStarted={handleJobStarted}
        onGoTo={handleGoTo}
        onError={setError}
        onDirtyChange={handleDirtyChange}
      />

      <footer className="wz-footer">
        <Link to={`/projects/${overview.id}/advanced`}>Chế độ nâng cao</Link>
        <span>Dành cho khi cần xem chi tiết kỹ thuật hoặc chẩn đoán lỗi.</span>
      </footer>
    </main>
  );
}

export default WizardPage;
