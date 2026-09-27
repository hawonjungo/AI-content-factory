import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import {
  assetFileUrl,
  assetsApi,
  clipPlanApi,
  contentProjectsApi,
  pipelineApi,
  qaApi,
  assetReferencesApi,
  scriptApi,
  storyboardApi,
  type Asset,
  type AssetType,
  type ContentProject,
  type QaScore,
  type AssetReferenceSlots,
  type Scene,
  type Script,
  type Storyboard,
} from "../api/client";

const ASSET_TYPES: AssetType[] = ["Video", "Image", "Audio", "Voice", "Music", "Subtitle", "Thumbnail"];

const sectionStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 8,
  padding: "1rem",
  marginBottom: "1.5rem",
};

// Statuses a background job might leave the project in - once we see one of
// these (or Failed) after triggering an action, the job is done.
const SETTLED_STATUSES = ["ScriptReady", "QA", "StoryboardReady", "Editing", "AwaitingApproval", "Failed"];

function AdvancedProjectPage() {
  const { id } = useParams<{ id: string }>();
  const [project, setProject] = useState<ContentProject | null>(null);
  const [script, setScript] = useState<Script | null>(null);
  const [storyboard, setStoryboard] = useState<Storyboard | null>(null);
  const [assets, setAssets] = useState<Asset[]>([]);
  const [qaScores, setQaScores] = useState<QaScore[]>([]);
  const [references, setReferences] = useState<AssetReferenceSlots | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busyAction, setBusyAction] = useState<string | null>(null);
  const [elapsedSeconds, setElapsedSeconds] = useState(0);
  const pollRef = useRef<number | null>(null);
  const elapsedRef = useRef<number | null>(null);

  const loadAll = useCallback(async () => {
    if (!id) return;
    const [p, s, sb, a, qa, refs] = await Promise.all([
      contentProjectsApi.getById(id),
      scriptApi.get(id),
      storyboardApi.get(id),
      assetsApi.getAll(id),
      qaApi.getHistory(id),
      assetReferencesApi.getSlots(id),
    ]);
    setProject(p);
    setScript(s);
    setStoryboard(sb);
    setAssets(a);
    setQaScores(qa);
    setReferences(refs);
    return p;
  }, [id]);

  useEffect(() => {
    setLoading(true);
    loadAll()
      .catch((err) => setError(err instanceof Error ? err.message : "Failed to load content project."))
      .finally(() => setLoading(false));

    return () => {
      if (pollRef.current) window.clearInterval(pollRef.current);
      if (elapsedRef.current) window.clearInterval(elapsedRef.current);
    };
  }, [loadAll]);

  useEffect(() => {
    const handler = (e: BeforeUnloadEvent) => {
      if (busyAction) {
        e.preventDefault();
        e.returnValue = "";
      }
    };
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, [busyAction]);

  const triggerAndPoll = async (actionName: string, trigger: () => Promise<unknown>) => {
    if (!id) return;
    setError(null);
    setBusyAction(actionName);
    setElapsedSeconds(0);
    try {
      await trigger();

      elapsedRef.current = window.setInterval(() => setElapsedSeconds((s) => s + 1), 1000);

      let ticks = 0;
      // Asset generation runs Veo (up to 5 min PER clip, sequentially) - a
      // plan with several clips can legitimately take 20+ minutes.
      const maxTicks = actionName === "assets" ? 500 : 60;
      pollRef.current = window.setInterval(async () => {
        ticks += 1;
        const p = await loadAll().catch((err) => {
          setError(err instanceof Error ? err.message : "Failed to refresh project status.");
          setBusyAction(null);
          if (pollRef.current) window.clearInterval(pollRef.current);
          if (elapsedRef.current) window.clearInterval(elapsedRef.current);
          return null;
        });
        if ((p && SETTLED_STATUSES.includes(p.status)) || ticks > maxTicks) {
          setBusyAction(null);
          if (pollRef.current) window.clearInterval(pollRef.current);
          if (elapsedRef.current) window.clearInterval(elapsedRef.current);
        }
      }, 3000);
    } catch (err) {
      setError(err instanceof Error ? err.message : `Failed to start ${actionName}.`);
      setBusyAction(null);
      if (elapsedRef.current) window.clearInterval(elapsedRef.current);
    }
  };

  const handleApprove = async (approve: boolean) => {
    if (!id) return;
    setError(null);
    try {
      const updated = await pipelineApi.setStatus(id, approve ? "Approved" : "Rejected");
      setProject(updated);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to update approval status.");
    }
  };

  if (loading) return <main style={{ padding: "2rem" }}>Loading...</main>;
  if (!project) return <main style={{ padding: "2rem" }}>Content project not found.</main>;

  const finalVideoAsset = assets.find((a) => a.sceneId == null && a.type === "Video" && a.status === "Ready");
  const latestQaScore = qaScores[0];

  const hasScript = script !== null;
  const hasScenes = (storyboard?.scenes.length ?? 0) > 0;
  const hasReadyClipPerScene =
    hasScenes && storyboard!.scenes.every((scene) => assets.some((a) => a.sceneId === scene.id && a.status === "Ready" && a.type === "Video"));

  const formatElapsed = (totalSeconds: number) => {
    const m = Math.floor(totalSeconds / 60);
    const s = totalSeconds % 60;
    return m > 0 ? `${m}m ${s}s` : `${s}s`;
  };

  return (
    <main style={{ maxWidth: 900, margin: "0 auto", padding: "2rem", fontFamily: "sans-serif" }}>
      <Link to={`/projects/${project.id}`}>&larr; Back to the wizard</Link>
      <h1 style={{ marginBottom: 0 }}>{project.title}</h1>
      <p style={{ color: "#a60", fontSize: "0.9em" }}>
        Advanced mode - raw pipeline state, provider names, generation prompts, and manual asset
        registration. The wizard is the supported way to make a video; this page exists for
        diagnosing a run that went wrong.
      </p>
      <p style={{ color: "#666" }}>
        Status: <strong>{project.status}</strong> &middot; {project.targetDurationSeconds}s target &middot; {project.aspectRatio}
      </p>

      <div style={{ marginBottom: "0.5rem", display: "flex", gap: "0.5rem", flexWrap: "wrap", alignItems: "center" }}>
        <button onClick={() => triggerAndPoll("script", () => contentProjectsApi.generate(id!))} disabled={!!busyAction}>
          {busyAction === "script" ? "Generating script..." : "1. Generate Script"}
        </button>
        <button
          onClick={() => triggerAndPoll("qa", () => pipelineApi.runQa(id!))}
          disabled={!!busyAction || !hasScript}
          title={hasScript ? undefined : "Generate a script first"}
        >
          {busyAction === "qa" ? "Scoring script..." : "2. Run QA (before spending on video)"}
        </button>
        <button
          onClick={() => triggerAndPoll("assets", () => pipelineApi.generateAssets(id!))}
          disabled={!!busyAction || !hasScenes}
          title={hasScenes ? undefined : "Set up the clip plan below first"}
        >
          {busyAction === "assets" ? "Generating clips (can take 20+ min)..." : "4. Generate Clips"}
        </button>
        <button onClick={() => loadAll()} disabled={!!busyAction}>
          Refresh
        </button>
      </div>
      <p style={{ fontSize: "0.8em", color: "#a60", marginTop: 0, marginBottom: "0.75rem" }}>
        🪙 Steps 1 &amp; 2 call the text model — tokens, counts toward the AI spend cap. 💸 Step 4 runs Veo:
        real per-second video billing. Refresh is free.
      </p>

      {busyAction && (
        <p style={{ color: "#0a6", fontWeight: "bold" }}>
          Working ({formatElapsed(elapsedSeconds)} elapsed) - please don't close this tab, the job keeps running on
          the server but you'll lose the live view.
        </p>
      )}

      {error && <p style={{ color: "crimson" }}>{error}</p>}

      <ScriptSection contentProjectId={project.id} script={script} onSaved={setScript} disabled={!!busyAction} />

      <ReferenceImagesSection references={references} />

      <ClipPlanSection
        contentProjectId={project.id}
        storyboard={storyboard}
        onChanged={setStoryboard}
        disabled={!!busyAction}
        defaultTotalDuration={project.targetDurationSeconds}
      />

      <AssetsSection contentProjectId={project.id} assets={assets} onChanged={setAssets} disabled={!!busyAction} />

      <section style={sectionStyle}>
        <h2>5. Render Video</h2>
        <RenderControls
          contentProjectId={project.id}
          disabled={!!busyAction || !hasReadyClipPerScene}
          disabledReason={hasReadyClipPerScene ? undefined : "Generate clips first - not every scene has a ready clip yet"}
          onTrigger={triggerAndPoll}
        />
        {finalVideoAsset ? (
          <video
            controls
            style={{ maxWidth: "100%", maxHeight: 500, marginTop: "1rem" }}
            src={assetFileUrl(project.id, finalVideoAsset.id)}
          />
        ) : (
          <p style={{ color: "#666" }}>No rendered video yet.</p>
        )}
      </section>

      <section style={sectionStyle}>
        <h2>QA (pre-flight script check)</h2>
        {latestQaScore ? (
          <>
            <p>
              <strong>Overall: {latestQaScore.overall.toFixed(1)} / 10</strong>
            </p>
            <ul style={{ paddingLeft: "1.2rem" }}>
              <li>Hook: {latestQaScore.hook.toFixed(1)}</li>
              <li>Story: {latestQaScore.story.toFixed(1)}</li>
              <li>Pacing: {latestQaScore.pacing.toFixed(1)}</li>
              <li>Factual accuracy: {latestQaScore.factualAccuracy.toFixed(1)}</li>
              <li>Platform suitability: {latestQaScore.platformSuitability.toFixed(1)}</li>
            </ul>
            <p>
              <em>Notes:</em> {latestQaScore.notes}
            </p>
          </>
        ) : (
          <p style={{ color: "#666" }}>No QA run yet - click "Run QA" above once a script exists.</p>
        )}

        {project.status === "AwaitingApproval" && (
          <div style={{ marginTop: "1rem" }}>
            <button onClick={() => handleApprove(true)}>Approve</button>{" "}
            <button onClick={() => handleApprove(false)}>Reject</button>
          </div>
        )}
        {(project.status === "Approved" || project.status === "Rejected") && (
          <p>
            <strong>{project.status}</strong>
          </p>
        )}
      </section>
    </main>
  );
}

