import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { contentProjectsApi, type ContentProject } from "../api/client";

function ContentProjectsListPage() {
  const [projects, setProjects] = useState<ContentProject[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [title, setTitle] = useState("");
  const [topic, setTopic] = useState("");
  const [niche, setNiche] = useState("");
  const [duration, setDuration] = useState(45);
  const [submitting, setSubmitting] = useState(false);

  const loadProjects = async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await contentProjectsApi.getAll();
      setProjects(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load content projects.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadProjects();
  }, []);

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!title.trim()) return;

    setSubmitting(true);
    setError(null);
    try {
      await contentProjectsApi.create({
        title,
        topic: topic || undefined,
        niche: niche || undefined,
        targetDurationSeconds: duration,
      });
      setTitle("");
      setTopic("");
      setNiche("");
      setDuration(45);
      await loadProjects();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to create content project.");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <main style={{ maxWidth: 800, margin: "0 auto", padding: "2rem", fontFamily: "sans-serif" }}>
      <h1>AI Content Factory</h1>
      <p style={{ color: "#666" }}>Content projects</p>

      <form onSubmit={handleCreate} style={{ display: "grid", gap: "0.5rem", marginBottom: "2rem" }}>
        <input
          placeholder="Title (required)"
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          required
        />
        <input placeholder="Topic" value={topic} onChange={(e) => setTopic(e.target.value)} />
        <input placeholder="Niche" value={niche} onChange={(e) => setNiche(e.target.value)} />
        <label>
          Target duration (seconds):{" "}
          <input
            type="number"
            min={1}
            max={180}
            value={duration}
            onChange={(e) => setDuration(Number(e.target.value))}
            style={{ width: "5rem" }}
          />
        </label>
        <button type="submit" disabled={submitting}>
          {submitting ? "Creating..." : "Create content project"}
        </button>
      </form>

      {error && <p style={{ color: "crimson" }}>{error}</p>}
      {loading ? (
        <p>Loading...</p>
      ) : projects.length === 0 ? (
        <p>No content projects yet. Create one above.</p>
      ) : (
        <table style={{ width: "100%", borderCollapse: "collapse" }}>
          <thead>
            <tr style={{ textAlign: "left", borderBottom: "1px solid #ccc" }}>
              <th>Title</th>
              <th>Topic</th>
              <th>Status</th>
              <th>Duration</th>
            </tr>
          </thead>
          <tbody>
            {projects.map((p) => (
              <tr key={p.id} style={{ borderBottom: "1px solid #eee" }}>
                <td>
                  <Link to={`/projects/${p.id}`}>{p.title}</Link>
                </td>
                <td>{p.topic ?? "-"}</td>
                <td>{p.status}</td>
                <td>{p.targetDurationSeconds}s</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </main>
  );
}

export default ContentProjectsListPage;
