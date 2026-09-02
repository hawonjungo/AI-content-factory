import { useEffect, useMemo, useRef, useState } from "react";
import {
  publishApi,
  type PublishJob,
  type PublishJobStatusName,
  type PublishPlatform,
  type SocialConnection,
} from "../api/client";

const PLATFORMS: { value: PublishPlatform; label: string }[] = [
  { value: "TikTok", label: "TikTok" },
  { value: "YouTubeShorts", label: "YouTube Shorts" },
  { value: "InstagramReels", label: "Instagram Reels" },
  { value: "FacebookPage", label: "Facebook Page" },
];

const STATUS_LABEL: Record<PublishJobStatusName, string> = {
  Pending: "Đang chờ",
  Scheduled: "Đã lên lịch",
  Publishing: "Đang đăng",
  Published: "Đã đăng",
  Failed: "Lỗi",
  Cancelled: "Đã huỷ",
};

const STATUS_CLASS: Record<PublishJobStatusName, string> = {
  Pending: "wz-badge-working",
  Scheduled: "wz-badge-working",
  Publishing: "wz-badge-working",
  Published: "wz-badge-ready",
  Failed: "wz-badge-failed",
  Cancelled: "",
};

function fmt(dt: string | null): string {
  if (!dt) return "";
  const d = new Date(dt);
  return Number.isNaN(d.getTime()) ? "" : d.toLocaleString();
}

/** `datetime-local` value (local time) -> ISO-8601 UTC string. */
function localToUtcIso(local: string): string | undefined {
  if (!local) return undefined;
  const d = new Date(local);
  return Number.isNaN(d.getTime()) ? undefined : d.toISOString();
}

