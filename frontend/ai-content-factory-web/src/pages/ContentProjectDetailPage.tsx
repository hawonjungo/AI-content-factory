import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import {
  assetsApi,
  contentProjectsApi,
  scriptApi,
  storyboardApi,
  type Asset,
  type AssetType,
  type ContentProject,
  type Scene,
  type SceneVisualType,
  type Script,
  type Storyboard,
} from "../api/client";

const VISUAL_TYPES: SceneVisualType[] = [
  "aiVideo",
  "aiImage",
  "existingFootage",
  "motionGraphic",
  "textAnimation",
  "diagram",
];

const ASSET_TYPES: AssetType[] = ["Video", "Image", "Audio", "Voice", "Music", "Subtitle", "Thumbnail"];

const sectionStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 8,
  padding: "1rem",
  marginBottom: "1.5rem",
};

function ContentProjectDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [project, setProject] = useState<ContentProject | null>(null);
  const [script, setScript] = useState<Script | null>(null);
  const [storyboard, setStoryboard] = useState<Storyboard | null>(null);
  const [assets, setAssets] = useState<Asset[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [generating, setGenerating] = useState(false);
  const pollRef = useRef<number | null>(null);

  const loadAll = useCallback(async () => {
    if (!id) return;
    const [p, s, sb, a] = await Promise.all([
      contentProjectsApi.getById(id),
      scriptApi.get(id),
      storyboardApi.get(id),
      assetsApi.getAll(id),
    ]);
    setProject(p);
    setScript(s);
    setStoryboard(sb);
    setAssets(a);
    return p;
  }, [id]);

  useEffect(() => {
    setLoading(true);
    loadAll()
      .catch((err) => setError(err instanceof Error ? err.message : "Failed to load content project."))
      .finally(() => setLoading(false));

    return () => {
      if (pollRef.current) window.clearInterval(pollRef.current);
    };
  }, [loadAll]);

  const handleGenerate = async () => {
    if (!id) return;
    setError(null);
    setGenerating(true);
    try {
      await contentProjectsApi.generate(id);

      let ticks = 0;
      pollRef.current = window.setInterval(async () => {
        ticks += 1;
        const p = await loadAll().catch(() => null);
        if (p && (p.status === "StoryboardReady" || p.status === "Failed")) {
          setGenerating(false);
          if (pollRef.current) window.clearInterval(pollRef.current);
          if (p.status === "Failed") {
            setError("Generation failed. Check the API logs and verify that Llm:Gemini:ApiKey is configured.");
          }
        }
        if (ticks > 60) {
          // ~3 minutes at 3s interval - stop polling either way
          setGenerating(false);
          if (pollRef.current) window.clearInterval(pollRef.current);
        }
      }, 3000);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to start generation.");
      setGenerating(false);
    }
  };

  if (loading) return <main style={{ padding: "2rem" }}>Loading...</main>;
  if (!project) return <main style={{ padding: "2rem" }}>Content project not found.</main>;

  return (
    <main style={{ maxWidth: 900, margin: "0 auto", padding: "2rem", fontFamily: "sans-serif" }}>
      <Link to="/">&larr; All content projects</Link>
      <h1 style={{ marginBottom: 0 }}>{project.title}</h1>
      <p style={{ color: "#666" }}>
        Status: <strong>{project.status}</strong> &middot; {project.targetDurationSeconds}s &middot; {project.aspectRatio}
      </p>

      <div style={{ marginBottom: "1.5rem" }}>
        <button onClick={handleGenerate} disabled={generating}>
          {generating ? "Generating (script -> storyboard -> prompts)..." : "Generate with AI"}
        </button>{" "}
        <button onClick={() => loadAll()}>Refresh</button>
      </div>

      {error && <p style={{ color: "crimson" }}>{error}</p>}

      <ScriptSection contentProjectId={project.id} script={script} onSaved={setScript} />
      <StoryboardSection contentProjectId={project.id} storyboard={storyboard} onChanged={setStoryboard} />
      <AssetsSection contentProjectId={project.id} assets={assets} onChanged={setAssets} />
    </main>
  );
}

function ScriptSection({
  contentProjectId,
  script,
  onSaved,
}: {
  contentProjectId: string;
  script: Script | null;
  onSaved: (s: Script) => void;
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
        <button onClick={handleSave} disabled={saving} style={{ justifySelf: "start" }}>
          {saving ? "Saving..." : "Save script"}
        </button>
      </div>
    </section>
  );
}

