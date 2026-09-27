import { useRef, useState, type ReactNode } from "react";
import {
  apiErrorCode,
  characterPendingReferenceImageFileUrl,
  characterReferenceImageFileUrl,
  describeApiError,
  storiesApi,
  type StoryCharacterKind,
  type StoryCharacterResponse,
  type StoryReferenceImageResponse,
  type StoryReferenceImageSource,
  type StoryReferencePromptResponse,
  type StoryReferencePromptTarget,
} from "../api/client";
import { CostNote, ErrorMessage, Field, StatusBadge } from "./components";
import {
  CANONICAL_TEXT_MAX,
  CHARACTER_KIND_LABEL,
  CHARACTER_KIND_ORDER,
  SOURCE_LABEL,
  SPECIES_MAX,
  characterPatchFromReferenceResult,
  characterPromptSignature,
  characterReferenceStatePatch,
  missingFieldNames,
  type CanonicalFieldValues,
} from "./storyCharacterHelpers";

// ---------------------------------------------------------------------------
// Story character: canonical identity fields + reference image workflow.
//
// The reference image is a neutral full-body A-pose identity image on a plain
// white background. A regenerated / uploaded image never silently replaces an
// APPROVED one: it lands as a CANDIDATE ("Ảnh đề xuất") next to the current
// "Ảnh chuẩn đang dùng" and only replaces it after the user explicitly confirms.
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Canonical fields (create + edit forms)
// ---------------------------------------------------------------------------

export function CharacterCanonicalFields({
  values,
  onChange,
  disabled,
}: {
  values: CanonicalFieldValues;
  onChange: (next: CanonicalFieldValues) => void;
  disabled?: boolean;
}) {
  const set = <K extends keyof CanonicalFieldValues>(key: K, value: CanonicalFieldValues[K]) =>
    onChange({ ...values, [key]: value });

  return (
    <>
      <Field
        label="Loại nhân vật"
        hint='Quyết định tư thế ảnh chuẩn (đứng trung tính, toàn thân). Khác với ô "hành vi giống người" bên dưới - ô đó chỉ áp dụng cho hành động trong cảnh.'
      >
        <select
          value={values.kind}
          disabled={disabled}
          onChange={(e) => set("kind", e.target.value as StoryCharacterKind)}
        >
          {CHARACTER_KIND_ORDER.map((k) => (
            <option key={k} value={k}>
              {CHARACTER_KIND_LABEL[k]}
            </option>
          ))}
        </select>
      </Field>
      <Field label="Loài / giống" hint='Ví dụ: "mèo Scottish Fold", "chó Corgi".'>
        <input
          type="text"
          maxLength={SPECIES_MAX}
          value={values.species}
          disabled={disabled}
          onChange={(e) => set("species", e.target.value)}
        />
      </Field>
      <Field
        label="Trang phục & phụ kiện"
        hint="Đây là thông tin chuẩn của nhân vật và được chép NGUYÊN VĂN vào prompt ảnh chuẩn - ghi rõ màu sắc, kiểu dáng."
      >
        <textarea
          rows={2}
          maxLength={CANONICAL_TEXT_MAX}
          value={values.clothingAndAccessories}
          disabled={disabled}
          onChange={(e) => set("clothingAndAccessories", e.target.value)}
        />
      </Field>
      <Field label="Đặc điểm nhận dạng" hint="Vết sẹo, khoang lông, nốt ruồi, màu mắt, dấu hiệu riêng...">
        <textarea
          rows={2}
          maxLength={CANONICAL_TEXT_MAX}
          value={values.distinctiveFeatures}
          disabled={disabled}
          onChange={(e) => set("distinctiveFeatures", e.target.value)}
        />
      </Field>
      <p className="wz-hint" style={{ marginTop: -6, marginBottom: 12 }}>
        Thiếu thông tin thì AI sẽ tự đoán, ảnh mỗi lần tạo có thể khác nhau.
      </p>
    </>
  );
}

// ---------------------------------------------------------------------------
// Consistency explainer (one per Characters section)
// ---------------------------------------------------------------------------

