import { useEffect, useState } from "react";
import { Link, useParams, useNavigate } from "react-router-dom";
import "../ui/wizard.css";
import {
  characterReferenceImageFileUrl,
  describeApiError,
  locationReferenceImageFileUrl,
  storiesApi,
  type StoryBibleResponse,
  type StoryCharacterResponse,
  type StoryEpisodeResponse,
  type StoryEpisodeStatus,
  type StoryReferenceImageResponse,
  type StoryResponse,
  type StoryStateResponse,
} from "../api/client";
import {
  CostNote,
  EmptyState,
  ErrorMessage,
  Field,
  Loading,
  StatusBadge,
  type StatusTone,
} from "../wizard/components";
import {
  CharacterCanonicalFields,
  ReferenceConsistencyInfo,
  StoryCharacterReferencePanel,
} from "../wizard/StoryCharacterReference";
import {
  CHARACTER_KIND_LABEL,
  EMPTY_CANONICAL_FIELDS,
  canonicalFieldsOf,
  type CanonicalFieldValues,
} from "../wizard/storyCharacterHelpers";
import { StoryStylePicker } from "../wizard/StoryStylePicker";
import { TopNav } from "../ui/TopNav";

const EPISODE_STATUS_LABEL: Record<StoryEpisodeStatus, string> = {
  Draft: "Nháp",
  Scripted: "Đã có kịch bản",
  Completed: "Hoàn tất",
};

const EPISODE_STATUS_TONE: Record<StoryEpisodeStatus, StatusTone> = {
  Draft: "neutral",
  Scripted: "working",
  Completed: "success",
};

function summarizeStory(story: StoryResponse): { label: string; tone: StatusTone } {
  if (story.episodes.length === 0) return { label: "Mới tạo", tone: "neutral" };
  if (story.episodes.every((e) => e.status === "Completed")) return { label: "Hoàn thành", tone: "success" };
  return { label: "Đang tiến hành", tone: "working" };
}

const splitList = (value: string): string[] =>
  value
    .split(",")
    .map((s) => s.trim())
    .filter(Boolean);

type ReferenceEntityKind = "character" | "location";

/**
 * Single-image reference (Character or Location) for a Story entity. Unlike
 * `ReferenceCard` (per-ContentProject, multi-variant), this feature is
 * explicitly single-image-per-entity - no variant picker, no upload/skip.
 */
function StoryReferenceImage({
  storyId,
  kind,
  entityId,
  status,
  hasImage,
  onUpdated,
}: {
  storyId: string;
  kind: ReferenceEntityKind;
  entityId: string;
  status: string;
  hasImage: boolean;
  onUpdated: (result: StoryReferenceImageResponse) => void;
}) {
  const [working, setWorking] = useState<null | "generate" | "approve">(null);
  const [error, setError] = useState<string | null>(null);

  const fileUrl =
    kind === "character"
      ? characterReferenceImageFileUrl(storyId, entityId)
      : locationReferenceImageFileUrl(storyId, entityId);

  const generate = async (isRegenerate: boolean) => {
    if (
      isRegenerate &&
      !window.confirm("Tạo lại sẽ thay thế ảnh hiện tại (và huỷ trạng thái đã duyệt nếu có). Tiếp tục?")
    ) {
      return;
    }
    setWorking("generate");
    setError(null);
    try {
      const result =
        kind === "character"
          ? await storiesApi.generateCharacterReferenceImage(storyId, entityId)
          : await storiesApi.generateLocationReferenceImage(storyId, entityId);
      onUpdated(result);
    } catch (err) {
      setError(describeApiError(err, "Không tạo được ảnh tham chiếu."));
    } finally {
      setWorking(null);
    }
  };

  const approve = async () => {
    setWorking("approve");
    setError(null);
    try {
      const result =
        kind === "character"
          ? await storiesApi.approveCharacterReferenceImage(storyId, entityId)
          : await storiesApi.approveLocationReferenceImage(storyId, entityId);
      onUpdated(result);
    } catch (err) {
      setError(describeApiError(err, "Không duyệt được ảnh tham chiếu."));
    } finally {
      setWorking(null);
    }
  };

  const disabled = working !== null;

  return (
    <div style={{ marginTop: 10 }}>
      {hasImage && kind === "character" && (
        // Identity reference: full body on a plain background - not a scene. Shown uncropped (contain) so the whole
        // figure is visible, with an explicit label so it is not mistaken for a scene/keyframe image.
        <figure style={{ margin: "0 0 6px" }}>
          <img
            src={fileUrl}
            alt="Ảnh nhận diện nhân vật (toàn thân, nền trơn)"
            style={{
              maxWidth: 140,
              maxHeight: 220,
              width: "auto",
              height: "auto",
              objectFit: "contain",
              borderRadius: 8,
              border: "1px solid var(--border)",
              background: "var(--code-bg, transparent)",
              display: "block",
            }}
          />
          <figcaption className="wz-hint" style={{ marginTop: 4 }}>
            Ảnh nhận diện nhân vật (không phải ảnh cảnh)
          </figcaption>
        </figure>
      )}
      {hasImage && kind === "location" && (
        <img
          src={fileUrl}
          alt="Ảnh tham chiếu"
          style={{
            width: 140,
            height: 140,
            objectFit: "cover",
            borderRadius: 8,
            border: "1px solid var(--border)",
            marginBottom: 6,
            display: "block",
          }}
        />
      )}

      {!hasImage && status === "Pending" && (
        <div className="wz-actions" style={{ marginTop: 0 }}>
          <button type="button" className="wz-btn wz-btn-sm" disabled={disabled} onClick={() => generate(false)}>
            {working === "generate" ? "Đang tạo..." : "Tạo ảnh tham chiếu"}
          </button>
          <CostNote kind="image" />
        </div>
      )}

      {hasImage && status === "Approved" && (
        <div className="wz-actions" style={{ marginTop: 0 }}>
          <StatusBadge tone="success">Đã duyệt ảnh</StatusBadge>
          <button type="button" className="wz-btn wz-btn-sm" disabled={disabled} onClick={() => generate(true)}>
            {working === "generate" ? "Đang tạo lại..." : "Tạo lại"}
          </button>
          <CostNote kind="image" />
        </div>
      )}

      {hasImage && status === "Generated" && (
        <div className="wz-actions" style={{ marginTop: 0 }}>
          <button
            type="button"
            className="wz-btn wz-btn-sm wz-btn-primary"
            disabled={disabled}
            onClick={approve}
          >
            {working === "approve" ? "Đang duyệt..." : "Duyệt"}
          </button>
          <button type="button" className="wz-btn wz-btn-sm" disabled={disabled} onClick={() => generate(true)}>
            {working === "generate" ? "Đang tạo lại..." : "Tạo lại"}
          </button>
          <CostNote kind="image" />
        </div>
      )}

      <ErrorMessage message={error} />
    </div>
  );
}

function StoryDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [story, setStory] = useState<StoryResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // --- Overview (inline edit via PUT /stories/{id}) ---
  const [editingOverview, setEditingOverview] = useState(false);
  const [title, setTitle] = useState("");
  const [premise, setPremise] = useState("");
  const [niche, setNiche] = useState("");
  const [savingOverview, setSavingOverview] = useState(false);
  const [overviewError, setOverviewError] = useState<string | null>(null);

  // --- Visual style (saved immediately on pick, like the wizard's preset pickers) ---
  const [savingStyle, setSavingStyle] = useState(false);
  const [styleError, setStyleError] = useState<string | null>(null);

  // --- Characters ---
  const [charName, setCharName] = useState("");
  const [charDesc, setCharDesc] = useState("");
  const [charVisual, setCharVisual] = useState("");
  const [charAnthropomorphicCat, setCharAnthropomorphicCat] = useState(false);
  const [charCanon, setCharCanon] = useState<CanonicalFieldValues>(EMPTY_CANONICAL_FIELDS);
  const [addingChar, setAddingChar] = useState(false);
  const [charError, setCharError] = useState<string | null>(null);

  // --- Edit an existing character (e.g. opt into AnthropomorphicCat after creation) ---
  const [editingCharacterId, setEditingCharacterId] = useState<string | null>(null);
  const [editCharName, setEditCharName] = useState("");
  const [editCharDesc, setEditCharDesc] = useState("");
  const [editCharVisual, setEditCharVisual] = useState("");
  const [editCharAnthropomorphicCat, setEditCharAnthropomorphicCat] = useState(false);
  const [editCharCanon, setEditCharCanon] = useState<CanonicalFieldValues>(EMPTY_CANONICAL_FIELDS);
  const [savingCharEdit, setSavingCharEdit] = useState(false);
  const [charEditError, setCharEditError] = useState<string | null>(null);

  // --- Locations ---
  const [locName, setLocName] = useState("");
  const [locDesc, setLocDesc] = useState("");
  const [locVisual, setLocVisual] = useState("");
  const [addingLoc, setAddingLoc] = useState(false);
  const [locError, setLocError] = useState<string | null>(null);

  // --- Story Bible (create-once / regenerate-only, no edit endpoint) ---
  const [bible, setBible] = useState<StoryBibleResponse | null>(null);
  const [bibleLoading, setBibleLoading] = useState(true);
  const [bibleError, setBibleError] = useState<string | null>(null);
  const [showBibleForm, setShowBibleForm] = useState(false);
  const [bibleTheme, setBibleTheme] = useState("");
  const [bibleGenre, setBibleGenre] = useState("");
  const [bibleTone, setBibleTone] = useState("");
  const [bibleAudience, setBibleAudience] = useState("");
  const [bibleMainCharacters, setBibleMainCharacters] = useState("");
  const [bibleLocations, setBibleLocations] = useState("");
  const [bibleConstraints, setBibleConstraints] = useState("");
  const [bibleEpisodeCount, setBibleEpisodeCount] = useState("");
  const [generatingBible, setGeneratingBible] = useState(false);
  const [bibleFormError, setBibleFormError] = useState<string | null>(null);

  // --- Story State (read-only, entirely server-computed) ---
  const [storyState, setStoryState] = useState<StoryStateResponse | null>(null);
  const [stateError, setStateError] = useState<string | null>(null);

  // --- Episodes (summary is already on `story`; detail is lazy per-row) ---
  const [expandedEpisodeId, setExpandedEpisodeId] = useState<string | null>(null);
  const [episodeDetails, setEpisodeDetails] = useState<Record<string, StoryEpisodeResponse>>({});
  const [episodeDetailLoadingId, setEpisodeDetailLoadingId] = useState<string | null>(null);
  const [episodeDetailErrors, setEpisodeDetailErrors] = useState<Record<string, string>>({});

  const [showNewEpisode, setShowNewEpisode] = useState(false);
  const [newEpisodeTitle, setNewEpisodeTitle] = useState("");
  const [creatingEpisode, setCreatingEpisode] = useState(false);
  const [newEpisodeError, setNewEpisodeError] = useState<string | null>(null);

  // --- Create/Open video (per episode) ---
  const [videoLoadingEpisodeId, setVideoLoadingEpisodeId] = useState<string | null>(null);
  const [videoErrors, setVideoErrors] = useState<Record<string, string>>({});

  // --- Sync reference images from Story onto an existing video (per episode) ---
  const [syncLoadingEpisodeId, setSyncLoadingEpisodeId] = useState<string | null>(null);
  const [syncErrors, setSyncErrors] = useState<Record<string, string>>({});
  const [syncMessages, setSyncMessages] = useState<Record<string, string>>({});
  // Characters whose approved image in that episode differs from the Story's current one (detection only).
  const [syncOutdated, setSyncOutdated] = useState<Record<string, string[]>>({});

  useEffect(() => {
    if (!id) return;
    setLoading(true);
    storiesApi
      .getById(id)
      .then((s) => {
        setStory(s);
        setTitle(s.title);
        setPremise(s.premise ?? "");
        setNiche(s.niche ?? "");
      })
      .catch((err) => setError(describeApiError(err, "Không tải được câu chuyện.")))
      .finally(() => setLoading(false));
  }, [id]);

  useEffect(() => {
    if (!id) return;
    storiesApi
      .getState(id)
      .then(setStoryState)
      .catch((err) => setStateError(describeApiError(err, "Không tải được trạng thái truyện.")));
  }, [id]);

  useEffect(() => {
    if (!id) return;
    setBibleLoading(true);
    storiesApi
      .getBible(id)
      .then(setBible)
      .catch((err) => setBibleError(describeApiError(err, "Không tải được Story Bible.")))
      .finally(() => setBibleLoading(false));
  }, [id]);

  const saveOverview = async () => {
    if (!story || savingStyle) return;
    if (!title.trim()) {
      setOverviewError("Tên câu chuyện không được để trống.");
      return;
    }
    setSavingOverview(true);
    setOverviewError(null);
    try {
      const updated = await storiesApi.update(story.id, {
        title: title.trim(),
        premise: premise.trim() || undefined,
        niche: niche.trim() || undefined,
      });
      setStory(updated);
      setEditingOverview(false);
    } catch (err) {
      setOverviewError(describeApiError(err, "Không lưu được thông tin câu chuyện."));
    } finally {
      setSavingOverview(false);
    }
  };

  const changeStyle = async (next: string | null) => {
    if (!story || savingOverview || savingStyle || next === story.stylePresetId) return;
    setSavingStyle(true);
    setStyleError(null);
    try {
      // PUT carries the whole overview, so resend the persisted values to avoid touching them. The style is always
      // sent explicitly: a blank string clears it (omitting it is not relied upon to mean "unchanged").
      const updated = await storiesApi.update(story.id, {
        title: story.title,
        premise: story.premise ?? undefined,
        niche: story.niche ?? undefined,
        stylePresetId: next ?? "",
      });
      // Merge ONLY the style (+ updatedAt): replacing the whole story would overwrite state that changed while the
      // PUT was in flight (e.g. a character image status - which could re-enable a second paid generation).
      setStory((cur) =>
        cur ? { ...cur, stylePresetId: updated.stylePresetId ?? null, updatedAt: updated.updatedAt ?? cur.updatedAt } : cur,
      );
    } catch (err) {
      setStyleError(describeApiError(err, "Không lưu được phong cách hình ảnh."));
    } finally {
      setSavingStyle(false);
    }
  };

  const addCharacter = async () => {
    if (!story) return;
    if (!charName.trim()) {
      setCharError("Nhập tên nhân vật.");
      return;
    }
    setAddingChar(true);
    setCharError(null);
    try {
      const character = await storiesApi.addCharacter(story.id, {
        name: charName.trim(),
        description: charDesc.trim() || undefined,
        visualDescription: charVisual.trim() || undefined,
        behaviorProfile: charAnthropomorphicCat ? "AnthropomorphicCat" : "None",
        kind: charCanon.kind,
        species: charCanon.species.trim() || undefined,
        clothingAndAccessories: charCanon.clothingAndAccessories.trim() || undefined,
        distinctiveFeatures: charCanon.distinctiveFeatures.trim() || undefined,
      });
      setStory((cur) => (cur ? { ...cur, characters: [...cur.characters, character] } : cur));
      setCharName("");
      setCharDesc("");
      setCharVisual("");
      setCharAnthropomorphicCat(false);
      setCharCanon(EMPTY_CANONICAL_FIELDS);
    } catch (err) {
      setCharError(describeApiError(err, "Không thêm được nhân vật."));
    } finally {
      setAddingChar(false);
    }
  };

  const startEditingCharacter = (c: StoryResponse["characters"][number]) => {
    setEditingCharacterId(c.id);
    setEditCharName(c.name);
    setEditCharDesc(c.description ?? "");
    setEditCharVisual(c.visualDescription ?? "");
    setEditCharAnthropomorphicCat(c.behaviorProfile === "AnthropomorphicCat");
    setEditCharCanon(canonicalFieldsOf(c));
    setCharEditError(null);
  };

  const cancelEditingCharacter = () => {
    setEditingCharacterId(null);
    setCharEditError(null);
  };

  const saveCharacterEdit = async () => {
    if (!story || !editingCharacterId) return;
    if (!editCharName.trim()) {
      setCharEditError("Tên nhân vật không được để trống.");
      return;
    }
    setSavingCharEdit(true);
    setCharEditError(null);
    try {
      const updated = await storiesApi.updateCharacter(story.id, editingCharacterId, {
        name: editCharName.trim(),
        description: editCharDesc.trim() || undefined,
        visualDescription: editCharVisual.trim() || undefined,
        behaviorProfile: editCharAnthropomorphicCat ? "AnthropomorphicCat" : "None",
        // Canonical text fields: "" clears, so always send the trimmed value (omitting would mean "unchanged").
        kind: editCharCanon.kind,
        species: editCharCanon.species.trim(),
        clothingAndAccessories: editCharCanon.clothingAndAccessories.trim(),
        distinctiveFeatures: editCharCanon.distinctiveFeatures.trim(),
      });
      // Merge the edited fields only - image state (status / candidate) may have changed while the PUT was in flight
      // and must not be overwritten by this response.
      setStory((cur) =>
        cur
          ? {
              ...cur,
              characters: cur.characters.map((c) =>
                c.id === updated.id
                  ? {
                      ...c,
                      name: updated.name,
                      description: updated.description,
                      visualDescription: updated.visualDescription,
                      behaviorProfile: updated.behaviorProfile,
                      kind: updated.kind,
                      species: updated.species,
                      clothingAndAccessories: updated.clothingAndAccessories,
                      distinctiveFeatures: updated.distinctiveFeatures,
                      missingReferenceFields: updated.missingReferenceFields,
                      canGenerateReference: updated.canGenerateReference,
                    }
                  : c,
              ),
            }
          : cur,
      );
      setEditingCharacterId(null);
    } catch (err) {
      setCharEditError(describeApiError(err, "Không lưu được thay đổi nhân vật."));
    } finally {
      setSavingCharEdit(false);
    }
  };

  const addLocation = async () => {
    if (!story) return;
    if (!locName.trim()) {
      setLocError("Nhập tên bối cảnh.");
      return;
    }
    setAddingLoc(true);
    setLocError(null);
    try {
      const location = await storiesApi.addLocation(story.id, {
        name: locName.trim(),
        description: locDesc.trim() || undefined,
        visualDescription: locVisual.trim() || undefined,
      });
      setStory((cur) => (cur ? { ...cur, locations: [...cur.locations, location] } : cur));
      setLocName("");
      setLocDesc("");
      setLocVisual("");
    } catch (err) {
      setLocError(describeApiError(err, "Không thêm được bối cảnh."));
    } finally {
      setAddingLoc(false);
    }
  };

  // Merges ONLY the given fields into ONE character (never replaces the whole story / other characters).
  const patchCharacter = (characterId: string, patch: Partial<StoryCharacterResponse>) => {
    setStory((cur) =>
      cur
        ? { ...cur, characters: cur.characters.map((c) => (c.id === characterId ? { ...c, ...patch } : c)) }
        : cur,
    );
  };

  const updateLocationReferenceImage = (locationId: string, result: StoryReferenceImageResponse) => {
    setStory((cur) =>
      cur
        ? {
            ...cur,
            locations: cur.locations.map((l) =>
              l.id === locationId
                ? { ...l, referenceImageStatus: result.status, hasReferenceImage: !!result.imagePath }
                : l,
            ),
          }
        : cur,
    );
  };

  const generateBible = async () => {
    if (!story) return;
    if (bible && !window.confirm('Tạo lại Story Bible sẽ ghi đè Bible hiện tại. Tiếp tục?')) {
      return;
    }
    setGeneratingBible(true);
    setBibleFormError(null);
    try {
      const mainCharacters = splitList(bibleMainCharacters);
      const locations = splitList(bibleLocations);
      const result = await storiesApi.generateBible(story.id, {
        theme: bibleTheme.trim() || undefined,
        genre: bibleGenre.trim() || undefined,
        tone: bibleTone.trim() || undefined,
        targetAudience: bibleAudience.trim() || undefined,
        mainCharacters: mainCharacters.length > 0 ? mainCharacters : undefined,
        locations: locations.length > 0 ? locations : undefined,
        storyRulesConstraints: bibleConstraints.trim() || undefined,
        desiredEpisodeCount: bibleEpisodeCount.trim() ? Number(bibleEpisodeCount) : undefined,
      });
      setBible(result);
      setShowBibleForm(false);
    } catch (err) {
      setBibleFormError(describeApiError(err, "Không tạo được Story Bible."));
    } finally {
      setGeneratingBible(false);
    }
  };

  const toggleEpisode = async (episodeId: string) => {
    if (expandedEpisodeId === episodeId) {
      setExpandedEpisodeId(null);
      return;
    }
    setExpandedEpisodeId(episodeId);
    if (!story || episodeDetails[episodeId]) return;
    setEpisodeDetailLoadingId(episodeId);
    try {
      const detail = await storiesApi.getEpisode(story.id, episodeId);
      setEpisodeDetails((cur) => ({ ...cur, [episodeId]: detail }));
    } catch (err) {
      setEpisodeDetailErrors((cur) => ({
        ...cur,
        [episodeId]: describeApiError(err, "Không tải được chi tiết tập phim."),
      }));
    } finally {
      setEpisodeDetailLoadingId(null);
    }
  };

  const createEpisode = async () => {
    if (!story) return;
    if (!newEpisodeTitle.trim()) {
      setNewEpisodeError("Nhập tên tập phim.");
      return;
    }
    setCreatingEpisode(true);
    setNewEpisodeError(null);
    try {
      const nextNumber = story.episodes.length
        ? Math.max(...story.episodes.map((e) => e.episodeNumber)) + 1
        : 1;
      const latest = story.episodes.length
        ? [...story.episodes].sort((a, b) => b.episodeNumber - a.episodeNumber)[0]
        : null;
      const episode = await storiesApi.addEpisode(story.id, {
        episodeNumber: nextNumber,
        title: newEpisodeTitle.trim(),
        previousEpisodeId: latest?.id,
      });
      setStory((cur) =>
        cur
          ? {
              ...cur,
              episodes: [
                ...cur.episodes,
                {
                  id: episode.id,
                  storyId: episode.storyId,
                  episodeNumber: episode.episodeNumber,
                  title: episode.title,
                  contentProjectId: episode.contentProjectId,
                  status: episode.status,
                },
              ],
              episodeCount: cur.episodeCount + 1,
            }
          : cur,
      );
      navigate(`/stories/${story.id}/episodes/${episode.id}/new`);
    } catch (err) {
      setNewEpisodeError(describeApiError(err, "Không tạo được tập phim mới."));
    } finally {
      setCreatingEpisode(false);
    }
  };

  const handleCreateOrOpenVideo = async (episodeId: string) => {
    if (!story) return;
    setVideoLoadingEpisodeId(episodeId);
    setVideoErrors((cur) => {
      const next = { ...cur };
      delete next[episodeId];
      return next;
    });
    try {
      const result = await storiesApi.createOrOpenVideo(story.id, episodeId);
      navigate(`/projects/${result.contentProjectId}`);
    } catch (err) {
      setVideoErrors((cur) => ({
        ...cur,
        [episodeId]: describeApiError(err, "Không tạo/mở được video cho tập này."),
      }));
    } finally {
      setVideoLoadingEpisodeId(null);
    }
  };

  const handleSyncVideoReferences = async (episodeId: string) => {
    if (!story) return;
    setSyncLoadingEpisodeId(episodeId);
    setSyncErrors((cur) => {
      const next = { ...cur };
      delete next[episodeId];
      return next;
    });
    setSyncMessages((cur) => {
      const next = { ...cur };
      delete next[episodeId];
      return next;
    });
    setSyncOutdated((cur) => {
      const next = { ...cur };
      delete next[episodeId];
      return next;
    });
    try {
      const result = await storiesApi.syncVideoReferences(story.id, episodeId);
      const outdated = result.outdatedLabels ?? [];
      const synced = result.syncedLabels ?? [];
      if (outdated.length > 0) {
        setSyncOutdated((cur) => ({ ...cur, [episodeId]: outdated }));
      }
      setSyncMessages((cur) => ({
        ...cur,
        [episodeId]: synced.length > 0 ? `Đã đồng bộ: ${synced.join(", ")}` : "Không có ảnh mới để đồng bộ.",
      }));
    } catch (err) {
      setSyncErrors((cur) => ({
        ...cur,
        [episodeId]: describeApiError(err, "Không đồng bộ được ảnh tham chiếu từ Story."),
      }));
    } finally {
      setSyncLoadingEpisodeId(null);
    }
  };

  if (loading) {
    return (
      <main className="wz">
        <TopNav />
        <Loading />
      </main>
    );
  }

  if (error || !story) {
    return (
      <main className="wz">
        <TopNav />
        <ErrorMessage message={error ?? "Không tìm thấy câu chuyện."} />
        <p className="wz-hint">
          <Link to="/stories">← Về danh sách câu chuyện</Link>
        </p>
      </main>
    );
  }

  const summary = summarizeStory(story);
  const sortedEpisodes = [...story.episodes].sort((a, b) => a.episodeNumber - b.episodeNumber);
  const stateRows: [string, string[]][] = storyState
    ? [
        ["Sự kiện quan trọng", storyState.importantEvents],
        ["Mạch truyện còn mở", storyState.openStoryThreads],
        ["Xung đột chưa giải quyết", storyState.unresolvedConflicts],
        ["Sự thật đã biết", storyState.knownFacts],
      ]
    : [];
  const stateListRows = stateRows.filter(([, items]) => items.length > 0);

  return (
    <main className="wz">
      <TopNav />
      <p className="wz-hint">
        <Link to="/stories">← Về danh sách câu chuyện</Link>
      </p>

      {/* --- Overview: always visible, compact --- */}
      <section style={{ marginBottom: 28 }}>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: 10, flexWrap: "wrap" }}>
          <h1 style={{ margin: 0 }}>{story.title}</h1>
          <StatusBadge tone={summary.tone}>{summary.label}</StatusBadge>
        </div>
        <p className="wz-hint" style={{ marginTop: 4 }}>
          {story.niche && `${story.niche} · `}
          {story.language.toUpperCase()} · {story.aspectRatio} · {story.episodeCount} tập · Cập nhật{" "}
          {new Date(story.updatedAt).toLocaleDateString()}
        </p>

        {editingOverview ? (
          <div className="wz-card" style={{ marginTop: 12 }}>
            <Field label="Tên câu chuyện">
              <input type="text" value={title} onChange={(e) => setTitle(e.target.value)} />
            </Field>
            <Field label="Chủ đề">
              <input type="text" value={niche} onChange={(e) => setNiche(e.target.value)} />
            </Field>
            <Field label="Ý tưởng / tiền đề">
              <textarea rows={3} value={premise} onChange={(e) => setPremise(e.target.value)} />
            </Field>
            <ErrorMessage message={overviewError} />
            <div className="wz-actions">
              <button
                type="button"
                className="wz-btn wz-btn-primary"
                disabled={savingOverview || savingStyle}
                onClick={saveOverview}
              >
                {savingOverview ? "Đang lưu..." : "Lưu"}
              </button>
              <button
                type="button"
                className="wz-btn"
                disabled={savingOverview}
                onClick={() => {
                  setEditingOverview(false);
                  setTitle(story.title);
                  setPremise(story.premise ?? "");
                  setNiche(story.niche ?? "");
                  setOverviewError(null);
                }}
              >
                Hủy
              </button>
            </div>
          </div>
        ) : (
          <>
            {story.premise && <p style={{ marginTop: 10 }}>{story.premise}</p>}
            <div className="wz-actions">
              <button type="button" className="wz-btn wz-btn-sm" onClick={() => setEditingOverview(true)}>
                Chỉnh sửa
              </button>
            </div>
          </>
        )}
      </section>

      {/* --- Visual style: story-level, used for character images + inherited by new episode videos --- */}
      <section style={{ marginBottom: 28 }}>
        <h2>Phong cách hình ảnh</h2>
        <p className="wz-hint">
          Áp dụng cho ảnh nhân vật của câu chuyện và được kế thừa bởi video của mọi tập mới. Đổi phong cách chỉ ảnh
          hưởng các lần tạo sau - các tập đã tạo giữ nguyên phong cách hiện tại. Ảnh đã tạo/đã duyệt cũng không tự đổi:
          nhân vật có ảnh tạo dưới phong cách khác nên được tạo lại bằng &quot;Tạo lại&quot; cho khớp, và ảnh đã được
          chép vào các tập mới giữ nguyên diện mạo lúc được tạo.
        </p>
        <StoryStylePicker
          selectedId={story.stylePresetId}
          onSelect={changeStyle}
          disabled={savingStyle || savingOverview}
        />
        {savingStyle && <Loading label="Đang lưu phong cách..." />}
        <ErrorMessage message={styleError} />
      </section>

      {/* --- Characters --- */}
      <section style={{ marginBottom: 28 }}>
        <h2>Nhân vật</h2>
        <p className="wz-hint">
          Ảnh tham chiếu nhân vật được tạo toàn thân trên nền trơn, chỉ để làm ảnh nhận diện (không có bối cảnh). Ảnh đã
          tạo trước đây mà vẫn còn hiện nền/bối cảnh nên được tạo lại bằng nút &quot;Tạo lại&quot;. Ảnh chuẩn đã duyệt
          không bao giờ bị thay âm thầm: ảnh tạo lại hoặc tải lên sẽ nằm ở mục &quot;Ảnh đề xuất&quot; cho đến khi bạn
          duyệt và xác nhận thay thế.
        </p>
        <ReferenceConsistencyInfo />
        {story.characters.length === 0 ? (
          <p className="wz-hint">Chưa có nhân vật nào.</p>
        ) : (
          <div className="wz-story-grid">
            {story.characters.map((c) => {
              const editing = editingCharacterId === c.id;
              return (
                <div className="wz-story-entity-card" key={c.id}>
                  {editing ? (
                    <>
                      <Field label="Tên nhân vật">
                        <input type="text" value={editCharName} onChange={(e) => setEditCharName(e.target.value)} />
                      </Field>
                      <Field label="Vai trò, tính cách, mô tả...">
                        <textarea rows={2} value={editCharDesc} onChange={(e) => setEditCharDesc(e.target.value)} />
                      </Field>
                      <Field label="Mô tả ngoại hình">
                        <textarea rows={2} value={editCharVisual} onChange={(e) => setEditCharVisual(e.target.value)} />
                      </Field>
                      <CharacterCanonicalFields
                        values={editCharCanon}
                        onChange={setEditCharCanon}
                        disabled={savingCharEdit}
                      />
                      <label style={{ display: "block", fontSize: 14, margin: "0 0 12px" }}>
                        <input
                          type="checkbox"
                          checked={editCharAnthropomorphicCat}
                          onChange={(e) => setEditCharAnthropomorphicCat(e.target.checked)}
                        />{" "}
                        🐱 Đây là nhân vật mèo/mèo con có hành vi giống người (ngồi thẳng, dùng chân trước cầm đồ vật như
                        tay)
                      </label>
                      <ErrorMessage message={charEditError} />
                      <div className="wz-actions">
                        <button
                          type="button"
                          className="wz-btn wz-btn-primary wz-btn-sm"
                          disabled={savingCharEdit}
                          onClick={saveCharacterEdit}
                        >
                          {savingCharEdit ? "Đang lưu..." : "Lưu"}
                        </button>
                        <button
                          type="button"
                          className="wz-btn wz-btn-sm"
                          disabled={savingCharEdit}
                          onClick={cancelEditingCharacter}
                        >
                          Hủy
                        </button>
                      </div>
                    </>
                  ) : (
                    <>
                      <strong>{c.name}</strong>
                      {c.description && <p className="wz-hint">{c.description}</p>}
                      {c.visualDescription && <p className="wz-hint">👁 {c.visualDescription}</p>}
                      {((c.kind && c.kind !== "Unspecified") || c.species) && (
                        <p className="wz-hint">
                          🧬 {[c.kind && c.kind !== "Unspecified" ? CHARACTER_KIND_LABEL[c.kind] : null, c.species]
                            .filter(Boolean)
                            .join(" · ")}
                        </p>
                      )}
                      {c.clothingAndAccessories && <p className="wz-hint">👕 {c.clothingAndAccessories}</p>}
                      {c.distinctiveFeatures && <p className="wz-hint">✨ {c.distinctiveFeatures}</p>}
                      {c.behaviorProfile === "AnthropomorphicCat" && (
                        <p className="wz-hint">🐱 Mèo có hành vi giống người (ngồi thẳng, dùng chân trước như tay)</p>
                      )}
                    </>
                  )}
                  {/* Stays mounted (only hidden) while editing, so an in-flight paid generation keeps its guard. */}
                  <div hidden={editing}>
                    <StoryCharacterReferencePanel
                      storyId={story.id}
                      character={c}
                      onChange={(patch) => patchCharacter(c.id, patch)}
                    />
                    <div className="wz-actions" style={{ marginTop: 8 }}>
                      <button type="button" className="wz-btn wz-btn-sm" onClick={() => startEditingCharacter(c)}>
                        Chỉnh sửa
                      </button>
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        )}

        <div className="wz-card" style={{ marginTop: 12 }}>
          <Field label="Tên nhân vật">
            <input type="text" value={charName} onChange={(e) => setCharName(e.target.value)} />
          </Field>
          <Field label="Vai trò, tính cách, mô tả..." hint="Vai trò trong truyện + tính cách + mô tả chung, gộp trong một đoạn văn.">
            <textarea rows={2} value={charDesc} onChange={(e) => setCharDesc(e.target.value)} />
          </Field>
          <Field label="Mô tả ngoại hình (để giữ nhất quán hình ảnh giữa các tập)">
            <textarea rows={2} value={charVisual} onChange={(e) => setCharVisual(e.target.value)} />
          </Field>
          <CharacterCanonicalFields values={charCanon} onChange={setCharCanon} disabled={addingChar} />
          <label style={{ display: "block", fontSize: 14, margin: "0 0 12px" }}>
            <input
              type="checkbox"
              checked={charAnthropomorphicCat}
              onChange={(e) => setCharAnthropomorphicCat(e.target.checked)}
            />{" "}
            🐱 Đây là nhân vật mèo/mèo con có hành vi giống người (ngồi thẳng, dùng chân trước cầm đồ vật như tay)
          </label>
          <ErrorMessage message={charError} />
          <div className="wz-actions">
            <button type="button" className="wz-btn wz-btn-primary wz-btn-sm" disabled={addingChar} onClick={addCharacter}>
              {addingChar ? "Đang thêm..." : "+ Thêm nhân vật"}
            </button>
          </div>
        </div>
      </section>

      {/* --- Locations --- */}
      <section style={{ marginBottom: 28 }}>
        <h2>Bối cảnh</h2>
        {story.locations.length === 0 ? (
          <p className="wz-hint">Chưa có bối cảnh nào.</p>
        ) : (
          <div className="wz-story-grid">
            {story.locations.map((l) => (
              <div className="wz-story-entity-card" key={l.id}>
                <strong>{l.name}</strong>
                {l.description && <p className="wz-hint">{l.description}</p>}
                {l.visualDescription && <p className="wz-hint">👁 {l.visualDescription}</p>}
                <StoryReferenceImage
                  storyId={story.id}
                  kind="location"
                  entityId={l.id}
                  status={l.referenceImageStatus}
                  hasImage={l.hasReferenceImage}
                  onUpdated={(result) => updateLocationReferenceImage(l.id, result)}
                />
              </div>
            ))}
          </div>
        )}

        <div className="wz-card" style={{ marginTop: 12 }}>
          <Field label="Tên bối cảnh">
            <input type="text" value={locName} onChange={(e) => setLocName(e.target.value)} />
          </Field>
          <Field label="Mô tả">
            <textarea rows={2} value={locDesc} onChange={(e) => setLocDesc(e.target.value)} />
          </Field>
          <Field label="Mô tả hình ảnh (để giữ nhất quán hình ảnh giữa các tập)">
            <textarea rows={2} value={locVisual} onChange={(e) => setLocVisual(e.target.value)} />
          </Field>
          <ErrorMessage message={locError} />
          <div className="wz-actions">
            <button type="button" className="wz-btn wz-btn-primary wz-btn-sm" disabled={addingLoc} onClick={addLocation}>
              {addingLoc ? "Đang thêm..." : "+ Thêm bối cảnh"}
            </button>
          </div>
        </div>
      </section>

      {/* --- Story Bible --- */}
      <section style={{ marginBottom: 28 }}>
        <h2>Story Bible</h2>
        <ErrorMessage message={bibleError} />
        {bibleLoading ? (
          <Loading label="Đang tải Story Bible..." />
        ) : bible ? (
          <>
            <p className="wz-hint">
              {bible.tone && (
                <>
                  Tông truyện: <strong>{bible.tone}</strong>.{" "}
                </>
              )}
              {bible.premise}
            </p>
            <details className="wz-advanced">
              <summary>Xem chi tiết Story Bible</summary>
              {bible.storyArc && (
                <div className="wz-story-block">
                  <h4>Mạch truyện tổng thể</h4>
                  <p>{bible.storyArc}</p>
                </div>
              )}
              {bible.characterDefinitions && (
                <div className="wz-story-block">
                  <h4>Định nghĩa nhân vật</h4>
                  <p>{bible.characterDefinitions}</p>
                </div>
              )}
              {bible.characterRelationships && (
                <div className="wz-story-block">
                  <h4>Quan hệ nhân vật</h4>
                  <p>{bible.characterRelationships}</p>
                </div>
              )}
              {bible.worldRules.length > 0 && (
                <div className="wz-story-block">
                  <h4>Luật lệ thế giới</h4>
                  <ul>
                    {bible.worldRules.map((r) => (
                      <li key={r}>{r}</li>
                    ))}
                  </ul>
                </div>
              )}
              {bible.visualConsistencyRules.length > 0 && (
                <div className="wz-story-block">
                  <h4>Quy tắc nhất quán hình ảnh</h4>
                  <ul>
                    {bible.visualConsistencyRules.map((r) => (
                      <li key={r}>{r}</li>
                    ))}
                  </ul>
                </div>
              )}
              {bible.recurringElements.length > 0 && (
                <div className="wz-story-block">
                  <h4>Yếu tố lặp lại</h4>
                  <ul>
                    {bible.recurringElements.map((r) => (
                      <li key={r}>{r}</li>
                    ))}
                  </ul>
                </div>
              )}
              {bible.storyConstraints.length > 0 && (
                <div className="wz-story-block">
                  <h4>Ràng buộc truyện</h4>
                  <ul>
                    {bible.storyConstraints.map((r) => (
                      <li key={r}>{r}</li>
                    ))}
                  </ul>
                </div>
              )}
            </details>
            <div className="wz-actions">
              <button type="button" className="wz-btn wz-btn-sm" onClick={() => setShowBibleForm((s) => !s)}>
                {showBibleForm ? "Đóng" : "Tạo lại Story Bible"}
              </button>
              <CostNote kind="text" />
            </div>
          </>
        ) : (
          <EmptyState
            title="Chưa có Story Bible"
            description="Story Bible giúp AI giữ nhất quán nhân vật, luật thế giới và văn phong xuyên suốt các tập."
            action={
              <button type="button" className="wz-btn wz-btn-primary wz-btn-sm" onClick={() => setShowBibleForm(true)}>
                Tạo Story Bible
              </button>
            }
          />
        )}

        {showBibleForm && (
          <div className="wz-card" style={{ marginTop: 12 }}>
            <Field label="Chủ đề (tùy chọn)">
              <input type="text" value={bibleTheme} onChange={(e) => setBibleTheme(e.target.value)} />
            </Field>
            <Field label="Thể loại (tùy chọn)">
              <input type="text" value={bibleGenre} onChange={(e) => setBibleGenre(e.target.value)} />
            </Field>
            <Field label="Tông truyện (tùy chọn)">
              <input type="text" value={bibleTone} onChange={(e) => setBibleTone(e.target.value)} />
            </Field>
            <Field label="Đối tượng khán giả (tùy chọn)">
              <input type="text" value={bibleAudience} onChange={(e) => setBibleAudience(e.target.value)} />
            </Field>
            <Field label="Nhân vật chính (tùy chọn)" hint="Cách nhau bằng dấu phẩy.">
              <input type="text" value={bibleMainCharacters} onChange={(e) => setBibleMainCharacters(e.target.value)} />
            </Field>
            <Field label="Bối cảnh chính (tùy chọn)" hint="Cách nhau bằng dấu phẩy.">
              <input type="text" value={bibleLocations} onChange={(e) => setBibleLocations(e.target.value)} />
            </Field>
            <Field label="Luật lệ / ràng buộc truyện (tùy chọn)">
              <textarea rows={2} value={bibleConstraints} onChange={(e) => setBibleConstraints(e.target.value)} />
            </Field>
            <Field label="Số tập dự kiến (tùy chọn)">
              <input
                type="number"
                min={1}
                value={bibleEpisodeCount}
                onChange={(e) => setBibleEpisodeCount(e.target.value)}
              />
            </Field>
            <ErrorMessage message={bibleFormError} />
            <div className="wz-actions">
              <button type="button" className="wz-btn wz-btn-primary" disabled={generatingBible} onClick={generateBible}>
                {generatingBible ? "Đang tạo..." : bible ? "Tạo lại Story Bible" : "Tạo Story Bible"}
              </button>
              <CostNote kind="text" />
            </div>
          </div>
        )}
      </section>

      {/* --- Story State: compact, read-only --- */}
      <section style={{ marginBottom: 28 }}>
        <h2>Trạng thái truyện hiện tại</h2>
        <ErrorMessage message={stateError} />
        {storyState && (
          <div className="wz-credits">
            <div className="wz-kv">
              {storyState.currentLocation && (
                <span>
                  <span className="wz-kv-k">Bối cảnh hiện tại</span>
                  <span className="wz-kv-v">{storyState.currentLocation}</span>
                </span>
              )}
              {storyState.currentObjective && (
                <span>
                  <span className="wz-kv-k">Mục tiêu hiện tại</span>
                  <span className="wz-kv-v">{storyState.currentObjective}</span>
                </span>
              )}
              {storyState.nextPlannedDestination && (
                <span>
                  <span className="wz-kv-k">Điểm đến tiếp theo</span>
                  <span className="wz-kv-v">{storyState.nextPlannedDestination}</span>
                </span>
              )}
            </div>
            {storyState.characterStates && <p className="wz-hint" style={{ marginTop: 8 }}>{storyState.characterStates}</p>}
            {stateListRows.map(([label, items]) => (
              <div className="wz-story-block" key={label} style={{ marginTop: 10 }}>
                <h4>{label}</h4>
                <ul>
                  {items.map((item) => (
                    <li key={item}>{item}</li>
                  ))}
                </ul>
              </div>
            ))}
            {storyState.notes && <p className="wz-hint" style={{ marginTop: 8 }}>{storyState.notes}</p>}
          </div>
        )}
      </section>

      {/* --- Episodes: vertical timeline --- */}
      <section>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: 10 }}>
          <h2 style={{ margin: 0 }}>Các tập phim</h2>
          <button type="button" className="wz-btn wz-btn-primary wz-btn-sm" onClick={() => setShowNewEpisode((s) => !s)}>
            {showNewEpisode ? "Đóng" : "+ Tạo tập mới"}
          </button>
        </div>

        {showNewEpisode && (
          <div className="wz-card" style={{ marginTop: 12, marginBottom: 12 }}>
            <Field label="Tên tập phim">
              <input type="text" value={newEpisodeTitle} onChange={(e) => setNewEpisodeTitle(e.target.value)} />
            </Field>
            <ErrorMessage message={newEpisodeError} />
            <div className="wz-actions">
              <button type="button" className="wz-btn wz-btn-primary" disabled={creatingEpisode} onClick={createEpisode}>
                {creatingEpisode ? "Đang tạo..." : "Tạo tập & bắt đầu"}
              </button>
            </div>
          </div>
        )}

        {sortedEpisodes.length === 0 ? (
          <EmptyState title="Chưa có tập phim nào" description='Bấm "+ Tạo tập mới" ở trên để bắt đầu tập đầu tiên.' />
        ) : (
          <ul className="wz-story-timeline" style={{ marginTop: 12 }}>
            {sortedEpisodes.map((ep) => {
              const expanded = expandedEpisodeId === ep.id;
              const detail = episodeDetails[ep.id];
              const detailLoading = episodeDetailLoadingId === ep.id;
              const detailError = episodeDetailErrors[ep.id];
              const hasScript = ep.status === "Scripted" || ep.status === "Completed";
              const videoLoading = videoLoadingEpisodeId === ep.id;
              const videoError = videoErrors[ep.id];
              const syncLoading = syncLoadingEpisodeId === ep.id;
              const syncError = syncErrors[ep.id];
              const syncMessage = syncMessages[ep.id];
              const outdatedLabels = syncOutdated[ep.id] ?? [];
              return (
                <li key={ep.id} className="wz-story-episode">
                  <button type="button" className="wz-story-episode-head" onClick={() => toggleEpisode(ep.id)}>
                    <strong>
                      Tập {ep.episodeNumber}: {ep.title}
                    </strong>
                    <StatusBadge tone={EPISODE_STATUS_TONE[ep.status]}>{EPISODE_STATUS_LABEL[ep.status]}</StatusBadge>
                    <span className="wz-hint">{expanded ? "▲ Thu gọn" : "▼ Xem chi tiết"}</span>
                  </button>

                  {expanded && (
                    <div className="wz-story-episode-detail">
                      <ErrorMessage message={detailError} />
                      {detailLoading ? (
                        <Loading label="Đang tải chi tiết tập..." />
                      ) : detail ? (
                        <>
                          {detail.summary && <p style={{ marginTop: 0 }}>{detail.summary}</p>}
                          <p className="wz-hint">
                            {detail.storyStateSnapshot?.currentLocation &&
                              `Bối cảnh: ${detail.storyStateSnapshot.currentLocation} · `}
                            Tạo lúc {new Date(detail.createdAt).toLocaleString()}
                          </p>
                          {!detail.summary && !detail.script && (
                            <p className="wz-hint">Tập này chưa có dàn ý hoặc kịch bản.</p>
                          )}
                        </>
                      ) : null}
                      <div className="wz-actions">
                        <Link className="wz-btn wz-btn-sm" to={`/stories/${story.id}/episodes/${ep.id}/new`}>
                          {ep.status === "Draft" ? "Tiếp tục viết tập này" : "Mở tập này"}
                        </Link>
                        {hasScript && (
                          <button
                            type="button"
                            className="wz-btn wz-btn-sm"
                            disabled={videoLoading}
                            onClick={() => handleCreateOrOpenVideo(ep.id)}
                          >
                            {videoLoading ? "Đang xử lý..." : ep.contentProjectId ? "Mở video" : "Tạo video"}
                          </button>
                        )}
                        {ep.contentProjectId && (
                          <button
                            type="button"
                            className="wz-btn wz-btn-sm"
                            disabled={syncLoading}
                            onClick={() => handleSyncVideoReferences(ep.id)}
                          >
                            {syncLoading ? "Đang đồng bộ..." : "Đồng bộ ảnh từ Story"}
                          </button>
                        )}
                      </div>
                      <ErrorMessage message={videoError} />
                      <ErrorMessage message={syncError} />
                      {syncMessage && <p className="wz-hint">{syncMessage}</p>}
                      {outdatedLabels.length > 0 && (
                        <div
                          className="wz-hint"
                          role="status"
                          style={{ border: "1px solid #b8860b", borderRadius: 8, padding: "8px 10px", opacity: 1 }}
                        >
                          {outdatedLabels.map((label) => (
                            <p key={label} style={{ margin: "0 0 4px" }}>
                              ℹ️ Ảnh của {label} trong tập này khác ảnh chuẩn hiện tại của Story - tập mới sẽ dùng ảnh
                              chuẩn; tập này giữ nguyên.
                            </p>
                          ))}
                        </div>
                      )}
                    </div>
                  )}
                </li>
              );
            })}
          </ul>
        )}
      </section>
    </main>
  );
}

export default StoryDetailPage;