function StoryboardSection({
  contentProjectId,
  storyboard,
  onChanged,
}: {
  contentProjectId: string;
  storyboard: Storyboard | null;
  onChanged: (sb: Storyboard) => void;
}) {
  const [narration, setNarration] = useState("");
  const [visualDescription, setVisualDescription] = useState("");
  const [cameraDirection, setCameraDirection] = useState("");
  const [duration, setDuration] = useState(5);
  const [visualType, setVisualType] = useState<SceneVisualType>("aiImage");
  const [submitting, setSubmitting] = useState(false);

  const handleAddScene = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!narration.trim()) return;
    setSubmitting(true);
    try {
      const updated = await storyboardApi.addScene(contentProjectId, {
        durationSeconds: duration,
        narration,
        visualDescription,
        cameraDirection,
        visualType,
      });
      onChanged(updated);
      setNarration("");
      setVisualDescription("");
      setCameraDirection("");
      setDuration(5);
    } finally {
      setSubmitting(false);
    }
  };

  const handleRemove = async (sceneId: string) => {
    const updated = await storyboardApi.removeScene(contentProjectId, sceneId);
    onChanged(updated);
  };

  const scenes: Scene[] = storyboard?.scenes ?? [];

  return (
    <section style={sectionStyle}>
      <h2>Storyboard ({scenes.length} scenes)</h2>

      {scenes.length === 0 ? (
        <p style={{ color: "#666" }}>No scenes yet.</p>
      ) : (
        <ol style={{ paddingLeft: "1.2rem" }}>
          {scenes.map((scene) => (
            <li key={scene.id} style={{ marginBottom: "0.75rem" }}>
              <strong>Scene {scene.sceneNumber}</strong> &middot; {scene.durationSeconds}s &middot; {scene.visualType} &middot; {scene.status}
              <br />
              <em>Narration:</em> {scene.narration}
              <br />
              <em>Visual:</em> {scene.visualDescription} {scene.cameraDirection && `(${scene.cameraDirection})`}
              {scene.generationPrompt && (
                <>
                  <br />
                  <em>Generation prompt:</em> {scene.generationPrompt}
                </>
              )}
              <br />
              <button onClick={() => handleRemove(scene.id)} style={{ marginTop: "0.25rem" }}>
                Remove scene
              </button>
            </li>
          ))}
        </ol>
      )}

      <h3>Add scene manually</h3>
      <form onSubmit={handleAddScene} style={{ display: "grid", gap: "0.5rem", maxWidth: 500 }}>
        <textarea placeholder="Narration" value={narration} onChange={(e) => setNarration(e.target.value)} rows={2} />
        <textarea
          placeholder="Visual description"
          value={visualDescription}
          onChange={(e) => setVisualDescription(e.target.value)}
          rows={2}
        />
        <input
          placeholder="Camera direction"
          value={cameraDirection}
          onChange={(e) => setCameraDirection(e.target.value)}
        />
        <label>
          Duration (seconds):{" "}
          <input type="number" min={1} value={duration} onChange={(e) => setDuration(Number(e.target.value))} style={{ width: "4rem" }} />
        </label>
        <label>
          Visual type:{" "}
          <select value={visualType} onChange={(e) => setVisualType(e.target.value as SceneVisualType)}>
            {VISUAL_TYPES.map((t) => (
              <option key={t} value={t}>
                {t}
              </option>
            ))}
          </select>
        </label>
        <button type="submit" disabled={submitting} style={{ justifySelf: "start" }}>
          {submitting ? "Adding..." : "Add scene"}
        </button>
      </form>
    </section>
  );
}

function AssetsSection({
  contentProjectId,
  assets,
  onChanged,
}: {
  contentProjectId: string;
  assets: Asset[];
  onChanged: (a: Asset[]) => void;
}) {
  const [type, setType] = useState<AssetType>("Video");
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

  return (
    <section style={sectionStyle}>
      <h2>Assets ({assets.length})</h2>

      {assets.length === 0 ? (
        <p style={{ color: "#666" }}>
          No assets yet. Manual import only for now - generate a clip elsewhere (e.g. Google Flow) and register it
          here with its file path.
        </p>
      ) : (
        <ul style={{ paddingLeft: "1.2rem" }}>
          {assets.map((a) => (
            <li key={a.id}>
              <strong>{a.type}</strong> &middot; {a.status} {a.provider && `(${a.provider})`}{" "}
              {a.filePath && <code>{a.filePath}</code>}{" "}
              <button onClick={() => handleRemove(a.id)}>Remove</button>
            </li>
          ))}
        </ul>
      )}

      <h3>Register asset manually</h3>
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
        <input placeholder="Provider (e.g. flow, manual)" value={provider} onChange={(e) => setProvider(e.target.value)} />
        <input placeholder="File path or URL" value={filePath} onChange={(e) => setFilePath(e.target.value)} />
        <button type="submit" disabled={submitting} style={{ justifySelf: "start" }}>
          {submitting ? "Adding..." : "Add asset"}
        </button>
      </form>
    </section>
  );
}

export default ContentProjectDetailPage;
