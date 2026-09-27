import { useState } from "react";
import {
  contentIdeasApi,
  type ContentIdeaGoal,
  type ContentIdeaSuggestion,
  type ContentIdeaSuggestionsResult,
} from "../api/client";
import { CostNote, StatusBadge } from "./components";

const GOALS: { value: ContentIdeaGoal; label: string; hint: string }[] = [
  { value: "overall", label: "Tổng thể", hint: "Cân bằng lượt xem, giữ chân, kiếm tiền và độ bền của kênh." },
  { value: "monetization", label: "Kiếm tiền", hint: "Ưu tiên tiềm năng kiếm tiền, affiliate, khán giả giá trị cao." },
  { value: "views", label: "Lượt xem", hint: "Ưu tiên khả năng lan truyền, tò mò, dễ chia sẻ, hook mạnh." },
];

// value = niche name sent to the API (English, matches how the model reasons);
// "auto" lets the backend pick.
const NICHES: { value: string; label: string }[] = [
  { value: "auto", label: "Tự động / Khám phá" },
  { value: "AI & Technology", label: "AI & Công nghệ" },
  { value: "Money & Finance", label: "Tiền & Tài chính" },
  { value: "Business", label: "Kinh doanh" },
  { value: "Productivity", label: "Năng suất" },
  { value: "Psychology", label: "Tâm lý học" },
  { value: "Health & Fitness", label: "Sức khỏe & Thể hình" },
  { value: "Interesting Facts", label: "Sự thật thú vị" },
  { value: "History", label: "Lịch sử" },
  { value: "Science", label: "Khoa học" },
  { value: "Motivation", label: "Động lực" },
  { value: "Lifestyle", label: "Phong cách sống" },
  { value: "Kitten / Cat", label: "🐱 Kitten / Cat" },
  { value: "Gaming", label: "Game" },
  { value: "Other", label: "Khác" },
];

/**
 * Turns the Error thrown by the API client (`Request failed (<status>): <body>`)
 * into a specific, user-facing message. Prefers the backend's localised
 * ProblemDetails `detail`, then falls back by status, then a generic message.
 */
function describeError(err: unknown): string {
  const raw = err instanceof Error ? err.message : "";
  const status = Number(raw.match(/\((\d{3})\)/)?.[1]);

  const bodyStart = raw.indexOf("): ");
  if (bodyStart !== -1) {
    try {
      const problem = JSON.parse(raw.slice(bodyStart + 3)) as { detail?: string };
      if (problem.detail?.trim()) return problem.detail.trim();
    } catch {
      // body was not JSON - use the status-based messages below
    }
  }

  if (status === 429) {
    return "Đã đạt giới hạn chi phí / credit AI. Vui lòng thử lại sau, hoặc kiểm tra hạn mức (spend cap) của API key.";
  }
  if (status === 404) {
    return "Máy chủ chưa có API gợi ý nội dung (backend chưa được cập nhật). Hãy chạy lại backend bản mới nhất.";
  }
  return "Không thể tạo gợi ý lúc này. Vui lòng thử lại.";
}

interface Props {
  language: string;
  durationSeconds: number;
  /** The user's own idea text (title/topic), used to steer away from duplicates. */
  existingIdeas: string[];
  disabled?: boolean;
  /** Drops the picked idea into the normal Step 2 fields - it then behaves like a user-typed idea. */
  onUseIdea: (idea: ContentIdeaSuggestion) => void;
}

