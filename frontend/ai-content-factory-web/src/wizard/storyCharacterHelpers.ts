import type {
  StoryCharacterKind,
  StoryCharacterResponse,
  StoryReferenceImageResponse,
  StoryReferenceImageSource,
} from "../api/client";

// Non-component helpers for StoryCharacterReference.tsx (kept out of the .tsx so it only exports components).

export const CHARACTER_KIND_LABEL: Record<StoryCharacterKind, string> = {
  Unspecified: "Chưa chọn",
  Animal: "Động vật thường",
  AnthropomorphicAnimal: "Động vật nhân hoá",
  Human: "Người",
  Other: "Khác",
};

export const CHARACTER_KIND_ORDER: StoryCharacterKind[] = [
  "Unspecified",
  "Animal",
  "AnthropomorphicAnimal",
  "Human",
  "Other",
];

const CANONICAL_FIELD_LABEL: Record<string, string> = {
  appearance: "Ngoại hình",
  species: "Loài / giống",
  clothingAndAccessories: "Trang phục & phụ kiện",
  distinctiveFeatures: "Đặc điểm nhận dạng",
};

/** Friendly names for backend field ids; unknown ids are shown as-is rather than dropped. */
export const missingFieldNames = (ids: string[] | null | undefined): string[] =>
  (ids ?? []).map((id) => CANONICAL_FIELD_LABEL[id] ?? id);

// Same limits as the backend domain (StoryCharacter.*MaxLength) so a too-long value is caught before the request.
export const SPECIES_MAX = 100;
export const CANONICAL_TEXT_MAX = 1000;

export const SOURCE_LABEL: Record<StoryReferenceImageSource, string> = {
  Generated: "AI tạo",
  Uploaded: "Tải lên",
};

const asSource = (value: string | null | undefined): StoryReferenceImageSource | null =>
  value === "Generated" || value === "Uploaded" ? value : null;

export interface CanonicalFieldValues {
  kind: StoryCharacterKind;
  species: string;
  clothingAndAccessories: string;
  distinctiveFeatures: string;
}

export const EMPTY_CANONICAL_FIELDS: CanonicalFieldValues = {
  kind: "Unspecified",
  species: "",
  clothingAndAccessories: "",
  distinctiveFeatures: "",
};

export const canonicalFieldsOf = (c: StoryCharacterResponse): CanonicalFieldValues => ({
  kind: c.kind ?? "Unspecified",
  species: c.species ?? "",
  clothingAndAccessories: c.clothingAndAccessories ?? "",
  distinctiveFeatures: c.distinctiveFeatures ?? "",
});

/** Reference-image fields of a character (everything an image operation or a refresh can change). */
export function characterReferenceStatePatch(c: StoryCharacterResponse): Partial<StoryCharacterResponse> {
  return {
    referenceImageStatus: c.referenceImageStatus,
    hasReferenceImage: c.hasReferenceImage,
    referenceImageSource: c.referenceImageSource,
    hasPendingReferenceImage: c.hasPendingReferenceImage === true,
    pendingReferenceImageSource: c.pendingReferenceImageSource ?? null,
    pendingVersion: c.pendingVersion ?? null,
  };
}

/** Everything that feeds the copied reference prompt; a cached prompt is only valid for the signature it was fetched under. */
export const characterPromptSignature = (c: StoryCharacterResponse): string =>
  JSON.stringify([
    c.name,
    c.description,
    c.visualDescription,
    c.behaviorProfile,
    c.kind,
    c.species,
    c.clothingAndAccessories,
    c.distinctiveFeatures,
  ]);

/**
 * Translates an image-endpoint response into the character fields it changes. A CANDIDATE never changes the
 * current image, so while `hasPending` is true only the pending fields are touched; otherwise the response
 * describes the (new) current image. Only the fields returned here are merged into the character.
 */
export function characterPatchFromReferenceResult(result: StoryReferenceImageResponse): Partial<StoryCharacterResponse> {
  const pending = result.hasPending === true;
  const patch: Partial<StoryCharacterResponse> = {
    hasPendingReferenceImage: pending,
    pendingReferenceImageSource: pending ? asSource(result.pendingSource) : null,
    pendingVersion: pending ? (result.pendingVersion ?? null) : null,
  };
  if (!pending) {
    patch.referenceImageStatus = result.status;
    patch.hasReferenceImage = !!result.imagePath;
    const source = asSource(result.source);
    if (source) patch.referenceImageSource = source;
  }
  return patch;
}