export function ReferenceConsistencyInfo() {
  return (
    <details className="wz-advanced">
      <summary>Mức độ nhất quán - ảnh chuẩn giúp được đến đâu?</summary>
      <p className="wz-hint" style={{ marginTop: 0 }}>
        &quot;Nhất quán&quot; gồm nhiều lớp riêng biệt - có lớp app đảm bảo được, có lớp thì không:
      </p>
      <ol className="wz-hint" style={{ paddingLeft: 18, margin: "6px 0" }}>
        <li>
          <strong style={{ display: "inline" }}>Mô tả nhân vật đã lưu</strong> - loài, ngoại hình, trang phục, đặc điểm
          nhận dạng được lưu trong Story. Đây là nguồn thông tin gốc của app, nhưng mới chỉ là chữ, chưa phải hình.
        </li>
        <li>
          <strong style={{ display: "inline" }}>Ảnh chuẩn đã chọn</strong> - ảnh bạn đã duyệt (&quot;Ảnh chuẩn đang
          dùng&quot;). Ảnh đề xuất chưa duyệt không được coi là ảnh chuẩn.
        </li>
        <li>
          <strong style={{ display: "inline" }}>Ảnh được gửi hoặc đính kèm vào công cụ</strong> - có ảnh chuẩn trong app
          chưa có nghĩa công cụ tạo ảnh/video đã nhận được nó. Prompt sao chép chỉ là chữ, không kèm ảnh.
        </li>
        <li>
          <strong style={{ display: "inline" }}>Ảnh công cụ thực sự dùng</strong> - dù được đính kèm, mỗi công cụ tự
          quyết định bám theo ảnh đến mức nào; có thể bỏ qua hoặc diễn giải lại chi tiết.
        </li>
        <li>
          <strong style={{ display: "inline" }}>Độ nhất quán của kết quả</strong> - ảnh chuẩn giúp cải thiện độ nhất
          quán nhưng không đảm bảo tuyệt đối. Nên xem lại từng ảnh/clip sau khi tạo.
        </li>
      </ol>
      <p className="wz-hint" style={{ marginBottom: 0 }}>
        <strong style={{ display: "inline" }}>Google Flow:</strong> app không tự đính kèm ảnh chuẩn vào Flow. Bạn cần tự
        tải ảnh chuẩn lên / đính kèm trong Flow khi tạo (bấm &quot;Mở ảnh gốc&quot; dưới ảnh để lưu về).
      </p>
    </details>
  );
}

// ---------------------------------------------------------------------------
// Reference image panel (one per character)
// ---------------------------------------------------------------------------

// PNG and JPEG only: downstream providers mislabel WebP.
const UPLOAD_TYPES = ["image/png", "image/jpeg"];
const UPLOAD_EXTENSIONS = /\.(png|jpe?g)$/i;
const UPLOAD_MAX_BYTES = 10 * 1024 * 1024;

function validateUpload(file: File): string | null {
  const typeOk = file.type ? UPLOAD_TYPES.includes(file.type) : UPLOAD_EXTENSIONS.test(file.name);
  if (!typeOk) return "Chỉ hỗ trợ ảnh PNG hoặc JPG.";
  if (file.size > UPLOAD_MAX_BYTES) {
    return `Ảnh quá lớn (${(file.size / (1024 * 1024)).toFixed(1)} MB). Giới hạn 10 MB.`;
  }
  if (file.size === 0) return "File ảnh trống.";
  return null;
}

const PROMPT_TARGETS: { id: StoryReferencePromptTarget; label: string }[] = [
  { id: "flow", label: "Google Flow (dùng credit Flow của bạn)" },
  { id: "midjourney", label: "Midjourney" },
  { id: "dalle", label: "DALL·E" },
  { id: "flux", label: "FLUX" },
  { id: "generic", label: "Chung" },
];

type Working = "generate" | "upload" | "approve" | "discard" | "prompt";

