import type { StepProps } from "./steps";
import { ReferenceCard } from "./ReferenceCard";
import { ProgressPanel } from "./components";

/**
 * Wizard step 4: lock in the Character and Environment reference images before
 * any clip is generated. Both slots must be Approved or Skipped for the
 * "Bắt đầu dựng video" button on the next step to unlock.
 */
export function ReferencesStep({ overview, catalog, busy, onRefresh, onJobStarted, onGoTo }: StepProps) {
  const { character, environment } = overview.references;
  const resolved = (s: typeof character) => s.status === "Approved" || s.status === "Skipped";
  const bothResolved = resolved(character) && resolved(environment);

  return (
    <>
      <section className="wz-card">
        <h2>Ảnh mẫu (Nhân vật &amp; Bối cảnh)</h2>
        <p className="wz-sub">
          Chốt ảnh mẫu <strong>trước khi tốn credit</strong>: mỗi clip sẽ bám theo ảnh này để nhân vật và bối cảnh
          đồng nhất. Không cần loại nào thì bấm "Không cần loại này".
        </p>
      </section>

      <ProgressPanel progress={overview.progress} />

      <div style={{ display: "flex", gap: 16, flexWrap: "wrap" }}>
        <ReferenceCard
          contentProjectId={overview.id}
          slot={character}
          pricing={catalog.pricing}
          busy={busy}
          onChanged={onRefresh}
          onJobStarted={onJobStarted}
        />
        <ReferenceCard
          contentProjectId={overview.id}
          slot={environment}
          pricing={catalog.pricing}
          busy={busy}
          onChanged={onRefresh}
          onJobStarted={onJobStarted}
        />
      </div>

      <section className="wz-card">
        <div className="wz-actions">
          <button
            type="button"
            className="wz-btn wz-btn-primary"
            disabled={busy || !bothResolved}
            title={bothResolved ? undefined : "Hãy chốt hoặc bỏ qua cả hai loại"}
            onClick={() => onGoTo("Generate")}
          >
            Tiếp tục
          </button>
        </div>
        {!bothResolved && (
          <p className="wz-hint" style={{ marginTop: 8 }}>
            Còn thiếu: {!resolved(character) && "Nhân vật"} {!resolved(character) && !resolved(environment) && "·"}{" "}
            {!resolved(environment) && "Bối cảnh"}
          </p>
        )}
      </section>
    </>
  );
}
