import { useEffect, useMemo, useRef, useState } from "react";
import {
  describeApiError,
  publishApi,
  type PublishJob,
  type PublishJobStatusName,
  type PublishPlatform,
  type PublishPlatformOptions,
  type SocialConnection,
} from "../api/client";
import { EmptyState, ErrorMessage, Loading, StatusBadge, type StatusTone } from "./components";

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

const STATUS_TONE: Record<PublishJobStatusName, StatusTone> = {
  Pending: "working",
  Scheduled: "working",
  Publishing: "working",
  Published: "success",
  Failed: "danger",
  Cancelled: "neutral",
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
  finalVideoUrl,
}: {
  contentProjectId: string;
  defaultTitle: string;
  defaultCaption: string;
  defaultHashtags: string;
  /** The wizard's current rendered video file URL - lets this panel tell whether a platform already published THIS exact video. */
  finalVideoUrl: string | null;
}) {
  const [connections, setConnections] = useState<SocialConnection[]>([]);
  const [jobs, setJobs] = useState<PublishJob[]>([]);
  // Only gates the very first fetch - later reloads (polling, after an
  // action) update connections/jobs in place without re-showing a spinner.
  const [loading, setLoading] = useState(true);
  const [selected, setSelected] = useState<Set<PublishPlatform>>(new Set());
  // Per-platform, independent: picking TikTok's privacy never touches YouTube's (or any other platform's) setting.
  const [platformOptions, setPlatformOptions] = useState<Partial<Record<PublishPlatform, PublishPlatformOptions>>>({});
  const [privacyByPlatform, setPrivacyByPlatform] = useState<Partial<Record<PublishPlatform, string>>>({});
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
    load().finally(() => setLoading(false));
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

  // A platform is "already published" only for the video currently loaded and
  // only for its currently connected account - matches the backend's own
  // duplicate-publish check (same video asset), so re-rendering a new video
  // (a different sourceVideoUrl) naturally re-enables it.
  const publishedForCurrentVideo = new Set<PublishPlatform>(
    finalVideoUrl
      ? jobs.filter((j) => j.status === "Published" && j.sourceVideoUrl === finalVideoUrl).map((j) => j.platform)
      : [],
  );
  const publishedKey = [...publishedForCurrentVideo].sort().join(",");

  // A platform that just became "already published" shouldn't sit checked-but-disabled.
  useEffect(() => {
    if (!publishedKey) return;
    setSelected((cur) => {
      const next = new Set(cur);
      let changed = false;
      for (const p of publishedKey.split(",") as PublishPlatform[]) {
        if (next.delete(p)) changed = true;
      }
      return changed ? next : cur;
    });
  }, [publishedKey]);

  const connectedPlatformsKey = connections
    .filter((c) => c.status === "Connected")
    .map((c) => c.platform)
    .sort()
    .join(",");

  // Load each connected platform's own privacy/visibility options independently -
  // TikTok's come from a live creator_info/query, so they must be re-fetched
  // whenever a platform (re)connects rather than assumed/cached across platforms.
  useEffect(() => {
    if (!connectedPlatformsKey) return;
    const platforms = connectedPlatformsKey.split(",") as PublishPlatform[];
    let cancelled = false;
    void Promise.all(
      platforms.map(async (p) => {
        try {
          const opts = await publishApi.publishOptions(p);
          return [p, opts] as const;
        } catch {
          return [p, null] as const;
        }
      }),
    ).then((results) => {
      if (cancelled) return;
      setPlatformOptions((cur) => {
        const next = { ...cur };
        for (const [p, opts] of results) {
          if (opts) next[p] = opts;
          else delete next[p];
        }
        return next;
      });
      setPrivacyByPlatform((cur) => {
        const next = { ...cur };
        for (const [p, opts] of results) {
          // Only seed a default the first time - never overwrite a choice the user already made for this platform.
          if (opts?.defaultPrivacy && next[p] === undefined) {
            next[p] = opts.defaultPrivacy;
          }
        }
        return next;
      });
    });
    return () => {
      cancelled = true;
    };
  }, [connectedPlatformsKey]);

  const toggle = (p: PublishPlatform) =>
    setSelected((cur) => {
      const next = new Set(cur);
      if (next.has(p)) next.delete(p);
      else next.add(p);
      return next;
    });

  // Setting one platform's privacy only ever touches that platform's own key.
  const setPrivacy = (p: PublishPlatform, value: string) =>
    setPrivacyByPlatform((cur) => ({ ...cur, [p]: value }));

  const connect = async (p: PublishPlatform) => {
    setError(null);
    try {
      const { authorizationUrl } = await publishApi.authorizeUrl(p);
      window.open(authorizationUrl, "_blank", "noopener,noreferrer");
    } catch (e) {
      setError(describeApiError(e, "Không mở được trang kết nối."));
    }
  };

  const disconnect = async (p: PublishPlatform) => {
    setError(null);
    try {
      await publishApi.disconnect(p);
      await load();
    } catch (e) {
      setError(describeApiError(e, "Không ngắt kết nối được."));
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
      setError(describeApiError(e, "Không chọn được Page."));
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
      const platformPrivacy: Partial<Record<PublishPlatform, string>> = {};
      for (const p of selectedList) {
        const value = privacyByPlatform[p];
        if (value) platformPrivacy[p] = value;
      }

      const result = await publishApi.publish(contentProjectId, {
        platforms: selectedList,
        title: title.trim(),
        caption: caption.trim() || undefined,
        hashtags: hashtags.trim() || undefined,
        mode,
        scheduledAt: mode === "schedule" ? localToUtcIso(scheduleAt) : undefined,
        platformPrivacy: Object.keys(platformPrivacy).length > 0 ? platformPrivacy : undefined,
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
      setError(describeApiError(e, "Không đăng được."));
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
      setError(describeApiError(e, "Không thử lại được."));
    }
  };

  const clearHistory = async () => {
    if (!window.confirm("Xóa các lịch sử đăng bị lỗi/đã huỷ? Các video đã đăng thành công sẽ được giữ nguyên.")) {
      return;
    }
    setError(null);
    try {
      // The server returns the authoritative remaining list - set it directly
      // instead of reloading, so a concurrent poll can't re-add a just-cleared row.
      const remaining = await publishApi.clearHistory(contentProjectId);
      setJobs(remaining);
    } catch (e) {
      setError(describeApiError(e, "Không xóa được lịch sử."));
    }
  };

  const connectedCount = connections.filter((c) => c.status === "Connected").length;

  return (
    <>
      {/* Hoisted above every card, not just the content-form one, so feedback
          from a Platforms-card action (connect/disconnect/select page) - or an
          OAuth-redirect notice - shows up right away instead of only after
          scrolling down to a card unrelated to what was just clicked. */}
      <ErrorMessage message={error} />
      {notice && <p className="wz-hint">{notice}</p>}

      {!loading && (
        <p className="wz-hint" style={{ marginBottom: 14 }}>
          {connectedCount}/{PLATFORMS.length} nền tảng đã kết nối
          {jobs.length > 0 && ` · ${jobs.length} lần đăng cho video này`}
        </p>
      )}

      <section className="wz-card">
        <h2>Nền tảng</h2>
        <p className="wz-sub">
          Kết nối tài khoản rồi chọn nơi muốn đăng. Client secret và token nằm hoàn toàn ở máy chủ.
        </p>

        {loading ? (
          <Loading />
        ) : (
        <ul style={{ listStyle: "none", padding: 0, margin: 0 }}>
          {PLATFORMS.map(({ value, label }) => {
            const conn = connectionFor(value);
            const connected = conn?.status === "Connected";
            const configured = conn?.configured ?? false;
            const needsPage = conn?.status === "PendingSelection";
            // Published already for THIS video, on THIS connected account - not a
            // global platform lock. A different account (after reconnecting) or a
            // freshly rendered video (different sourceVideoUrl) is unaffected.
            const alreadyPublished = connected && publishedForCurrentVideo.has(value);
            const publishedJob = alreadyPublished
              ? jobs.find((j) => j.platform === value && j.status === "Published" && j.sourceVideoUrl === finalVideoUrl)
              : undefined;
            const connectionTone: StatusTone =
              alreadyPublished || connected ? "success" : conn?.status === "Expired" ? "danger" : needsPage ? "working" : "neutral";
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
                    disabled={!connected || alreadyPublished}
                    onChange={() => toggle(value)}
                  />
                  <strong>{label}</strong>
                </label>

                <StatusBadge tone={connectionTone}>
                  {alreadyPublished
                    ? "✓ Đã đăng video này"
                    : connected
                      ? `Đã kết nối${conn?.accountName ? ` · ${conn.accountName}` : ""}${conn?.accountId ? ` · ID ${conn.accountId}` : ""}`
                      : conn?.status === "Expired"
                        ? "Hết hạn"
                        : needsPage
                          ? "Chọn Page"
                          : "Chưa kết nối"}
                </StatusBadge>

                {alreadyPublished && publishedJob?.publishedUrl && (
                  <a href={publishedJob.publishedUrl} target="_blank" rel="noreferrer">
                    Mở bài đăng ↗
                  </a>
                )}

                <span style={{ marginLeft: "auto", display: "flex", gap: 6 }}>
                  {connected ? (
                    <button type="button" className="wz-btn wz-btn-sm wz-btn-danger" onClick={() => disconnect(value)}>
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
                    <label className="wz-hint" htmlFor={`fb-page-${value}`}>
                      Facebook Page:
                    </label>
                    <select
                      id={`fb-page-${value}`}
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

                {/* Each platform's own privacy/visibility setting - only shown when that
                    platform actually supports one (TikTok's list comes live from
                    creator_info/query; YouTube's is fixed; Instagram/Facebook show nothing). */}
                {connected && (platformOptions[value]?.privacyOptions.length ?? 0) > 0 && (
                  <div style={{ flexBasis: "100%", display: "flex", flexDirection: "column", gap: 4, marginTop: 4 }}>
                    <div style={{ display: "flex", gap: 6, alignItems: "center" }}>
                      <label className="wz-hint" htmlFor={`privacy-${value}`}>
                        {label} - chế độ hiển thị:
                      </label>
                      <select
                        id={`privacy-${value}`}
                        value={privacyByPlatform[value] ?? platformOptions[value]?.defaultPrivacy ?? ""}
                        onChange={(e) => setPrivacy(value, e.target.value)}
                      >
                        {platformOptions[value]!.privacyOptions.map((o) => (
                          <option key={o.value} value={o.value}>
                            {o.label}
                          </option>
                        ))}
                      </select>
                    </div>
                    {platformOptions[value]?.notice && (
                      <p className="wz-hint" style={{ margin: 0 }}>
                        {platformOptions[value]!.notice}
                      </p>
                    )}
                  </div>
                )}
              </li>
            );
          })}
        </ul>
        )}
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

        <div className="wz-actions">
          <button type="button" className="wz-btn wz-btn-primary" disabled={!canSubmit} onClick={submit}>
            {submitting ? "Đang gửi..." : "🚀 Đăng / Lên lịch video"}
          </button>
        </div>
        {selectedList.length > 0 && !anySelectedConnected && (
          <p className="wz-hint">Chưa có nền tảng nào trong số đã chọn được kết nối.</p>
        )}
      </section>

      {loading ? (
        <section className="wz-card">
          <h2>Trạng thái đăng</h2>
          <Loading />
        </section>
      ) : jobs.length === 0 ? (
        <section className="wz-card">
          <h2>Trạng thái đăng</h2>
          <EmptyState
            title="Chưa có lần đăng nào"
            description={'Chọn một nền tảng đã kết nối ở trên rồi bấm "Đăng / Lên lịch video" - trạng thái từng lần đăng sẽ hiện ở đây.'}
          />
        </section>
      ) : (
        <section className="wz-card">
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 10 }}>
            <h2 style={{ margin: 0 }}>Trạng thái đăng</h2>
            {jobs.some((j) => j.status === "Failed" || j.status === "Cancelled") && (
              <button type="button" className="wz-btn wz-btn-sm" onClick={clearHistory}>
                Xóa lịch sử
              </button>
            )}
          </div>
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
                <StatusBadge tone={STATUS_TONE[j.status]}>{STATUS_LABEL[j.status]}</StatusBadge>
                {j.privacy && (
                  <span className="wz-hint">
                    {platformOptions[j.platform]?.privacyOptions.find((o) => o.value === j.privacy)?.label ?? j.privacy}
                  </span>
                )}
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