export function ContentIdeaSuggestions({ language, durationSeconds, existingIdeas, disabled, onUseIdea }: Props) {
  const [open, setOpen] = useState(false);
  const [goal, setGoal] = useState<ContentIdeaGoal>("overall");
  const [niche, setNiche] = useState<string>("auto");

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<ContentIdeaSuggestionsResult | null>(null);

  const generate = async () => {
    if (loading) return; // guard against duplicate requests
    setLoading(true);
    setError(null);
    try {
      // Fold the previous idea's title into "existing" so a re-roll avoids repeating it.
      const previousTitles = result?.ideas.map((i) => i.title) ?? [];
      const res = await contentIdeasApi.suggest({
        goal,
        niche,
        language,
        duration: durationSeconds,
        existingIdeas: [...existingIdeas, ...previousTitles].filter((s) => s.trim().length > 0).slice(0, 12),
      });
      setResult(res);
      // The backend now returns exactly one (its single best) idea - apply it
      // straight to the form the moment it arrives. No separate "pick one"
      // step: the user already sees it land in the Title/Content fields below.
      const best = res.ideas[0];
      if (best) onUseIdea(best);
    } catch (err) {
      // Production UI stays friendly; the real backend error (status + body from
      // handleResponse) goes to the console so it is diagnosable during dev.
      // Deliberately does not touch `result` or the form fields - a failed
      // request must never overwrite what the user already has.
      console.error("[AI content suggestions] request failed:", err);
      setError(describeError(err));
    } finally {
      setLoading(false);
    }
  };

  if (!open) {
    return (
      <div className="wz-ideas-teaser">
        <div>
          <strong>✨ AI Gợi ý</strong>
          <p className="wz-hint" style={{ margin: "2px 0 0" }}>
            Chưa có ý tưởng? Để AI chọn 1 ý tưởng tốt nhất và tự điền vào form cho bạn.
          </p>
        </div>
        <button type="button" className="wz-btn wz-btn-sm" disabled={disabled} onClick={() => setOpen(true)}>
          ✨ AI Gợi ý
        </button>
      </div>
    );
  }

  const controlsDisabled = disabled || loading;

  return (
    <div className="wz-ideas">
      <div className="wz-ideas-bar">
        <strong>✨ AI Gợi ý</strong>
        <button
          type="button"
          className="wz-btn wz-btn-sm"
          style={{ marginLeft: "auto" }}
          disabled={loading}
          onClick={() => setOpen(false)}
        >
          Thu gọn
        </button>
      </div>

      <div className="wz-ideas-controls">
        <div>
          <span className="wz-ideas-label">Mục tiêu</span>
          <div className="wz-seg">
            {GOALS.map((g) => (
              <button
                key={g.value}
                type="button"
                className="wz-seg-btn"
                aria-pressed={goal === g.value}
                disabled={controlsDisabled}
                title={g.hint}
                onClick={() => setGoal(g.value)}
              >
                {g.label}
              </button>
            ))}
          </div>
        </div>

        <label className="wz-ideas-niche">
          <span className="wz-ideas-label">Niche</span>
          <select value={niche} disabled={controlsDisabled} onChange={(e) => setNiche(e.target.value)}>
            {NICHES.map((n) => (
              <option key={n.value} value={n.value}>
                {n.label}
              </option>
            ))}
          </select>
        </label>
      </div>

      <p className="wz-hint" style={{ margin: "0 0 10px" }}>
        {GOALS.find((g) => g.value === goal)?.hint}
      </p>

      <div className="wz-actions" style={{ marginTop: 0 }}>
        <button type="button" className="wz-btn wz-btn-primary wz-btn-sm" disabled={controlsDisabled} onClick={generate}>
          {loading ? "Đang tạo..." : result ? "✨ AI Gợi ý khác" : "✨ AI Gợi ý"}
        </button>
        <CostNote kind="text">1 ý tưởng tốt nhất ≈ 1 lần gọi AI, tốn ít token · tự động điền vào form bên dưới</CostNote>
      </div>

      {loading && (
        <div className="wz-progress" role="status" aria-live="polite" style={{ marginTop: 12 }}>
          <strong>Đang phân tích cơ hội nội dung...</strong>
          <p className="wz-hint" style={{ margin: "4px 0 0" }}>Đang chọn ý tưởng tốt nhất...</p>
        </div>
      )}

      {error && !loading && (
        <div style={{ marginTop: 12 }}>
          <p className="wz-error" style={{ marginBottom: 8 }}>{error}</p>
          <button type="button" className="wz-btn wz-btn-sm" disabled={controlsDisabled} onClick={generate}>
            Thử lại
          </button>
        </div>
      )}

      {result && !loading && !error && (
        <>
          <p className="wz-hint" style={{ margin: "12px 0 10px" }}>ⓘ {result.disclaimer}</p>
          {result.ideas.length === 0 ? (
            <p className="wz-hint">AI không trả về ý tưởng nào. Hãy thử lại hoặc đổi mục tiêu / niche.</p>
          ) : (
            <div className="wz-ideas-grid">
              <IdeaCard idea={result.ideas[0]} />
            </div>
          )}
        </>
      )}
    </div>
  );
}

/**
 * Read-only summary of the one idea the AI picked - it's already been applied
 * to the form by the time this renders (see generate() above), so there is no
 * "use this idea" action here anymore, just confirmation of what landed and why.
 */
function IdeaCard({ idea }: { idea: ContentIdeaSuggestion }) {
  return (
    <div className="wz-idea-card" data-used="true">
      <div className="wz-idea-head">
        <strong>{idea.title}</strong>
      </div>
      <div className="wz-idea-niche">{idea.niche}</div>

      <p className="wz-idea-concept">{idea.concept}</p>
      <p className="wz-hint">
        <strong>Mở đầu:</strong> “{idea.hook}”
      </p>
      <p className="wz-hint">
        <strong>Vì sao nên làm:</strong> {idea.whyItWorks}
      </p>

      <div className="wz-idea-scores">
        <StatusBadge tone="success">Tiềm năng {idea.overallScore}/100</StatusBadge>
        <span className="wz-badge">Kiếm tiền {idea.monetizationScore}</span>
        <span className="wz-badge">Lan truyền {idea.viralScore}</span>
      </div>

      <StatusBadge tone="success">✓ Đã điền vào form bên dưới — chỉnh sửa nếu muốn</StatusBadge>
    </div>
  );
}
