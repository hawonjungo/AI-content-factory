import { useEffect, useState } from "react";
import { describeApiError, presetsApi, type NamedPreset } from "../api/client";
import { ErrorMessage, Loading, PresetPicker } from "./components";

/**
 * Visual-style picker for a Story (series). Same catalog (`presetsApi.getCatalog().styles`) and same
 * `PresetPicker` cards as the video wizard's "Phong cách hình ảnh" - nothing is duplicated on the frontend.
 *
 * Fully controlled: `selectedId === null` means "no style chosen" (generation then falls back to the system's
 * default style, which can differ per generation path). The parent decides when/how to persist (create form vs. immediate save on detail).
 */
export function StoryStylePicker({
  selectedId,
  onSelect,
  disabled,
}: {
  selectedId: string | null;
  onSelect: (id: string | null) => void;
  disabled?: boolean;
}) {
  const [styles, setStyles] = useState<NamedPreset[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let cancelled = false;
    presetsApi
      .getCatalog()
      .then((catalog) => {
        if (!cancelled) setStyles(catalog.styles);
      })
      .catch((err) => {
        if (!cancelled) setError(describeApiError(err, "Không tải được danh sách phong cách hình ảnh."));
      });
    return () => {
      cancelled = true;
    };
  }, [attempt]);

  const load = () => {
    setError(null);
    setStyles(null);
    setAttempt((n) => n + 1);
  };

  if (error) {
    return (
      <div style={{ marginBottom: 16 }}>
        <ErrorMessage message={error} />
        <div className="wz-actions" style={{ marginTop: 0 }}>
          <button type="button" className="wz-btn wz-btn-sm" onClick={load}>
            Thử lại
          </button>
        </div>
      </div>
    );
  }

  if (!styles) return <Loading label="Đang tải danh sách phong cách..." />;

  if (styles.length === 0) {
    return <p className="wz-hint">Chưa có phong cách hình ảnh nào để chọn.</p>;
  }

  const selectedKnown = selectedId !== null && styles.some((s) => s.id === selectedId);

  return (
    <div>
      <PresetPicker
        label="Phong cách hình ảnh"
        presets={styles}
        selectedId={selectedId}
        disabled={disabled}
        onSelect={(id) => onSelect(id)}
      />
      {selectedId !== null && !selectedKnown && (
        <p className="wz-hint" style={{ marginTop: -12 }}>
          Phong cách đang lưu ({selectedId}) không còn trong danh sách - hãy chọn một phong cách khác.
        </p>
      )}
      {selectedId === null ? (
        <p className="wz-hint" style={{ marginTop: -12 }}>
          Chưa chọn phong cách: ảnh nhân vật và video các tập sẽ dùng style mặc định của hệ thống. Nên chọn một phong cách trước khi tạo ảnh nhân vật.
        </p>
      ) : (
        <div className="wz-actions" style={{ marginTop: -12 }}>
          {selectedKnown && (
            <span className="wz-hint">
              Đang dùng: <strong>{styles.find((s) => s.id === selectedId)?.name}</strong>
            </span>
          )}
          <button type="button" className="wz-btn wz-btn-sm" disabled={disabled} onClick={() => onSelect(null)}>
            Bỏ chọn (dùng mặc định)
          </button>
        </div>
      )}
    </div>
  );
}
