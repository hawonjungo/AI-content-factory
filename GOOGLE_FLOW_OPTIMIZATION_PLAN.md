# 📋 Kế Hoạch Tối Ưu Google Flow - 50 Credits/Ngày

## 🎯 Chiến Lược Tổng Quan

```
HIỆN TẠI (Veo Text-to-Video)          →    MỚI (Imagen + Veo Image-to-Video)
─────────────────────────────────────      ─────────────────────────────────
Text Script (dài)                          Hook-Heavy Script (20s: 5+10+5)
    ↓                                            ↓
Reference Images (tính phí)                Free Imagen Images (0 credits)
    ↓                                            ↓
Veo: Text→Video (chậm, đắt)           Veo 3.1 Lite: Image→Video (nhanh, rẻ)
    ↓                                            ↓
$0.40/giây × thời gian                  10 credits × 5 lần = 50 credits tối đa
    ↓                                            ↓
FFmpeg concat + TTS + captions           FFmpeg concat + TTS + captions
    ↓                                            ↓
Video dài (15-30s)                       Video 20s hoàn hảo mỗi ngày
```

---

## 🏗️ Thay Đổi Kiến Trúc Chi Tiết

### 1️⃣ **Script Generation Agent (Hook-Heavy)**
**Vị trí**: `src/AiContentFactory.Application/Agents/PromptAgent.cs` → Tạo `HookScriptAgent.cs`

**Thay đổi**:
```csharp
// HIỆN TẠI
ScriptPrompt = "Generate a long video script with multiple scenes..."

// MỚI - Hook-Heavy (Bắt buộc 20 giây)
HookScriptPrompt = """
You are a viral short-form video script writer.

Generate EXACTLY this structure (NO VARIATIONS):
- 0-5s (HOOK): One shocking fact or psychological hook
- 5-15s (CLIMAX): Peak story or answer (NEEDS STRONG MOTION)
- 15-20s (CTA): Call-to-action or loop

Format: Return 3 scenes with timings and narration text.
Make each scene HIGHLY COMPELLING for 20-second format.
Scene descriptions optimized for static image → video generation.
""";
```

**Kết quả**: 3 scene cố định, mỗi scene 5-10 giây, narration ngắn gọn

---

### 2️⃣ **Image Generation Provider (Imagen - Free)**
**Vị trí**: Tạo mới `src/AiContentFactory.Infrastructure/Providers/Google/ImagenImageProvider.cs`

**Spec**:
```csharp
public class ImagenImageProvider : IImageGenerationProvider
{
    // API: Google Imagen model (free tier)
    // Model: "imagen-3.0-generate-001" (latest Imagen)
    // Credit cost: 0 (completely free)
    // Limit: Unlimited generations per day
    
    public async Task<ImageGenerationResult> GenerateAsync(
        ImageGenerationRequest request, 
        CancellationToken cancellationToken)
    {
        // POST /models/imagen-3.0-generate-001:predict
        // Response: images[0].bytesBase64
        // Store locally for Image-to-Video step
    }
}
```

**Prompt Optimization** (quan trọng nhất):
```csharp
ImagePrompt = """
Create a stunning, PHOTOREALISTIC image for a 5-second video scene:
{sceneDescription}

CRITICAL:
- Aspect ratio: 9:16 (vertical)
- Style: Cinematic, high-quality photography
- Motion-ready: Clear focal point for subtle camera movement
- Characters: If any, make them expressive and visible
- Colors: Vibrant, shareable social media quality

This image will be animated into a 5-second video clip.
""";
```

---

### 3️⃣ **Image-to-Video Provider (Veo 3.1 Lite)**
**Vị trí**: Tạo mới `src/AiContentFactory.Infrastructure/Providers/Google/ImageToVideoVeoProvider.cs`

**Khác biệt so với Text-to-Video**:
```csharp
public class ImageToVideoVeoProvider : IVideoGenerationProvider
{
    // Model: "veo-3.1-generate-lite" (not "veo-3.1-fast-generate-preview")
    // Input: Image bytes (from Imagen) + Motion prompt
    // Output: 5-second MP4 clip
    // Credit cost: Exactly 10 credits per clip
    // Max clips/day: 5 (with 50 credits)
    
    private const int CREDITS_PER_CLIP = 10;
    private const int CLIP_DURATION_SECONDS = 5;
    
    public async Task<VideoGenerationResult> GenerateAsync(
        VideoGenerationRequest request,  // request.ReferenceImages[0] = Imagen image
        CancellationToken cancellationToken)
    {
        // POST /models/veo-3.1-generate-lite:predictLongRunning
        // Payload: image (inline base64), motion_level, prompt
        // Motion levels: 1-3 (1=subtle, 2=normal, 3=intense)
        //   - Scene 1 (Hook): motion_level=1 (subtle zoom, smoke effect)
        //   - Scene 2 (Climax): motion_level=3 (intense action)
        //   - Scene 3 (CTA): motion_level=1 (slow reveal)
    }
}
```

