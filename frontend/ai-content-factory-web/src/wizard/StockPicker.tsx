import { useState } from "react";
import { describeApiError, stockApi, type StockVideo } from "../api/client";
import { CostNote, ErrorMessage } from "./components";

const STOP_WORDS = new Set(
  (
    "a an the and or of in on at to into onto from with without by for as is are was were be been its it this that these those " +
    "his her their its through across over under past near while very slowly quickly gently softly some each every one two " +
    "lit light shot frame scene camera behind front beside toward towards against"
  ).split(" "),
);

/** A short English search query from the scene's own visual prompt: the first few content words. */
function stockQueryFrom(prompt: string | null | undefined): string {
  if (!prompt) return "";
  return prompt
    .toLowerCase()
    .replace(/[^a-z\s-]/g, " ")
    .split(/\s+/)
    .filter((w) => w.length > 2 && !STOP_WORDS.has(w))
    .slice(0, 4)
    .join(" ");
}

/**
 * Free stock footage for one scene (Pexels). Nothing is requested until the
 * user presses "Tìm" - a project with many scenes never fires searches on load.
 */
export function StockPicker({
  prompt,
  disabled,
  onImport,
}: {
  /** The scene's visual prompt - pre-fills the search. */
  prompt: string | null | undefined;
  disabled: boolean;
  onImport: (videoId: string) => void;
}) {
  const [query, setQuery] = useState(() => stockQueryFrom(prompt));
  const [results, setResults] = useState<StockVideo[] | null>(null);
  const [searching, setSearching] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const search = async () => {
    setSearching(true);
    setError(null);
    try {
      setResults(await stockApi.search(query.trim()));
    } catch (err) {
      setError(describeApiError(err, "Không tìm được video stock."));
    } finally {
      setSearching(false);
    }
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
      <p className="wz-hint" style={{ margin: 0 }}>
        Hợp với cảnh <strong>không có nhân vật chính</strong> (phong cảnh, đám đông, đồ vật). Video dọc từ Pexels, dùng
        miễn phí; tìm bằng tiếng Anh cho kết quả tốt nhất.
      </p>
      <div className="wz-actions" style={{ marginTop: 0 }}>
        <input
          type="text"
          value={query}
          maxLength={100}
          placeholder="ví dụ: ancient egypt temple"
          disabled={disabled || searching}
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter" && query.trim()) void search();
          }}
          style={{ flex: "1 1 200px" }}
        />
        <button type="button" className="wz-btn wz-btn-sm" disabled={disabled || searching || !query.trim()} onClick={search}>
          {searching ? "Đang tìm..." : "Tìm"}
        </button>
        <CostNote kind="free">Pexels</CostNote>
      </div>
      <ErrorMessage message={error} />
      {results && results.length === 0 && <p className="wz-hint" style={{ margin: 0 }}>Không có kết quả - thử từ khóa khác.</p>}
      {results && results.length > 0 && (
        <>
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(96px, 1fr))", gap: 8 }}>
            {results.map((v) => (
              <div key={v.id} style={{ display: "flex", flexDirection: "column", gap: 4 }}>
                <a href={v.pageUrl} target="_blank" rel="noreferrer" title="Xem trên Pexels">
                  <img
                    src={v.previewImageUrl}
                    alt={`Pexels video ${v.id}`}
                    loading="lazy"
                    style={{ width: "100%", aspectRatio: "9 / 16", objectFit: "cover", borderRadius: 6, border: "1px solid var(--border)" }}
                  />
                </a>
                <span className="wz-hint" style={{ margin: 0 }}>
                  {Math.round(v.durationSeconds)}s ·{" "}
                  <a href={v.authorUrl} target="_blank" rel="noreferrer">
                    {v.author}
                  </a>
                </span>
                <button
                  type="button"
                  className="wz-btn wz-btn-sm"
                  disabled={disabled}
                  onClick={() => {
                    if (window.confirm("Dùng video Pexels này làm clip cho cảnh? Clip hiện tại của cảnh (nếu có) sẽ bị thay.")) {
                      onImport(v.id);
                    }
                  }}
                >
                  Dùng video này
                </button>
              </div>
            ))}
          </div>
          <p className="wz-hint" style={{ margin: 0 }}>
            Video cung cấp bởi{" "}
            <a href="https://www.pexels.com" target="_blank" rel="noreferrer">
              Pexels
            </a>
            .
          </p>
        </>
      )}
    </div>
  );
}
