# Entity Framework Core Tracking Issues Analysis

## Issues Found

### 1. ⚠️ **Critical: ClipPlanService.cs - Potential Tracking Conflict on Scene Deletion**

**Location:** [ClipPlanService.cs](src/AiContentFactory.Application/Storyboards/ClipPlanService.cs#L47-L60)

**Issue:**
```csharp
var storyboard = await _storyboardRepository.GetByContentProjectIdAsync(contentProjectId, cancellationToken);
// ...
storyboard.ClearScenes();  // Line 54 - Clears internal collection
foreach (var chunk in chunks)
{
    storyboard.AddScene(...);  // Line 55-58 - Adds new scenes
}
await _storyboardRepository.SaveChangesAsync(cancellationToken);
```

**Problem Analysis:**
- `GetByContentProjectIdAsync()` includes all `Scenes` with `.Include(s => s.Scenes)`, so old scenes are **tracked by DbContext**
- `ClearScenes()` removes scenes from the internal `_scenes` List, but EF Core still tracks the removed entities
- When `SaveChangesAsync()` executes, EF Core detects these removed scenes as "deleted" changes
- This works most of the time, BUT can cause `DbUpdateConcurrencyException` if:
  - The same DbContext processes multiple storyboards in rapid succession
  - Concurrent requests modify the same storyboard simultaneously
  - Change tracking state gets confused due to relationship management

**Risk Level:** 🔴 **HIGH**

---

### 2. ⚠️ **Medium: StoryboardService.cs - No Explicit AsNoTracking() on Multi-Operation Flow**

**Location:** [StoryboardService.cs](src/AiContentFactory.Application/Storyboards/StoryboardService.cs#L43-L50)

**Method: AddSceneAsync**
```csharp
public async Task<StoryboardResponse> AddSceneAsync(Guid contentProjectId, CreateSceneRequest request, CancellationToken cancellationToken = default)
{
    var storyboard = await GetOrCreateStoryboardEntityAsync(contentProjectId, cancellationToken);
    storyboard.AddScene(...);  // Modifies tracked entity
    await _repository.SaveChangesAsync(cancellationToken);
    return StoryboardResponse.FromDomain(storyboard);  // Returns storyboard that may still be tracked
}
```

**Problem Analysis:**
- Each scene modification creates/modifies a tracked entity
- If the same storyboard is queried again in the same scope, it returns the **in-memory tracked instance** rather than fresh DB data
- Multiple quick modifications could cause race conditions or stale data
- The response returns the tracked entity, which could be modified further unintentionally

**Risk Level:** 🟡 **MEDIUM**

---

### 3. ⚠️ **Medium: Repository Query Pattern - Always Includes Scenes**

**Location:** [StoryboardRepository.cs](src/AiContentFactory.Infrastructure/Persistence/Repositories/StoryboardRepository.cs#L13-L15)

```csharp
public Task<Storyboard?> GetByContentProjectIdAsync(Guid contentProjectId, CancellationToken cancellationToken = default) =>
    _db.Storyboards
        .Include(s => s.Scenes)
        .FirstOrDefaultAsync(s => s.ContentProjectId == contentProjectId, cancellationToken);
```

**Problem Analysis:**
- Always includes all scenes, even when only the storyboard metadata is needed
- Loads unnecessary data for read-only operations
- All loaded scenes are tracked, increasing memory footprint
- Makes `ClearScenes()` operations more risky

**Risk Level:** 🟡 **MEDIUM**

---

## Recommended Fixes

### Fix 1: Use ExecuteDeleteAsync() for Scene Deletion (ClipPlanService.cs)

**Current Code (Lines 47-60):**
```csharp
var storyboard = await _storyboardRepository.GetByContentProjectIdAsync(contentProjectId, cancellationToken);
if (storyboard is null)
{
    storyboard = Storyboard.Create(contentProjectId);
    await _storyboardRepository.AddAsync(storyboard, cancellationToken);
}

storyboard.ClearScenes();
foreach (var chunk in chunks)
{
    storyboard.AddScene(request.ClipDurationSeconds, chunk, string.Empty, string.Empty, SceneVisualType.AiVideo);
}

await _storyboardRepository.SaveChangesAsync(cancellationToken);
```

**Recommended Solution:**
```csharp
// Fetch fresh storyboard without tracking
var storyboard = await _storyboardRepository.GetByContentProjectIdAsyncNoTracking(contentProjectId, cancellationToken);
if (storyboard is null)
{
    storyboard = Storyboard.Create(contentProjectId);
    await _storyboardRepository.AddAsync(storyboard, cancellationToken);
}
else
{
    // Delete old scenes directly from database, not from tracked entity
    await _storyboardRepository.DeleteScenesByStoryboardIdAsync(storyboard.Id, cancellationToken);
    // Re-attach storyboard to context without scenes
    storyboard = await _storyboardRepository.GetByContentProjectIdAsyncNoTracking(contentProjectId, cancellationToken)
        ?? throw new InvalidOperationException("Storyboard was deleted");
}

foreach (var chunk in chunks)
{
    storyboard.AddScene(request.ClipDurationSeconds, chunk, string.Empty, string.Empty, SceneVisualType.AiVideo);
}

await _storyboardRepository.SaveChangesAsync(cancellationToken);
```

**Why This Works:**
- ✅ Uses `ExecuteDeleteAsync()` directly on the DB set (EF Core 7+)
- ✅ Avoids tracking old scenes
- ✅ Clean separation: DB deletion vs. in-memory object modification
- ✅ Prevents concurrency exceptions

---

### Fix 2: Add AsNoTracking() Method to Repository

**Add to [StoryboardRepository.cs](src/AiContentFactory.Infrastructure/Persistence/Repositories/StoryboardRepository.cs):**

```csharp
public Task<Storyboard?> GetByContentProjectIdAsyncNoTracking(Guid contentProjectId, CancellationToken cancellationToken = default) =>
    _db.Storyboards
        .Include(s => s.Scenes)
        .AsNoTracking()
        .FirstOrDefaultAsync(s => s.ContentProjectId == contentProjectId, cancellationToken);

public Task DeleteScenesByStoryboardIdAsync(Guid storyboardId, CancellationToken cancellationToken = default) =>
    _db.Scenes
        .Where(s => s.StoryboardId == storyboardId)
        .ExecuteDeleteAsync(cancellationToken);
```

---

### Fix 3: Improve StoryboardService Response Pattern

**Current Code (AddSceneAsync):**
```csharp
return StoryboardResponse.FromDomain(storyboard);  // Returns tracked entity
```

**Recommended:**
```csharp
// Re-fetch to ensure fresh data is returned
var refreshedStoryboard = await _repository.GetByContentProjectIdAsync(contentProjectId, cancellationToken)
    ?? throw new DomainException("Storyboard was not found after creation.");
return StoryboardResponse.FromDomain(refreshedStoryboard);
```

**Or use AsNoTracking() for response:**
```csharp
var responseStoryboard = await _repository.GetByContentProjectIdAsyncNoTracking(contentProjectId, cancellationToken)
    ?? throw new DomainException("Storyboard was not found.");
return StoryboardResponse.FromDomain(responseStoryboard);
```

---

## Implementation Priority

| Priority | Issue | Impact | Effort |
|----------|-------|--------|--------|
| 🔴 **P0** | ClipPlanService scene deletion tracking | Can cause runtime exceptions | Medium |
| 🟡 **P1** | Repository always includes Scenes | Memory & performance | Low |
| 🟡 **P2** | StoryboardService response tracking | Subtle bugs & stale data | Low |

---

## Testing Recommendations

1. **Unit Tests:**
   - Test `ClipPlanService.GenerateAsync()` with concurrent calls on same storyboard
   - Test rapid scene additions to same storyboard
   - Test scene deletion with multiple DbContext instances

2. **Integration Tests:**
   - Simulate concurrent requests to `AddSceneAsync()` and `GenerateAsync()`
   - Verify `DbUpdateConcurrencyException` doesn't occur

3. **Performance Tests:**
   - Profile memory usage of loaded storyboards with 100+ scenes
   - Measure query execution time with/without AsNoTracking()