function RenderControls({
  contentProjectId,
  disabled,
  disabledReason,
  onTrigger,
}: {
  contentProjectId: string;
  disabled: boolean;
  disabledReason?: string;
  onTrigger: (actionName: string, trigger: () => Promise<unknown>) => Promise<void>;
}) {
  return (
    <div style={{ display: "flex", gap: "0.75rem", alignItems: "center", flexWrap: "wrap" }}>
      <button
        onClick={() => onTrigger("render", () => pipelineApi.render(contentProjectId))}
        disabled={disabled}
        title={disabledReason}
      >
        Render Video
      </button>
      <span style={{ color: "#666", fontSize: "0.9em" }}>
        Caption styling (including turning captions off) is part of the project's caption settings -
        edit it in the wizard's preview step, then re-render. Re-rendering never regenerates clips.
      </span>
    </div>
  );
}

function ScriptSection({
  contentProjectId,
  script,
  onSaved,
  disabled,
}: {
  contentProjectId: string;
  script: Script | null;
  onSaved: (s: Script) => void;
  disabled: boolean;
}) {
  const [hook, setHook] = useState(script?.hook ?? "");
  const [introduction, setIntroduction] = useState(script?.introduction ?? "");
  const [body, setBody] = useState(script?.body ?? "");
  const [escalation, setEscalation] = useState(script?.escalation ?? "");
  const [payoff, setPayoff] = useState(script?.payoff ?? "");
  const [callToAction, setCallToAction] = useState(script?.callToAction ?? "");
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    setHook(script?.hook ?? "");
    setIntroduction(script?.introduction ?? "");
    setBody(script?.body ?? "");
    setEscalation(script?.escalation ?? "");
    setPayoff(script?.payoff ?? "");
    setCallToAction(script?.callToAction ?? "");
  }, [script]);

  const handleSave = async () => {
    setSaving(true);
    try {
      const updated = await scriptApi.upsert(contentProjectId, { hook, introduction, body, escalation, payoff, callToAction });
      onSaved(updated);
    } finally {
      setSaving(false);
    }
  };

  return (
    <section style={sectionStyle}>
      <h2>Script</h2>
      <div style={{ display: "grid", gap: "0.5rem" }}>
        <label>
          Hook
          <textarea value={hook} onChange={(e) => setHook(e.target.value)} rows={2} style={{ width: "100%" }} />
        </label>
        <label>
          Introduction
          <textarea value={introduction} onChange={(e) => setIntroduction(e.target.value)} rows={2} style={{ width: "100%" }} />
        </label>
        <label>
          Body
          <textarea value={body} onChange={(e) => setBody(e.target.value)} rows={3} style={{ width: "100%" }} />
        </label>
        <label>
          Escalation
          <textarea value={escalation} onChange={(e) => setEscalation(e.target.value)} rows={2} style={{ width: "100%" }} />
        </label>
        <label>
          Payoff
          <textarea value={payoff} onChange={(e) => setPayoff(e.target.value)} rows={2} style={{ width: "100%" }} />
        </label>
        <label>
          Call to action
          <textarea value={callToAction} onChange={(e) => setCallToAction(e.target.value)} rows={2} style={{ width: "100%" }} />
        </label>
        <button onClick={handleSave} disabled={saving || disabled} style={{ justifySelf: "start" }}>
          {saving ? "Saving..." : "Save script"}
        </button>
      </div>
    </section>
  );
}