---

### 4️⃣ **Daily Quota Manager (50 Credits)**
**Vị trí**: Tạo mới `src/AiContentFactory.Application/Costs/GoogleFlowQuotaManager.cs`

```csharp
public class GoogleFlowQuotaManager
{
    public const int DAILY_CREDITS = 50;
    public const int CREDITS_PER_VIDEO = 10;
    public const int MAX_VIDEOS_PER_DAY = 5; // 50 / 10
    
    public async Task<QuotaCheckResult> CheckDailyQuotaAsync(
        Guid projectId, 
        CancellationToken cancellationToken)
    {
        // Query database: "Today's video generation count for this project"
        // If < 5 → ALLOW
        // If >= 5 → DENY (wait until tomorrow at UTC 00:00)
        // Reset logic: Based on DateTime.UtcNow, not per-user time
    }
    
    public async Task RecordVideoGenerationAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        // Insert record with timestamp TODAY
        // Next day at UTC 00:00, old records don't count
    }
}
```

---

### 5️⃣ **Updated Cost/Billing System**
**Vị trí**: Modify `src/AiContentFactory.Application/Costs/AiUsageTrackerService.cs`

**Thay đổi**:
```csharp
// HIỆN TẠI
private const decimal EstimatedVideoCostPerSecondUsd = 0.40m; // $0.40/sec Veo

// MỚI - Free Flow Model
private const decimal EstimatedImageCostUsd = 0m; // Imagen: FREE
private const decimal EstimatedVideoCreditsPerClipUsd = 0m; // Google credits, not USD

// Tracking: Switch from USD to CREDITS
public async Task RecordVideoGenerationAsync(
    Guid projectId,
    VideoSource source) // enum: TextToVideo (paid) vs ImageToVideo (free quota)
{
    if (source == VideoSource.ImageToVideo)
    {
        // Record as "10 credits used" not "$4 used"
        // Check daily quota, not monthly budget
    }
    else if (source == VideoSource.TextToVideo)
    {
        // Keep existing USD-based tracking for backward compat
    }
}
```

---

### 6️⃣ **Asset Generation Service (New Workflow)**
**Vị trí**: Modify `src/AiContentFactory.Application/Generation/AssetGenerationService.cs`

**Flow hiện tại**:
```
Text Script → Reference Images → Veo (Text→Video) → TTS → FFmpeg
  (1 agent)      (1 call)        (N calls)      (N)   (1)
  Cost: $0.02    Cost: $0.05     Cost: $N×0.40  Free  Free
```

**Flow mới**:
```
Hook Script → Imagen Images → Veo Lite (Image→Video) → TTS → FFmpeg
  (1 agent)     (3-4 calls)      (≤5 calls)          (3)   (1)
  Cost: $0.01   Cost: FREE       Cost: 50 credits    Free  Free
```

**Pseudo-code**:
```csharp
public async Task RunAsync(Guid contentProjectId, CancellationToken ct)
{
    // 1. Generate Hook-Heavy Script (3 scenes, 20 seconds total)
    var script = await _hookScriptAgent.GenerateAsync(topic, ct);
    // Result: [Scene1(5s), Scene2(10s), Scene3(5s)]
    
    // 2. Generate FREE images via Imagen (3-4 images)
    var images = new List<ImageData>();
    foreach (var scene in script.Scenes)
    {
        var image = await _imagenProvider.GenerateAsync(
            prompt: scene.Description, 
            ct);
        images.Add(image);
    }
    // Cost: 0 credits ✓
    
    // 3. Check daily quota (max 5 videos = 50 credits)
    var quota = await _quotaManager.CheckDailyQuotaAsync(contentProjectId, ct);
    if (!quota.CanGenerate)
    {
        throw new QuotaExceededException("Daily 50-credit limit reached");
    }
    
    // 4. Generate videos via Image-to-Video Veo Lite (5 clips max)
    var videos = new List<VideoData>();
    foreach (var (scene, image) in script.Scenes.Zip(images))
    {
        var motionLevel = scene.Type switch
        {
            SceneType.Hook => 1,      // Subtle motion
            SceneType.Climax => 3,    // Intense motion
            SceneType.CTA => 1        // Slow reveal
        };
        
        var video = await _imageToVideoProvider.GenerateAsync(
            new VideoGenerationRequest(
                Image: image.Bytes,
                MotionLevel: motionLevel,
                Prompt: $"Animate with {motionLevel} level motion: {scene.VisualDirection}"),
            ct);
        
        videos.Add(video);
        await _quotaManager.RecordVideoGenerationAsync(contentProjectId, ct);
    }
    // Cost: 10 credits × 5 videos = 50 credits ✓
    
    // 5. Generate TTS narration for each scene
    var audioClips = new List<AudioData>();
    foreach (var scene in script.Scenes)
    {
        var audio = await _ttsProvider.GenerateAsync(scene.Narration, ct);
        audioClips.Add(audio);
    }
    // Cost: ~$0.01 ✓
    
    // 6. Concatenate via FFmpeg (same as before)
    var finalVideo = await _ffmpegRenderer.RenderAsync(
        videos: videos,
        audioTracks: audioClips,
        captions: script.GenerateSubtitles(),
        ct);
    // Cost: FREE ✓
}
```

