import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import "../ui/wizard.css";
import { describeApiError, storiesApi, type StoryResponse } from "../api/client";
import { EmptyState, ErrorMessage, Field, Loading, StatusBadge, type StatusTone } from "../wizard/components";
import { StoryStylePicker } from "../wizard/StoryStylePicker";
import { TopNav } from "../ui/TopNav";

/**
 * Coarse status derived purely from episode statuses - the backend has no
 * story-level status field of its own.
 */
function summarizeStory(story: StoryResponse): { label: string; tone: StatusTone } {
  if (story.episodes.length === 0) return { label: "Mới tạo", tone: "neutral" };
  if (story.episodes.every((e) => e.status === "Completed")) return { label: "Hoàn thành", tone: "success" };
  return { label: "Đang tiến hành", tone: "working" };
}

function currentEpisodeLabel(story: StoryResponse): string {
  if (story.episodes.length === 0) return "Chưa có tập";
  const latest = [...story.episodes].sort((a, b) => b.episodeNumber - a.episodeNumber)[0];
  return `Tập ${latest.episodeNumber}: ${latest.title}`;
}

function StoriesListPage() {
  const navigate = useNavigate();
  const [stories, setStories] = useState<StoryResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [showCreate, setShowCreate] = useState(false);
  const [title, setTitle] = useState("");
  const [niche, setNiche] = useState("");
  const [premise, setPremise] = useState("");
  const [stylePresetId, setStylePresetId] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  useEffect(() => {
    storiesApi
      .getAll()
      .then(setStories)
      .catch((err) => setError(describeApiError(err, "Không tải được danh sách câu chuyện.")))
      .finally(() => setLoading(false));
  }, []);

  // Most-recently-touched story first, same convention as the video project list.
  const sortedStories = [...stories].sort(
    (a, b) => new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime(),
  );

  const handleCreate = async () => {
    if (!title.trim()) {
      setCreateError("Nhập tên câu chuyện.");
      return;
    }
    setCreating(true);
    setCreateError(null);
    try {
      const story = await storiesApi.create({
        title: title.trim(),
        premise: premise.trim() || undefined,
        niche: niche.trim() || undefined,
        stylePresetId: stylePresetId ?? undefined,
      });
      navigate(`/stories/${story.id}`);
    } catch (err) {
      setCreateError(describeApiError(err, "Không tạo được câu chuyện."));
    } finally {
      setCreating(false);
    }
  };

  return (
    <main className="wz">
      <TopNav />
      <h1>Chuỗi truyện</h1>
      <p className="wz-sub">
        Quản lý các câu chuyện nhiều tập với nhân vật, bối cảnh và mạch truyện xuyên suốt.
        {stories.length > 0 && ` Bạn có ${stories.length} câu chuyện.`}
      </p>

      <div className="wz-actions" style={{ marginTop: 0, marginBottom: 24 }}>
        <button type="button" className="wz-btn wz-btn-primary" onClick={() => setShowCreate((s) => !s)}>
          {showCreate ? "Đóng" : "Tạo Story mới"}
        </button>
      </div>

      {showCreate && (
        <div className="wz-card" style={{ marginBottom: 20 }}>
          <Field label="Tên câu chuyện">
            <input
              type="text"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="VD: Hành trình của Minh"
            />
          </Field>
          <Field label="Chủ đề (tùy chọn)" hint="Thể loại/chủ đề ngắn gọn, ví dụ: phiêu lưu, kinh dị, hài hước...">
            <input type="text" value={niche} onChange={(e) => setNiche(e.target.value)} />
          </Field>
          <Field label="Ý tưởng / tiền đề (tùy chọn)">
            <textarea
              rows={3}
              value={premise}
              onChange={(e) => setPremise(e.target.value)}
              placeholder="Tóm tắt ngắn gọn thế giới, nhân vật chính hoặc tình huống mở đầu..."
            />
          </Field>
          <StoryStylePicker selectedId={stylePresetId} onSelect={setStylePresetId} disabled={creating} />
          <p className="wz-hint" style={{ marginTop: 0 }}>
            Phong cách này dùng cho ảnh nhân vật và được kế thừa bởi video của mọi tập mới. Bạn có thể đổi lại sau trong
            trang chi tiết câu chuyện.
          </p>
          <ErrorMessage message={createError} />
          <div className="wz-actions">
            <button type="button" className="wz-btn wz-btn-primary" disabled={creating} onClick={handleCreate}>
              {creating ? "Đang tạo..." : "Tạo câu chuyện"}
            </button>
          </div>
        </div>
      )}

      <ErrorMessage message={error} />

      {loading ? (
        <Loading />
      ) : stories.length === 0 ? (
        <EmptyState
          title="Chưa có câu chuyện nào"
          description='Bấm "Tạo Story mới" ở trên để bắt đầu một chuỗi truyện nhiều tập với nhân vật và mạch truyện xuyên suốt.'
        />
      ) : (
        <ul style={{ display: "grid", gap: 10, listStyle: "none", padding: 0, margin: 0 }}>
          {sortedStories.map((story) => {
            const summary = summarizeStory(story);
            return (
              <li key={story.id} className="wz-card" style={{ marginBottom: 0 }}>
                <Link to={`/stories/${story.id}`} style={{ display: "block", textDecoration: "none" }}>
                  <div style={{ display: "flex", gap: 10, alignItems: "center", flexWrap: "wrap" }}>
                    <strong style={{ color: "var(--text-h)" }}>{story.title}</strong>
                    <StatusBadge tone={summary.tone}>{summary.label}</StatusBadge>
                    {story.niche && <span className="wz-hint">· {story.niche}</span>}
                    <span className="wz-hint">· {story.episodeCount} tập</span>
                    <span className="wz-hint">· Cập nhật {new Date(story.updatedAt).toLocaleDateString()}</span>
                  </div>
                  <p className="wz-hint" style={{ marginTop: 6 }}>
                    {currentEpisodeLabel(story)}
                  </p>
                  {story.premise && (
                    <p className="wz-hint" style={{ marginTop: 4 }}>
                      {story.premise}
                    </p>
                  )}
                </Link>
              </li>
            );
          })}
        </ul>
      )}
    </main>
  );
}

export default StoriesListPage;