function ReferenceImagesSection({ references }: { references: AssetReferenceSlots | null }) {
  const slots = references ? [references.character, references.environment] : [];
  return (
    <section style={sectionStyle}>
      <h2>Asset References</h2>
      <p style={{ color: "#666" }}>
        Managed in the wizard's "Ảnh mẫu" step now. Read-only here.
      </p>
      <div style={{ display: "flex", gap: "0.75rem", flexWrap: "wrap" }}>
        {slots.map((slot) => (
          <div key={slot.type} style={{ fontSize: "0.9em" }}>
            <div>
              <strong>{slot.type}</strong>: {slot.status}
            </div>
            {slot.imageUrl && (
              <img
                src={`${import.meta.env.VITE_API_BASE_URL ?? "http://localhost:8080"}${slot.imageUrl}`}
                alt={slot.type}
                style={{ width: 140, height: 140, objectFit: "cover", borderRadius: 6, border: "1px solid #ccc" }}
              />
            )}
          </div>
        ))}
      </div>
    </section>
  );
}

function ClipPlanSection({
  contentProjectId,
  storyboard,
  onChanged,
  disabled,
  defaultTotalDuration,
}: {
  contentProjectId: string;
  storyboard: Storyboard | null;
  onChanged: (sb: Storyboard) => void;
  disabled: boolean;
  defaultTotalDuration: number;
}) {
  const [totalDuration, setTotalDuration] = useState(defaultTotalDuration);
  const [clipDuration, setClipDuration] = useState(8);
  const [generating, setGenerating] = useState(false);
  const [editingSceneId, setEditingSceneId] = useState<string | null>(null);
  const [editText, setEditText] = useState("");

  const clipCount = Math.max(1, Math.round(totalDuration / clipDuration));

  const handleGeneratePlan = async () => {
    setGenerating(true);
    try {
      const updated = await clipPlanApi.generate(contentProjectId, { clipCount, clipDurationSeconds: clipDuration });
      onChanged(updated);
    } finally {
      setGenerating(false);
    }
  };

  const startEdit = (scene: Scene) => {
    setEditingSceneId(scene.id);
    setEditText(scene.narration);
  };

  const saveEdit = async (scene: Scene) => {
    const updated = await storyboardApi.updateScene(contentProjectId, scene.id, {
      durationSeconds: scene.durationSeconds,
      narration: editText,
      visualDescription: scene.visualDescription,
      cameraDirection: scene.cameraDirection,
      visualType: "aiVideo",
    });
    onChanged(updated);
    setEditingSceneId(null);
  };

  const handleRemove = async (sceneId: string) => {
    const updated = await storyboardApi.removeScene(contentProjectId, sceneId);
    onChanged(updated);
  };

  const scenes: Scene[] = storyboard?.scenes ?? [];

  return (
    <section style={sectionStyle}>
      <h2>Clip Plan ({scenes.length} clips)</h2>
      <p style={{ color: "#666" }}>
        Set your total video length and clip length (Veo generates in ~8s units) - the script gets split evenly
        across that many clips. Regenerating replaces the current plan.
      </p>
      <div style={{ display: "flex", gap: "0.75rem", alignItems: "center", flexWrap: "wrap", marginBottom: "1rem" }}>
        <label>
          Total duration (s):{" "}
          <input
            type="number"
            min={clipDuration}
            value={totalDuration}
            onChange={(e) => setTotalDuration(Number(e.target.value))}
            style={{ width: "5rem" }}
          />
        </label>
        <label>
          Clip length (s):{" "}
          <input
            type="number"
            min={1}
            max={60}
            value={clipDuration}
            onChange={(e) => setClipDuration(Number(e.target.value))}
            style={{ width: "4rem" }}
          />
        </label>
        <span style={{ color: "#666" }}>= {clipCount} clips</span>
        <button onClick={handleGeneratePlan} disabled={generating || disabled}>
          {generating ? "Generating..." : "Generate Clip Plan"}
        </button>
      </div>

      {scenes.length === 0 ? (
        <p style={{ color: "#666" }}>No clips planned yet.</p>
      ) : (
        <ol style={{ paddingLeft: "1.2rem" }}>
          {scenes.map((scene) => (
            <li key={scene.id} style={{ marginBottom: "0.75rem" }}>
              <strong>Clip {scene.sceneNumber}</strong> &middot; {scene.durationSeconds}s &middot; {scene.status}
              <br />
              {editingSceneId === scene.id ? (
                <>
                  <textarea
                    value={editText}
                    onChange={(e) => setEditText(e.target.value)}
                    rows={2}
                    style={{ width: "100%" }}
                  />
                  <button onClick={() => saveEdit(scene)} disabled={disabled}>
                    Save
                  </button>{" "}
                  <button onClick={() => setEditingSceneId(null)}>Cancel</button>
                </>
              ) : (
                <>
                  {scene.narration}{" "}
                  <button onClick={() => startEdit(scene)} disabled={disabled}>
                    Edit
                  </button>{" "}
                  <button onClick={() => handleRemove(scene.id)} disabled={disabled}>
                    Remove
                  </button>
                </>
              )}
              {scene.generationPrompt && (
                <div style={{ color: "#666", fontSize: "0.9em" }}>
                  <em>Generation prompt:</em> {scene.generationPrompt}
                </div>
              )}
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}

function AssetsSection({
  contentProjectId,
  assets,
  onChanged,
  disabled,
}: {
  contentProjectId: string;
  assets: Asset[];
  onChanged: (a: Asset[]) => void;
  disabled: boolean;
}) {
  const [type, setType] = useState<AssetType>("Music");
  const [provider, setProvider] = useState("");
  const [filePath, setFilePath] = useState("");
  const [submitting, setSubmitting] = useState(false);

  const handleAdd = async (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    try {
      await assetsApi.create(contentProjectId, { type, provider: provider || undefined, filePath: filePath || undefined });
      const updated = await assetsApi.getAll(contentProjectId);
      onChanged(updated);
      setProvider("");
      setFilePath("");
    } finally {
      setSubmitting(false);
    }
  };

  const handleRemove = async (assetId: string) => {
    await assetsApi.remove(contentProjectId, assetId);
    onChanged(await assetsApi.getAll(contentProjectId));
  };

  // Reference images have their own section above - don't show them twice here.
  const visibleAssets = assets;

  return (
    <section style={sectionStyle}>
      <h2>Assets ({visibleAssets.length})</h2>
      <p style={{ color: "#666" }}>
        Generated clips/voice show up here automatically. Manually register background music (project-level, no
        scene) below - it'll be mixed in at render time.
      </p>

      {visibleAssets.length === 0 ? (
        <p style={{ color: "#666" }}>None yet.</p>
      ) : (
        <ul style={{ paddingLeft: "1.2rem" }}>
          {visibleAssets.map((a) => (
            <li key={a.id}>
              <strong>{a.type}</strong> &middot; {a.status} {a.provider && `(${a.provider})`}{" "}
              {a.filePath && <code>{a.filePath}</code>}{" "}
              <button onClick={() => handleRemove(a.id)} disabled={disabled}>
                Remove
              </button>
            </li>
          ))}
        </ul>
      )}

      <h3>Register asset manually (e.g. background music)</h3>
      <form onSubmit={handleAdd} style={{ display: "grid", gap: "0.5rem", maxWidth: 500 }}>
        <label>
          Type:{" "}
          <select value={type} onChange={(e) => setType(e.target.value as AssetType)}>
            {ASSET_TYPES.map((t) => (
              <option key={t} value={t}>
                {t}
              </option>
            ))}
          </select>
        </label>
        <input placeholder="Provider (e.g. manual)" value={provider} onChange={(e) => setProvider(e.target.value)} />
        <input placeholder="File path or URL" value={filePath} onChange={(e) => setFilePath(e.target.value)} />
        <button type="submit" disabled={submitting || disabled} style={{ justifySelf: "start" }}>
          {submitting ? "Adding..." : "Add asset"}
        </button>
      </form>
    </section>
  );
}

export default AdvancedProjectPage;
