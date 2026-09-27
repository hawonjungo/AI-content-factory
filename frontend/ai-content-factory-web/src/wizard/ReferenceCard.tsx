import { useEffect, useState } from "react";
import {
  apiUrl,
  assetReferencesApi,
  describeApiError,
  type AssetReferenceSlot,
  type AssetReferenceType,
  type Pricing,
} from "../api/client";
import { StatusBadge, formatUsd } from "./components";

const TITLE: Record<AssetReferenceType, string> = {
  Character: "Nhân vật chính",
  Environment: "Bối cảnh chính",
};

const HINT: Record<AssetReferenceType, string> = {
  Character: "Ảnh nhân vật (người thật) giữ nguyên qua mọi cảnh - tạo bằng AI hoặc tải ảnh của bạn.",
  Environment: "Ảnh bối cảnh/không gian chính để các cảnh sau khớp màu và ánh sáng.",
};

// One image per generation - the character anchor is a single reusable portrait.
const VARIANT_COUNT = 1;

/**
 * One consistency anchor (Character or Environment). For Character the default
 * prompt is fetched, shown in an editable box, and the user's (possibly edited)
 * prompt is what gets sent - the original is never silently reused. The result
 * is exactly one image.
 */
export function ReferenceCard({
  contentProjectId,
  slot,
  pricing,
  busy,
  onChanged,
  onJobStarted,
}: {
  contentProjectId: string;
  slot: AssetReferenceSlot;
  pricing?: Pricing;
  busy: boolean;
  onChanged: () => void;
  onJobStarted: () => void;
}) {
  const type = slot.type;
  const isCharacter = type === "Character";
  // Configurable per-image cost from the pricing catalog; no hardcoded number.
  const imageUsd = pricing?.imageUsd ?? 0;

  const [prompt, setPrompt] = useState("");
  const [promptLoaded, setPromptLoaded] = useState(false);
  const [showPrompt, setShowPrompt] = useState(false); // Environment: optional custom prompt
  const [working, setWorking] = useState<null | "generate" | "upload" | "approve" | "skip" | "prompt">(null);
  const [error, setError] = useState<string | null>(null);
  const [picked, setPicked] = useState<string | null>(null);

  // Load the default character prompt for review/edit before any spend.
  useEffect(() => {
    if (!isCharacter || promptLoaded) return;
    let cancelled = false;
    setWorking("prompt");
    assetReferencesApi
      .getSuggestedPrompt(contentProjectId, "Character")
      .then((res) => {
        if (cancelled) return;
        setPrompt((cur) => (cur.trim() ? cur : res.prompt));
        setPromptLoaded(true);
      })
      .catch((err) => {
        if (!cancelled) setError(describeApiError(err, "Không tải được prompt mẫu."));
      })
      .finally(() => {
        if (!cancelled) setWorking(null);
      });
    return () => {
      cancelled = true;
    };
  }, [isCharacter, promptLoaded, contentProjectId]);

  const run = async (kind: NonNullable<typeof working>, fn: () => Promise<unknown>) => {
    setWorking(kind);
    setError(null);
    try {
      await fn();
      if (kind === "generate") onJobStarted();
      onChanged();
    } catch (err) {
      setError(describeApiError(err, "Thao tác không thành công."));
    } finally {
      setWorking(null);
    }
  };

  const disabled = busy || working !== null;
  const resolved = slot.status === "Approved" || slot.status === "Skipped";
  const singleResult = slot.variants.length === 1;

  const generate = (customPrompt?: string) =>
    run("generate", () =>
      assetReferencesApi.generate(contentProjectId, type, {
        count: VARIANT_COUNT,
        prompt: customPrompt?.trim() || undefined,
      }),
    );

  const reloadDefaultPrompt = () => {
    setWorking("prompt");
    setError(null);
    assetReferencesApi
      .getSuggestedPrompt(contentProjectId, "Character")
      .then((res) => setPrompt(res.prompt))
      .catch((err) => setError(describeApiError(err, "Không tải được prompt mẫu.")))
      .finally(() => setWorking(null));
  };

  return (
    <section className="wz-card" style={{ flex: "1 1 320px" }}>
      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 4 }}>
        <h3 style={{ margin: 0 }}>{TITLE[type]}</h3>
        {slot.status === "Approved" && <StatusBadge tone="success">Đã chốt</StatusBadge>}
        {slot.status === "Skipped" && <StatusBadge tone="neutral">Đã bỏ qua</StatusBadge>}
      </div>
      <p className="wz-hint" style={{ marginBottom: 12 }}>
        {HINT[type]}
      </p>
      <p className="wz-hint" style={{ marginBottom: 12 }}>
        💰 ~{formatUsd(imageUsd)} /ảnh × 1 ảnh ≈ {formatUsd(imageUsd)} mỗi lần tạo.
      </p>

      {slot.status === "Approved" && slot.imageUrl && (
        <img
          src={apiUrl(slot.imageUrl)}
          alt={TITLE[type]}
          style={{ width: "100%", borderRadius: 8, border: "1px solid var(--border)", marginBottom: 12 }}
        />
      )}

      {/* Human-in-the-loop prompt: Character always, Environment on request. */}
      {isCharacter && slot.status !== "Approved" && (
        <div style={{ marginBottom: 12 }}>
          <label className="wz-field" style={{ marginBottom: 6 }}>
            <span>Prompt nhân vật (xem &amp; sửa trước khi tạo)</span>
            <textarea
              rows={7}
              value={prompt}
              disabled={disabled}
              onChange={(e) => setPrompt(e.target.value)}
              placeholder={working === "prompt" ? "Đang tải prompt mẫu..." : ""}
            />
          </label>
          <button type="button" className="wz-btn wz-btn-sm" disabled={disabled} onClick={reloadDefaultPrompt}>
            {working === "prompt" ? "..." : "Khôi phục prompt mặc định"}
          </button>
        </div>
      )}

      {/* Result: exactly one image when a single variant exists. */}
      {singleResult && slot.status !== "Approved" && (
        <div style={{ marginBottom: 12 }}>
          <p className="wz-hint" style={{ marginBottom: 6 }}>Ảnh vừa tạo:</p>
          <img
            src={apiUrl(slot.variants[0].imageUrl)}
            alt={TITLE[type]}
            style={{ width: "100%", borderRadius: 8, border: "1px solid var(--border)", marginBottom: 8 }}
          />
          <button
            type="button"
            className="wz-btn wz-btn-sm wz-btn-primary"
            disabled={disabled}
            onClick={() => run("approve", () => assetReferencesApi.approve(contentProjectId, slot.variants[0].id))}
          >
            {working === "approve" ? "Đang chốt..." : "Chốt ảnh này"}
          </button>
        </div>
      )}

      {slot.variants.length > 1 && slot.status !== "Approved" && (
        <>
          <p className="wz-hint" style={{ marginBottom: 6 }}>Chọn 1 ảnh rồi bấm "Chốt ảnh này":</p>
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fill, minmax(120px, 1fr))",
              gap: 8,
              marginBottom: 12,
            }}
          >
            {slot.variants.map((variant) => (
              <button
                key={variant.id}
                type="button"
                onClick={() => setPicked(variant.id)}
                disabled={disabled}
                style={{
                  padding: 0,
                  border: `2px solid ${picked === variant.id ? "var(--accent)" : "var(--border)"}`,
                  borderRadius: 8,
                  overflow: "hidden",
                  cursor: "pointer",
                  background: "none",
                }}
              >
                <img src={apiUrl(variant.imageUrl)} alt="Biến thể" style={{ width: "100%", display: "block" }} />
              </button>
            ))}
          </div>
          {picked && (
            <button
              type="button"
              className="wz-btn wz-btn-sm wz-btn-primary"
              disabled={disabled}
              onClick={() => run("approve", () => assetReferencesApi.approve(contentProjectId, picked))}
              style={{ marginBottom: 12 }}
            >
              {working === "approve" ? "Đang chốt..." : "Chốt ảnh này"}
            </button>
          )}
        </>
      )}

      {error && <p className="wz-error">{error}</p>}

      <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
        <button
          type="button"
          className="wz-btn wz-btn-sm wz-btn-primary"
          disabled={disabled || (isCharacter && !prompt.trim())}
          onClick={() => generate(isCharacter ? prompt : undefined)}
        >
          {working === "generate"
            ? "Đang tạo..."
            : isCharacter
              ? `Xác nhận Prompt & Tạo 1 ảnh (${formatUsd(imageUsd)})`
              : slot.variants.length > 0 || resolved
                ? "Tạo lại bằng AI"
                : "Tạo ảnh mẫu với AI"}
        </button>

        {!isCharacter && (
          <button type="button" className="wz-btn wz-btn-sm" disabled={disabled} onClick={() => setShowPrompt((v) => !v)}>
            Tái tạo (prompt riêng)
          </button>
        )}

        <label className="wz-btn wz-btn-sm" style={{ cursor: disabled ? "not-allowed" : "pointer", opacity: disabled ? 0.5 : 1 }}>
          {working === "upload" ? "Đang tải..." : "Tải ảnh lên"}
          <input
            type="file"
            accept="image/*"
            hidden
            disabled={disabled}
            onChange={(e) => {
              const f = e.target.files?.[0];
              if (f) run("upload", () => assetReferencesApi.upload(contentProjectId, type, f));
              e.target.value = "";
            }}
          />
        </label>

        <button
          type="button"
          className="wz-btn wz-btn-sm"
          disabled={disabled}
          onClick={() => run("skip", () => assetReferencesApi.skip(contentProjectId, type))}
        >
          {working === "skip" ? "..." : "Không cần loại này"}
        </button>
      </div>

      {!isCharacter && showPrompt && (
        <div style={{ marginTop: 10 }}>
          <textarea
            rows={3}
            value={prompt}
            placeholder="Ví dụ: một căn hộ nhỏ ở Sài Gòn, cửa sổ nhìn ra ban công..."
            onChange={(e) => setPrompt(e.target.value)}
            style={{ width: "100%", boxSizing: "border-box", padding: 9, borderRadius: 8, border: "1px solid var(--border)" }}
          />
          <button
            type="button"
            className="wz-btn wz-btn-sm wz-btn-primary"
            disabled={disabled || !prompt.trim()}
            style={{ marginTop: 6 }}
            onClick={() => generate(prompt)}
          >
            {working === "generate" ? "Đang tạo..." : "Tạo với prompt này"}
          </button>
        </div>
      )}
    </section>
  );
}