function defaultScheduleValue(): string {
  const d = new Date(Date.now() + 60 * 60 * 1000); // +1h
  d.setSeconds(0, 0);
  const pad = (n: number) => `${n}`.padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

export function PublishPanel({
  contentProjectId,
  defaultTitle,
  defaultCaption,
  defaultHashtags,
}: {
  contentProjectId: string;
  defaultTitle: string;
  defaultCaption: string;
  defaultHashtags: string;
}) {
  const [connections, setConnections] = useState<SocialConnection[]>([]);
  const [jobs, setJobs] = useState<PublishJob[]>([]);
  const [selected, setSelected] = useState<Set<PublishPlatform>>(new Set());
  const [title, setTitle] = useState(defaultTitle);
  const [caption, setCaption] = useState(defaultCaption);
  const [hashtags, setHashtags] = useState(defaultHashtags);
  const [mode, setMode] = useState<"now" | "schedule">("now");
  const [scheduleAt, setScheduleAt] = useState(defaultScheduleValue);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const submittingRef = useRef(false);

  const load = useMemo(
    () => async () => {
      const [c, j] = await Promise.all([
        publishApi.connections().catch(() => [] as SocialConnection[]),
        publishApi.jobs(contentProjectId).catch(() => [] as PublishJob[]),
      ]);
      setConnections(c);
      setJobs(j);
    },
    [contentProjectId],
  );

  useEffect(() => {
    void load();
    // Pick up an OAuth redirect result (?social_connected / ?social_select_page / ?social_error).
    const params = new URLSearchParams(window.location.search);
    if (params.get("social_connected")) {
      setNotice(`Đã kết nối ${params.get("social_connected")}.`);
    } else if (params.get("social_select_page")) {
      setNotice("Đã đăng nhập Facebook - hãy chọn Page bên dưới để hoàn tất.");
    } else if (params.get("social_error")) {
      setError(`Kết nối thất bại: ${params.get("social_error")}`);
    }
  }, [load]);

  // Poll while any job is still in flight.
  const hasInFlight = jobs.some((j) => j.status === "Pending" || j.status === "Scheduled" || j.status === "Publishing");
  useEffect(() => {
    if (!hasInFlight) return;
    const id = window.setInterval(() => void load(), 5000);
    return () => window.clearInterval(id);
  }, [hasInFlight, load]);

  const connectionFor = (p: PublishPlatform) => connections.find((c) => c.platform === p);

  const toggle = (p: PublishPlatform) =>
    setSelected((cur) => {
      const next = new Set(cur);
      if (next.has(p)) next.delete(p);
      else next.add(p);
      return next;
    });

  const connect = async (p: PublishPlatform) => {
    setError(null);
    try {
      const { authorizationUrl } = await publishApi.authorizeUrl(p);
      window.open(authorizationUrl, "_blank", "noopener,noreferrer");
    } catch (e) {
      setError(e instanceof Error ? e.message : "Không mở được trang kết nối.");
    }
  };

  const disconnect = async (p: PublishPlatform) => {
    setError(null);
    try {
      await publishApi.disconnect(p);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Không ngắt kết nối được.");
    }
  };

  const selectPage = async (p: PublishPlatform, pageId: string) => {
    if (!pageId) return;
    setError(null);
    try {
      await publishApi.selectPage(p, pageId);
      setNotice(null);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Không chọn được Page.");
    }
  };

  const selectedList = [...selected];
  const anySelectedConnected = selectedList.some((p) => connectionFor(p)?.status === "Connected");
  const scheduleInPast = mode === "schedule" && new Date(scheduleAt).getTime() <= Date.now();
  const canSubmit =
    !submitting &&
    selectedList.length > 0 &&
    anySelectedConnected &&
    title.trim().length > 0 &&
    !scheduleInPast;

  const submit = async () => {
    if (submittingRef.current || !canSubmit) return; // guard against double-click
    submittingRef.current = true;
    setSubmitting(true);
    setError(null);
    setNotice(null);
    try {
      const result = await publishApi.publish(contentProjectId, {
        platforms: selectedList,
        title: title.trim(),
        caption: caption.trim() || undefined,
        hashtags: hashtags.trim() || undefined,
        mode,
        scheduledAt: mode === "schedule" ? localToUtcIso(scheduleAt) : undefined,
      });
      const parts: string[] = [];
      if (result.created.length > 0) {
        parts.push(`${result.created.length} tác vụ đã ${mode === "schedule" ? "lên lịch" : "vào hàng đợi"}.`);
      }
      for (const s of result.skipped) {
        parts.push(`${s.platform}: ${s.reason}`);
      }
      setNotice(parts.join(" ") || "Không có gì để đăng.");
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Không đăng được.");
    } finally {
      submittingRef.current = false;
      setSubmitting(false);
    }
  };

  const retry = async (jobId: string) => {
    setError(null);
    try {
      await publishApi.retry(contentProjectId, jobId);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Không thử lại được.");
    }
  };

  return (
    <>
      <section className="wz-card">
        <h2>Nền tảng</h2>
        <p className="wz-sub">
          Kết nối tài khoản rồi chọn nơi muốn đăng. Client secret và token nằm hoàn toàn ở máy chủ.
        </p>

        <ul style={{ listStyle: "none", padding: 0, margin: 0 }}>
          {PLATFORMS.map(({ value, label }) => {
            const conn = connectionFor(value);
            const connected = conn?.status === "Connected";
            const configured = conn?.configured ?? false;
            const needsPage = conn?.status === "PendingSelection";
            return (
              <li
                key={value}
                className="wz-card"
                style={{ padding: 12, marginBottom: 8, display: "flex", alignItems: "center", gap: 10, flexWrap: "wrap" }}
              >
                <label style={{ display: "flex", alignItems: "center", gap: 8, fontSize: 14 }}>
                  <input
                    type="checkbox"
                    checked={selected.has(value)}
                    disabled={!connected}
                    onChange={() => toggle(value)}
                  />
                  <strong>{label}</strong>
                </label>

                <span
                  className={`wz-badge ${connected ? "wz-badge-ready" : conn?.status === "Expired" ? "wz-badge-failed" : needsPage ? "wz-badge-working" : ""}`}
                >
                  {connected
                    ? `Đã kết nối${conn?.accountName ? ` · ${conn.accountName}` : ""}${conn?.accountId ? ` · ID ${conn.accountId}` : ""}`
                    : conn?.status === "Expired"
                      ? "Hết hạn"
                      : needsPage
                        ? "Chọn Page"
                        : "Chưa kết nối"}
                </span>

                <span style={{ marginLeft: "auto", display: "flex", gap: 6 }}>
                  {connected ? (
                    <button type="button" className="wz-btn wz-btn-sm" onClick={() => disconnect(value)}>
                      Ngắt kết nối
                    </button>
                  ) : needsPage ? (
                    <button type="button" className="wz-btn wz-btn-sm" onClick={() => disconnect(value)}>
                      Huỷ
                    </button>
                  ) : (
                    <button
                      type="button"
                      className="wz-btn wz-btn-sm wz-btn-primary"
                      disabled={!configured}
                      title={configured ? undefined : "Nền tảng chưa được cấu hình client key/secret trên máy chủ"}
                      onClick={() => connect(value)}
                    >
                      {configured ? (value === "FacebookPage" ? "Kết nối Facebook" : "Kết nối tài khoản") : "Chưa cấu hình"}
                    </button>
                  )}
                </span>

                {needsPage && conn?.pages && conn.pages.length > 0 && (
                  <div style={{ flexBasis: "100%", display: "flex", gap: 6, alignItems: "center", marginTop: 4 }}>
                    <span className="wz-hint">Facebook Page:</span>
                    <select
                      value=""
                      onChange={(e) => selectPage(value, e.target.value)}
                    >
                      <option value="" disabled>
                        — chọn Page —
                      </option>
                      {conn.pages.map((pg) => (
                        <option key={pg.id} value={pg.id}>
                          {pg.name} ({pg.id})
                        </option>
                      ))}
                    </select>
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      </section>

      <section className="wz-card">
        <h2>Nội dung đăng</h2>

        <label className="wz-field">
          <span>Tiêu đề</span>
          <input type="text" value={title} maxLength={200} onChange={(e) => setTitle(e.target.value)} />
        </label>
        <label className="wz-field">
          <span>Mô tả / caption</span>
          <textarea rows={3} value={caption} onChange={(e) => setCaption(e.target.value)} />
        </label>
        <label className="wz-field">
          <span>Hashtag</span>
          <input
            type="text"
            value={hashtags}
            placeholder="#viral #ai #shorts"
            onChange={(e) => setHashtags(e.target.value)}
          />
        </label>

        <div className="wz-radios" style={{ marginTop: 6 }}>
          <label>
            <input type="radio" name="pubmode" checked={mode === "now"} onChange={() => setMode("now")} /> Đăng ngay
          </label>
          <label>
            <input type="radio" name="pubmode" checked={mode === "schedule"} onChange={() => setMode("schedule")} /> Lên lịch
          </label>
        </div>
        {mode === "schedule" && (
          <label className="wz-field" style={{ marginTop: 6 }}>
            <span>Thời điểm đăng</span>
            <input type="datetime-local" value={scheduleAt} onChange={(e) => setScheduleAt(e.target.value)} />
            {scheduleInPast && <span className="wz-error">Thời điểm phải ở tương lai.</span>}
          </label>
        )}

        {error && <p className="wz-error">{error}</p>}
        {notice && <p className="wz-hint">{notice}</p>}

        <div className="wz-actions">
          <button type="button" className="wz-btn wz-btn-primary" disabled={!canSubmit} onClick={submit}>
            {submitting ? "Đang gửi..." : "🚀 Đăng / Lên lịch video"}
          </button>
        </div>
        {selectedList.length > 0 && !anySelectedConnected && (
          <p className="wz-hint">Chưa có nền tảng nào trong số đã chọn được kết nối.</p>
        )}
      </section>

      {jobs.length > 0 && (
        <section className="wz-card">
          <h2>Trạng thái đăng</h2>
          <ul style={{ listStyle: "none", padding: 0, margin: 0 }}>
            {jobs.map((j) => (
              <li
                key={j.id}
                className="wz-card"
                style={{ padding: 12, marginBottom: 8, display: "flex", gap: 10, alignItems: "center", flexWrap: "wrap" }}
              >
                <strong style={{ fontSize: 13 }}>
                  {PLATFORMS.find((p) => p.value === j.platform)?.label ?? j.platform}
                </strong>
                <span className={`wz-badge ${STATUS_CLASS[j.status]}`}>{STATUS_LABEL[j.status]}</span>
                {j.scheduledAtUtc && j.status === "Scheduled" && (
                  <span className="wz-hint">lịch: {fmt(j.scheduledAtUtc)}</span>
                )}
                {j.publishedAtUtc && <span className="wz-hint">đăng: {fmt(j.publishedAtUtc)}</span>}
                {j.publishedUrl && (
                  <a href={j.publishedUrl} target="_blank" rel="noreferrer">
                    Mở bài đăng ↗
                  </a>
                )}
                {j.errorMessage && (
                  <span className="wz-error" style={{ flexBasis: "100%" }}>
                    {j.errorMessage}
                    {j.isPermanentFailure && " (lỗi vĩnh viễn)"}
                  </span>
                )}
                {j.canRetry && (
                  <button type="button" className="wz-btn wz-btn-sm" style={{ marginLeft: "auto" }} onClick={() => retry(j.id)}>
                    Thử lại
                  </button>
                )}
              </li>
            ))}
          </ul>
        </section>
      )}
    </>
  );
}
