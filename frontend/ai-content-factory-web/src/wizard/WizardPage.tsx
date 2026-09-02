import { useCallback, useEffect, useRef, useState, type ReactElement } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import "../ui/wizard.css";
import {
  contentProjectsApi,
  presetsApi,
  wizardApi,
  type PresetCatalog,
  type ProjectOverview,
  type WizardStepName,
} from "../api/client";
import { ExportStep, GenerateStep, IdeaStep, PreviewStep, ScriptStep, TemplateStep, type StepProps } from "./steps";
import { ReferencesStep } from "./ReferencesStep";
import { Field, PresetPicker, ProgressPanel, StepNav } from "./components";

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
      .catch((err) => setCatalogError(err instanceof Error ? err.message : "Không tải được danh sách mẫu."));
  }, []);

  if (catalogError) {
    return (
      <main className="wz">
        <p className="wz-error">{catalogError}</p>
      </main>
    );
  }

  if (!catalog) {
    return (
      <main className="wz">
        <p>Đang tải...</p>
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
      setError(err instanceof Error ? err.message : "Không tạo được video.");
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

      {error && <p className="wz-error">{error}</p>}

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

  const load = useCallback(async () => {
    const data = await wizardApi.getOverview(contentProjectId);
    setOverview(data);

    if (data.progress.busy) {
      sawBusyRef.current = true;
    } else if (sawBusyRef.current) {
      // The job we were waiting on has finished.
      sawBusyRef.current = false;
      jobStartedAtRef.current = null;
      setWaitingForJob(false);
      pinnedRef.current = false;
    } else if (jobStartedAtRef.current !== null && Date.now() - jobStartedAtRef.current > JOB_PICKUP_GRACE_MS) {
      // Never saw it start - give up waiting rather than polling forever.
      jobStartedAtRef.current = null;
      setWaitingForJob(false);
    }

    if (!pinnedRef.current) {
      setStep(data.step);
    }

    return data;
  }, [contentProjectId]);

  useEffect(() => {
    setLoading(true);
    load()
      .catch((err) => setError(err instanceof Error ? err.message : "Không tải được video này."))
      .finally(() => setLoading(false));
  }, [load]);

  const busy = (overview?.progress.busy ?? false) || waitingForJob;

  useEffect(() => {
    if (!busy) return;

    const timer = window.setInterval(() => {
      load().catch((err) => setError(err instanceof Error ? err.message : "Mất kết nối với máy chủ."));
    }, POLL_INTERVAL_MS);

    return () => window.clearInterval(timer);
  }, [busy, load]);

  const handleJobStarted = useCallback(() => {
    jobStartedAtRef.current = Date.now();
    sawBusyRef.current = false;
    setWaitingForJob(true);
    // Following the server's step again is the point of starting a job.
    pinnedRef.current = false;
  }, []);

  const handleRefresh = useCallback(() => {
    load().catch((err) => setError(err instanceof Error ? err.message : "Không làm mới được."));
  }, [load]);

  const handleGoTo = useCallback((next: WizardStepName) => {
    pinnedRef.current = true;
    setStep(next);
  }, []);

  if (loading || !overview || !step) {
    return (
      <main className="wz">
        <p>{error ? <span className="wz-error">{error}</span> : "Đang tải..."}</p>
      </main>
    );
  }

  // The nav lets the user revisit anything the project's data supports, plus
  // whichever step they are currently looking at.
  const reachable = Array.from(new Set([...overview.reachableSteps, step]));
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
          <strong>Đang bắt đầu...</strong>
        </div>
      )}

      {overview.failed && !busy && (
        <p className="wz-error">Lần chạy trước bị lỗi. Hãy thử lại bước đang dở - phần đã xong vẫn được giữ.</p>
      )}
      {error && <p className="wz-error">{error}</p>}

      <StepComponent
        overview={overview}
        catalog={catalog}
        busy={busy}
        onRefresh={handleRefresh}
        onJobStarted={handleJobStarted}
        onGoTo={handleGoTo}
        onError={setError}
      />

      <footer className="wz-footer">
        <Link to={`/projects/${overview.id}/advanced`}>Chế độ nâng cao</Link>
        <span>Dành cho khi cần xem chi tiết kỹ thuật hoặc chẩn đoán lỗi.</span>
      </footer>
    </main>
  );
}

export default WizardPage;
