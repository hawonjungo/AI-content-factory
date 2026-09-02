import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import "../ui/wizard.css";
import { contentProjectsApi, type ContentProject } from "../api/client";

/**
 * Coarse list-level summary. The authoritative status-to-user mapping lives
 * server-side in WizardStepResolver and is exposed per project through
 * /overview; this only needs enough to sort a list at a glance, so it buckets
 * rather than restating that logic.
 */
function summarize(status: string): { label: string; className: string } {
  switch (status) {
    case "Draft":
    case "Researching":
      return { label: "Nháp", className: "" };
    case "Approved":
    case "Published":
      return { label: "Đã xong", className: "wz-badge-ready" };
    case "Failed":
      return { label: "Lỗi", className: "wz-badge-failed" };
    case "AwaitingApproval":
      return { label: "Chờ duyệt", className: "wz-badge-ready" };
    default:
      return { label: "Đang làm", className: "wz-badge-working" };
  }
}

function ContentProjectsListPage() {
  const [projects, setProjects] = useState<ContentProject[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    contentProjectsApi
      .getAll()
      .then(setProjects)
      .catch((err) => setError(err instanceof Error ? err.message : "Không tải được danh sách video."))
      .finally(() => setLoading(false));
  }, []);

  return (
    <main className="wz">
      <h1>AI Content Factory</h1>
      <p className="wz-sub">Chọn một video để tiếp tục, hoặc bắt đầu một video mới.</p>

      <div className="wz-actions" style={{ marginTop: 0, marginBottom: 24 }}>
        <Link className="wz-btn wz-btn-primary" to="/projects/new">
          Tạo video mới
        </Link>
      </div>

      {error && <p className="wz-error">{error}</p>}

      {loading ? (
        <p>Đang tải...</p>
      ) : projects.length === 0 ? (
        <p className="wz-hint">Chưa có video nào.</p>
      ) : (
        <div style={{ display: "grid", gap: 10 }}>
          {projects.map((project) => {
            const summary = summarize(project.status);
            return (
              <Link
                key={project.id}
                to={`/projects/${project.id}`}
                className="wz-card"
                style={{ display: "block", marginBottom: 0, textDecoration: "none" }}
              >
                <div style={{ display: "flex", gap: 10, alignItems: "center", flexWrap: "wrap" }}>
                  <strong style={{ color: "var(--text-h)" }}>{project.title}</strong>
                  <span className={`wz-badge ${summary.className}`}>{summary.label}</span>
                  <span className="wz-hint" style={{ marginLeft: "auto" }}>
                    {project.targetDurationSeconds}s
                  </span>
                </div>
                {project.topic && (
                  <p className="wz-hint" style={{ marginTop: 6 }}>
                    {project.topic}
                  </p>
                )}
              </Link>
            );
          })}
        </div>
      )}
    </main>
  );
}

export default ContentProjectsListPage;
