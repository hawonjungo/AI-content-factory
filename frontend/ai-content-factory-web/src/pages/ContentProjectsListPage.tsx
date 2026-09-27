import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import "../ui/wizard.css";
import { contentProjectsApi, describeApiError, type ContentProject } from "../api/client";
import { EmptyState, ErrorMessage, Loading, StatusBadge, type StatusTone } from "../wizard/components";
import { TopNav } from "../ui/TopNav";

/**
 * Coarse list-level summary. The authoritative status-to-user mapping lives
 * server-side in WizardStepResolver and is exposed per project through
 * /overview; this only needs enough to sort a list at a glance, so it buckets
 * rather than restating that logic.
 */
function summarize(status: string): { label: string; tone: StatusTone } {
  switch (status) {
    case "Draft":
    case "Researching":
      return { label: "Nháp", tone: "neutral" };
    case "Approved":
    case "Published":
      return { label: "Đã xong", tone: "success" };
    case "Failed":
      return { label: "Lỗi", tone: "danger" };
    case "AwaitingApproval":
      return { label: "Chờ duyệt", tone: "success" };
    default:
      return { label: "Đang làm", tone: "working" };
  }
}

function ContentProjectsListPage() {
  const [projects, setProjects] = useState<ContentProject[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [deletingId, setDeletingId] = useState<string | null>(null);

  useEffect(() => {
    contentProjectsApi
      .getAll()
      .then(setProjects)
      .catch((err) => setError(describeApiError(err, "Không tải được danh sách video.")))
      .finally(() => setLoading(false));
  }, []);

  const deleteProject = async (project: ContentProject) => {
    if (!window.confirm(`Xóa "${project.title}"? Hành động này không thể hoàn tác.`)) {
      return;
    }
    setError(null);
    setDeletingId(project.id);
    try {
      await contentProjectsApi.remove(project.id);
      // Server is the source of truth for what got deleted - drop it from
      // local state directly so it disappears immediately without a refetch race.
      setProjects((cur) => cur.filter((p) => p.id !== project.id));
    } catch (err) {
      setError(describeApiError(err, "Không xóa được video."));
    } finally {
      setDeletingId(null);
    }
  };

  // Most-recently-touched project first - the one thing a returning user is
  // most likely looking for. Client-side only; the API doesn't guarantee an
  // order and this needs no server change to add.
  const sortedProjects = [...projects].sort(
    (a, b) => new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime(),
  );

  return (
    <main className="wz">
      <TopNav />
      <h1>AI Content Factory</h1>
      <p className="wz-sub">
        Chọn một video để tiếp tục, hoặc bắt đầu một video mới.
        {projects.length > 0 && ` Bạn có ${projects.length} video.`}
      </p>

      <div className="wz-actions" style={{ marginTop: 0, marginBottom: 24 }}>
        <Link className="wz-btn wz-btn-primary" to="/projects/new">
          Tạo video mới
        </Link>
      </div>

      <ErrorMessage message={error} />

      {loading ? (
        <Loading />
      ) : projects.length === 0 ? (
        <EmptyState
          title="Chưa có video nào"
          description='Bấm "Tạo video mới" ở trên để bắt đầu - AI sẽ giúp bạn từ ý tưởng đến video hoàn chỉnh.'
        />
      ) : (
        <ul style={{ display: "grid", gap: 10, listStyle: "none", padding: 0, margin: 0 }}>
          {sortedProjects.map((project) => {
            const summary = summarize(project.status);
            // Failed projects need attention - a quiet border accent lets them
            // stand out from a longer list without adding another badge.
            const needsAttention = summary.tone === "danger";
            return (
              <li
                key={project.id}
                className={`wz-card${needsAttention ? " wz-card-alert" : ""}`}
                style={{ marginBottom: 0 }}
              >
                <div style={{ display: "flex", gap: 10, alignItems: "flex-start" }}>
                  <Link to={`/projects/${project.id}`} style={{ flex: 1, minWidth: 0, display: "block", textDecoration: "none" }}>
                    <div style={{ display: "flex", gap: 10, alignItems: "center", flexWrap: "wrap" }}>
                      <strong style={{ color: "var(--text-h)" }}>{project.title}</strong>
                      <StatusBadge tone={summary.tone}>{summary.label}</StatusBadge>
                      <span className="wz-hint">{project.targetDurationSeconds}s</span>
                      {project.niche && <span className="wz-hint">· {project.niche}</span>}
                      <span className="wz-hint">· Cập nhật {new Date(project.updatedAt).toLocaleDateString()}</span>
                    </div>
                    {project.topic && (
                      <p className="wz-hint" style={{ marginTop: 6 }}>
                        {project.topic}
                      </p>
                    )}
                  </Link>
                  <button
                    type="button"
                    className="wz-btn wz-btn-sm wz-btn-danger"
                    disabled={deletingId === project.id}
                    onClick={() => deleteProject(project)}
                    title="Xóa video này"
                  >
                    {deletingId === project.id ? "Đang xóa..." : "Xóa"}
                  </button>
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </main>
  );
}

export default ContentProjectsListPage;