---

## 📊 So Sánh Chi Phí

| Thành phần | HIỆN TẠI | MỚI | Tiết kiệm |
|-----------|---------|-----|----------|
| **Script** | $0.02 | $0.01 | 50% ↓ |
| **Reference Images** | $0.10 | FREE | 100% ↓ |
| **Video (20s ≈ 4 clips × 5s)** | $1.60 | 50 credits (FREE) | 100% ↓ |
| **TTS** | $0.01 | $0.01 | - |
| **Rendering** | FREE | FREE | - |
| **TỔNG CỘNG** | **$1.73/video** | **$0.02/video** | **98.8% ↓** |
| **MỖI NGÀY (50 videos)** | $86.50 | $1.00 | **98.8% ↓** |

---

## ⚙️ Implementation Priority

### Phase 1: Thiết lập (Tuần 1)
- [ ] Tạo `HookScriptAgent.cs` với prompt 20s
- [ ] Tạo `ImagenImageProvider.cs` (free image generation)
- [ ] Tạo `GoogleFlowQuotaManager.cs` (quota tracking)

### Phase 2: Video Generation (Tuần 2)
- [ ] Tạo `ImageToVideoVeoProvider.cs` (Image→Video model)
- [ ] Update `AssetGenerationService.cs` workflow
- [ ] Test 5 videos = 50 credits

### Phase 3: Optimization (Tuần 3)
- [ ] Motion level tuning per scene type
- [ ] Re-roll logic (dự phòng 10 credits)
- [ ] Cost tracking migration (USD → Credits)

### Phase 4: Production (Tuần 4)
- [ ] Frontend UI update (show "5 videos/day" instead of "$50 budget")
- [ ] Documentation update
- [ ] Deploy & monitor

---

## 🔧 Configuration (appsettings.json)

```json
{
  "Llm": {
    "VideoGeneration": {
      "Provider": "GoogleFlowImageToVideo",  // Switch: "Veo" | "GoogleFlowImageToVideo"
      "DailyQuotaModel": "50_credits",        // Quota type: "monthly_usd" | "daily_credits"
      "Veo": {
        "TextToVideoModel": "veo-3.1-fast-generate-preview",
        "ImageToVideoModel": "veo-3.1-generate-lite",
        "CostPerSecondUsd": 0.40
      },
      "GoogleFlow": {
        "DailyCredits": 50,
        "CreditsPerImageToVideo": 10,
        "ImageProvider": "imagen-3.0-generate-001",
        "ImageCost": 0  // Free
      }
    }
  }
}
```

---

## ✅ Expected Output (Mỗi Ngày)

```
Ngày 1:
├─ Video 1 (20s): Hook (5s) + Climax (10s) + CTA (5s)
├─ Video 2 (20s): Story variation A
├─ Video 3 (20s): Story variation B
├─ Video 4 (20s): Story variation C
└─ Video 5 (20s): Story variation D (or re-roll của Video 1-4)

Total: 5 × 20s = 100 giây video
Cost: 50 credits (FREE, Google credits)
Time: ~30 phút (parallel generation)

Ngày 2: Reset quota, lặp lại
```

---

## 🎓 Next Steps

1. **Xác nhận API endpoint**: Bạn có biết endpoint chính xác của:
   - Imagen 3.0 (image generation)?
   - Veo 3.1 Lite (image-to-video)?
   - Quota API để check remaining credits?

2. **Test credentials**: Google Cloud project của bạn đã enable Google Flow API chưa?

3. **Prompt engineering**: Bạn muốn tôi bắt đầu với hook-heavy script agent (quan trọng nhất)?