function ReferenceImageFigure({
  src,
  alt,
  title,
  badges,
}: {
  src: string;
  alt: string;
  title: string;
  badges: ReactNode;
}) {
  return (
    <figure style={{ margin: 0 }}>
      <img
        src={src}
        alt={alt}
        style={{
          maxWidth: 130,
          maxHeight: 210,
          width: "auto",
          height: "auto",
          objectFit: "contain",
          borderRadius: 8,
          border: "1px solid var(--border)",
          background: "var(--code-bg, transparent)",
          display: "block",
        }}
      />
      <figcaption style={{ marginTop: 4, maxWidth: 150 }}>
        <strong style={{ fontSize: 12.5 }}>{title}</strong>
        <span style={{ display: "flex", gap: 4, flexWrap: "wrap" }}>{badges}</span>
        <a className="wz-hint" href={src} target="_blank" rel="noreferrer">
          Mở ảnh gốc
        </a>
      </figcaption>
    </figure>
  );
}

/**
 * Reference image panel for one Story character. Every request goes through `withBusy` (a ref guard, so a fast
 * double click can never fire a second paid generation) and, once it returns, only THIS character's fields are
 * merged into the page state via `onChange`.
 */
export function StoryCharacterReferencePanel({
  storyId,
  character,
  onChange,
}: {
  storyId: string;
  character: StoryCharacterResponse;
  onChange: (patch: Partial<StoryCharacterResponse>) => void;
}) {
  const [working, setWorking] = useState<Working | null>(null);
  const [error, setError] = useState<string | null>(null);
  // Bumped after every image change so the browser does not keep showing a cached previous image at the same URL.
  const [imageKey, setImageKey] = useState(() => Date.now());
  const [promptTarget, setPromptTarget] = useState<StoryReferencePromptTarget>("flow");
  // The fetched prompt is remembered together with the character data it was built from (`characterPromptSignature`);
  // once the canonical fields are edited the signature no longer matches and the stale prompt is dropped, so it can
  // never be shown or copied again.
  const [promptEntry, setPromptEntry] = useState<{ signature: string; result: StoryReferencePromptResponse } | null>(
    null,
  );
  const [copied, setCopied] = useState<string | null>(null);
  const busyRef = useRef(false);

  const promptSignature = characterPromptSignature(character);
  const promptResult = promptEntry && promptEntry.signature === promptSignature ? promptEntry.result : null;

  const hasCurrent = character.hasReferenceImage;
  const hasPending = character.hasPendingReferenceImage === true;
  const currentApproved = character.referenceImageStatus === "Approved";
  const canGenerate = character.canGenerateReference !== false;
  const canApproveCurrent = character.referenceImageStatus === "Generated";
  const missing = missingFieldNames(character.missingReferenceFields);
  const promptNotes = promptResult?.notes ?? [];
  const disabled = working !== null;

  const blockedReason = !canGenerate
    ? missing.length > 0
      ? `Cần bổ sung ${missing.join(", ")} trước khi tạo ảnh bằng AI.`
      : "Chưa đủ thông tin nhân vật để tạo ảnh bằng AI."
    : null;

  const withBusy = async (kind: Working, fn: () => Promise<void>) => {
    if (busyRef.current) return;
    busyRef.current = true;
    setWorking(kind);
    setError(null);
    try {
      await fn();
    } finally {
      busyRef.current = false;
      setWorking(null);
    }
  };

  const runImageOperation = (
    kind: Exclude<Working, "prompt">,
    call: () => Promise<StoryReferenceImageResponse | undefined>,
    fallbackError: string,
  ) =>
    withBusy(kind, async () => {
      try {
        const result = await call();
        onChange(
          result
            ? characterPatchFromReferenceResult(result)
            : // Empty body (e.g. discard answered with 204): the only thing that changed is the candidate slot.
              { hasPendingReferenceImage: false, pendingReferenceImageSource: null, pendingVersion: null },
        );
        setImageKey(Date.now());
      } catch (err) {
        const code = apiErrorCode(err);
        if (code === "REFERENCE_PENDING_CHANGED" || code === "REFERENCE_REPLACE_CONFIRMATION_REQUIRED") {
          // Our view of this character is out of date (e.g. another tab replaced the candidate): reload ONLY this
          // character's image state so the user sees the current images before deciding again.
          const refreshed = await refreshCharacterImages();
          setError(
            code === "REFERENCE_PENDING_CHANGED"
              ? refreshed
                ? "Ảnh đề xuất đã thay đổi ở nơi khác - đã tải lại, hãy xem lại ảnh rồi duyệt."
                : "Ảnh đề xuất đã thay đổi ở nơi khác - hãy tải lại trang, xem lại ảnh rồi duyệt."
              : "Cần xác nhận trước khi thay thế ảnh chuẩn hiện tại - hãy bấm duyệt lại và xác nhận.",
          );
        } else {
          setError(describeApiError(err, fallbackError));
        }
      }
    });

  /** Re-reads this character from the server and merges only its reference-image fields. */
  const refreshCharacterImages = async (): Promise<boolean> => {
    try {
      const story = await storiesApi.getById(storyId);
      const fresh = story.characters.find((c) => c.id === character.id);
      if (!fresh) return false;
      onChange(characterReferenceStatePatch(fresh));
      setImageKey(Date.now());
      return true;
    } catch {
      return false;
    }
  };

  /** New images never replace an approved one, but they DO overwrite an existing candidate / unapproved image. */
  const confirmOverwrite = (): boolean => {
    if (hasPending) return window.confirm("Ảnh đề xuất hiện tại sẽ bị thay bằng ảnh mới. Tiếp tục?");
    if (hasCurrent && !currentApproved) {
      return window.confirm("Ảnh hiện tại (chưa duyệt) sẽ bị thay bằng ảnh mới. Tiếp tục?");
    }
    return true;
  };

  const generate = () => {
    if (!canGenerate || disabled || !confirmOverwrite()) return;
    void runImageOperation(
      "generate",
      () => storiesApi.generateCharacterReferenceImage(storyId, character.id),
      "Không tạo được ảnh chuẩn.",
    );
  };

  const upload = (file: File) => {
    const problem = validateUpload(file);
    if (problem) {
      setError(problem);
      return;
    }
    if (disabled || !confirmOverwrite()) return;
    void runImageOperation(
      "upload",
      () => storiesApi.uploadCharacterReferenceImage(storyId, character.id, file),
      "Không tải được ảnh lên.",
    );
  };

  const replacingApproved = hasPending && hasCurrent;

  const approve = () => {
    if (disabled) return;
    // The server rejects promoting a candidate over an existing image unless told to - only say so after the user agreed.
    if (
      replacingApproved &&
      !window.confirm("Thay thế ảnh chuẩn hiện tại? Ảnh đã dùng ở các tập cũ không tự đổi.")
    ) {
      return;
    }
    void runImageOperation(
      "approve",
      () =>
        storiesApi.approveCharacterReferenceImage(storyId, character.id, {
          confirmReplace: replacingApproved,
          // The candidate the user is looking at: the server refuses if it was replaced elsewhere in the meantime.
          expectedPendingVersion: hasPending ? character.pendingVersion : null,
        }),
      "Không duyệt được ảnh chuẩn.",
    );
  };

  const discard = () => {
    if (disabled) return;
    if (!window.confirm("Bỏ ảnh đề xuất này? Ảnh chuẩn đang dùng được giữ nguyên.")) return;
    void runImageOperation(
      "discard",
      () => storiesApi.discardCharacterPendingReferenceImage(storyId, character.id),
      "Không bỏ được ảnh đề xuất.",
    );
  };

  const flashCopied = (key: string) => {
    setCopied(key);
    window.setTimeout(() => setCopied((cur) => (cur === key ? null : cur)), 2000);
  };

  const copyText = async (key: string, text: string) => {
    try {
      await navigator.clipboard.writeText(text);
      flashCopied(key);
    } catch {
      setError("Trình duyệt chặn copy - hãy chọn và copy thủ công từ ô prompt bên dưới.");
    }
  };

  const copyPrompt = () => {
    if (disabled) return;
    void withBusy("prompt", async () => {
      try {
        const res = await storiesApi.getCharacterReferencePrompt(storyId, character.id, promptTarget);
        setPromptEntry({ signature: promptSignature, result: res });
        await copyText("prompt", res.prompt);
      } catch (err) {
        setError(describeApiError(err, "Không lấy được prompt."));
      }
    });
  };

  const sourceBadge = (source: StoryReferenceImageSource | null | undefined) =>
    source ? <StatusBadge tone="neutral">{SOURCE_LABEL[source]}</StatusBadge> : null;

  return (
    <div style={{ marginTop: 10 }}>
      {hasCurrent || hasPending ? (
        <div style={{ display: "flex", gap: 12, flexWrap: "wrap", alignItems: "flex-start" }}>
          {hasCurrent && (
            <ReferenceImageFigure
              src={characterReferenceImageFileUrl(storyId, character.id, imageKey)}
              alt={currentApproved ? "Ảnh chuẩn đang dùng" : "Ảnh hiện tại (chưa duyệt)"}
              title={currentApproved ? "Ảnh chuẩn đang dùng" : "Ảnh hiện tại (chưa duyệt)"}
              badges={
                <>
                  <StatusBadge tone={currentApproved ? "success" : "warning"}>
                    {currentApproved ? "Đã duyệt" : "Chưa duyệt"}
                  </StatusBadge>
                  {sourceBadge(character.referenceImageSource)}
                </>
              }
            />
          )}
          {hasPending && (
            <ReferenceImageFigure
              src={characterPendingReferenceImageFileUrl(storyId, character.id, imageKey)}
              alt="Ảnh đề xuất"
              title="Ảnh đề xuất"
              badges={
                <>
                  <StatusBadge tone="warning">Chưa duyệt</StatusBadge>
                  {sourceBadge(character.pendingReferenceImageSource)}
                </>
              }
            />
          )}
        </div>
      ) : (
        <p className="wz-hint" style={{ marginTop: 0 }}>
          Chưa có ảnh chuẩn. Tạo bằng AI, tải ảnh có sẵn lên, hoặc sao chép prompt để tạo ở công cụ khác.
        </p>
      )}
      {hasPending && (
        <p className="wz-hint">
          Ảnh đề xuất chưa được dùng ở đâu cả. Ảnh chuẩn đang dùng chỉ đổi khi bạn duyệt ảnh đề xuất và xác nhận thay
          thế.
        </p>
      )}
      {hasCurrent && currentApproved && !hasPending && (
        <p className="wz-hint">
          Ảnh chuẩn nhận diện toàn thân, nền trắng trơn - không phải ảnh cảnh. Tạo lại hoặc tải ảnh mới sẽ chỉ tạo ảnh
          đề xuất, không tự thay ảnh này.
        </p>
      )}

      {missing.length > 0 && (
        <p
          className="wz-hint"
          style={{ border: "1px solid #b8860b", borderRadius: 8, padding: "8px 10px", opacity: 1 }}
        >
          ⚠️ Chưa có: {missing.join(", ")}. Thiếu thông tin thì ảnh dễ khác nhau giữa các lần tạo - bấm &quot;Chỉnh
          sửa&quot; để bổ sung.
        </p>
      )}

      {/* Paid: in-app AI generation. */}
      <div className="wz-actions" style={{ marginTop: 8 }}>
        <button
          type="button"
          className="wz-btn wz-btn-sm wz-btn-primary"
          disabled={disabled || !canGenerate}
          title={blockedReason ?? undefined}
          onClick={generate}
        >
          {working === "generate" ? "Đang tạo..." : hasCurrent || hasPending ? "Tạo lại" : "Tạo ảnh (AI)"}
        </button>
        <CostNote kind="image" />
      </div>
      {blockedReason && <p className="wz-hint">{blockedReason}</p>}

      {/* Free actions. */}
      <div className="wz-actions" style={{ marginTop: 8 }}>
        <label
          className="wz-btn wz-btn-sm"
          style={{ cursor: disabled ? "not-allowed" : "pointer", opacity: disabled ? 0.5 : 1 }}
        >
          {working === "upload" ? "Đang tải lên..." : "Tải ảnh lên"}
          <input
            type="file"
            accept="image/png,image/jpeg"
            hidden
            disabled={disabled}
            onChange={(e) => {
              const file = e.target.files?.[0];
              e.target.value = "";
              if (file) upload(file);
            }}
          />
        </label>
        {(hasPending || (hasCurrent && canApproveCurrent)) && (
          <button type="button" className="wz-btn wz-btn-sm wz-btn-primary" disabled={disabled} onClick={approve}>
            {working === "approve" ? "Đang duyệt..." : hasPending ? "Duyệt ảnh đề xuất" : "Duyệt ảnh này"}
          </button>
        )}
        {hasPending && (
          <button type="button" className="wz-btn wz-btn-sm wz-btn-danger" disabled={disabled} onClick={discard}>
            {working === "discard" ? "Đang bỏ..." : "Bỏ ảnh đề xuất"}
          </button>
        )}
        <span className="wz-hint" style={{ marginTop: 0 }}>
          Tải lên / duyệt / bỏ: không gọi AI trong app (chỉ PNG hoặc JPG, tối đa 10 MB)
        </span>
      </div>

      {/* Free: prompt for an external tool. */}
      <div className="wz-actions" style={{ marginTop: 8 }}>
        <select
          aria-label="Công cụ tạo ảnh"
          value={promptTarget}
          disabled={disabled}
          onChange={(e) => {
            setPromptTarget(e.target.value as StoryReferencePromptTarget);
            setPromptEntry(null);
          }}
          style={{
            border: "1px solid var(--border)",
            borderRadius: 8,
            padding: "5px 8px",
            font: "inherit",
            fontSize: 13,
            background: "var(--bg)",
            color: "var(--text-h)",
          }}
        >
          {PROMPT_TARGETS.map((t) => (
            <option key={t.id} value={t.id}>
              {t.label}
            </option>
          ))}
        </select>
        <button type="button" className="wz-btn wz-btn-sm" disabled={disabled} onClick={copyPrompt}>
          {working === "prompt" ? "Đang lấy prompt..." : copied === "prompt" ? "Đã sao chép ✓" : "Sao chép prompt"}
        </button>
        <span className="wz-hint" style={{ marginTop: 0 }}>
          Không gọi AI trong app (công cụ bên ngoài có thể tính phí/credit riêng)
        </span>
      </div>

      <ErrorMessage message={error} />

      {promptResult && (
        <details className="wz-advanced" open style={{ marginTop: 8, marginBottom: 0 }}>
          <summary>Hướng dẫn dùng prompt này</summary>
          {missingFieldNames(promptResult.missingFields).length > 0 && (
            <p className="wz-hint" style={{ marginTop: 0 }}>
              ⚠️ Prompt còn thiếu: {missingFieldNames(promptResult.missingFields).join(", ")}. Bổ sung ở phần
              &quot;Chỉnh sửa&quot; rồi sao chép lại để kết quả ổn định hơn.
            </p>
          )}
          {promptNotes.length > 0 && (
            <ul className="wz-hint" style={{ paddingLeft: 18, margin: "0 0 8px" }}>
              {promptNotes.map((note) => (
                <li key={note}>{note}</li>
              ))}
            </ul>
          )}
          <textarea
            readOnly
            rows={5}
            value={promptResult.prompt}
            onFocus={(e) => e.currentTarget.select()}
            style={{
              width: "100%",
              boxSizing: "border-box",
              border: "1px solid var(--border)",
              borderRadius: 8,
              padding: 8,
              font: "inherit",
              fontSize: 12.5,
              background: "var(--bg)",
              color: "var(--text-h)",
            }}
          />
          {promptResult.negativePrompt && (
            <div style={{ marginTop: 8 }}>
              <div className="wz-actions" style={{ marginTop: 0 }}>
                <strong style={{ fontSize: 12.5 }}>Prompt loại trừ (negative)</strong>
                <button
                  type="button"
                  className="wz-btn wz-btn-sm"
                  onClick={() => void copyText("negative", promptResult.negativePrompt ?? "")}
                >
                  {copied === "negative" ? "Đã sao chép ✓" : "Sao chép"}
                </button>
              </div>
              <p className="wz-hint" style={{ userSelect: "all" }}>
                {promptResult.negativePrompt}
              </p>
            </div>
          )}
        </details>
      )}
    </div>
  );
}
