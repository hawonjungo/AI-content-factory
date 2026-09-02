# AI Content Factory - Video Generation Test Guide

## ✅ Service Status
- ✓ **Frontend**: Running on http://localhost:5174
- ✓ **API Backend**: Running on http://localhost:8080
- ✓ **PostgreSQL Database**: Running and healthy on localhost:5432
- ✓ **Gemini API Key**: Configured in environment
- ✓ **FFmpeg**: Available on system PATH

---

## 📋 Video Generation Workflow

The complete pipeline has 7 phases:

### Phase 1: Create Content Project
1. Open http://localhost:5174 in your browser
2. Click **"Create New Project"** or use the project creation form
3. Fill in:
   - **Project Title**: e.g., "My First AI Video"
   - **Description**: Brief description of the video content
   - **Topic/Theme**: What the video is about
4. Click **Create**
5. Note the **Project ID** (you'll need this for API calls)

### Phase 2: Generate Script
1. In the project detail page, click **"1. Generate Script"**
2. The backend will:
   - Use Gemini 2.5-flash to generate a video script
   - Break it into scenes (typically 3-5 scenes)
   - Save script content and metadata to the database
3. This costs ~$0.01-0.05 (cheap)
4. Wait for the status to show "Script Generated"

### Phase 3: Run QA Score
1. Click **"2. Run QA Score"**
2. The system will:
   - Evaluate the script for quality, coherence, and engagement
   - Generate a score (0-10)
   - Show breakdown of scoring criteria
3. If score ≥ 6.0 (default threshold), proceed to next phase
4. If score < 6.0, you can regenerate the script or proceed anyway

### Phase 4: Generate Reference Images
1. Click **"3. Generate Reference Images"**
2. The system will:
   - Identify key characters/subjects from the script
   - Generate 2 consistent reference images using Gemini 2.5-flash-image
   - Use these for consistency in Veo video generation
3. This costs ~$0.05-0.10 per image
4. Wait for "Images Generated" status

### Phase 5: Set Clip Plan
1. Click **"4. Set Clip Plan"**
2. Configure:
   - **Total Video Duration**: 15, 30, 60 seconds (recommended: 30s for testing)
   - **Individual Clip Length**: 6-8 seconds (default: 8s)
   - Example: 30s total ÷ 8s clips = 4 video clips
3. Click **Apply**

### Phase 6: Generate Video Clips ⭐ (COSTS MONEY!)
1. Click **"5. Generate Clips"** 
2. **IMPORTANT**: This will call the Veo 3.1 model, costing ~$0.40 USD per second
   - Example: 4 clips × 8 seconds = $3.20 cost
   - Default budget: $50/month
3. The system will:
   - For each scene, generate 1 video clip using Veo 3.1
   - Use reference images from Phase 4 for consistency
   - Poll every 5-20 seconds (exponential backoff)
   - Timeout: 300 seconds per clip
4. Status updates in real-time
5. **Total time**: 5-20 minutes depending on clip count and Veo queue

### Phase 7: Render Final Video
1. Once all clips are ready, click **"6. Render Video"**
2. Optional: Toggle **"Burn in captions"** checkbox (default: ON)
3. The system will:
   - Generate voice narration for each scene (Gemini TTS)
   - Combine all video clips using FFmpeg
   - Burn captions (SRT file) into the video (optional)
   - Mix background music (if available)
4. **Cost**: Free (only uses FFmpeg locally)
5. **Output**: Final MP4 video at `/app/storage/content-projects/{projectId}/final.mp4`
6. View the final video in the player

---

## 💰 Cost Breakdown (Example: 30-second video with 4 clips)

| Phase | Service | Cost | Details |
|-------|---------|------|---------|
| 2 | Script Generation | $0.02 | Gemini 2.5-flash |
| 3 | QA Scoring | $0.01 | Script analysis only |
| 4 | Reference Images | $0.10 | 2 images × $0.05 each |
| 6 | Video Clips | **$3.20** | 32 seconds × $0.10/sec (Veo 3.1) |
| 7 | Voice Narration | $0.05 | ~50 characters × TTS |
| 7 | Rendering | FREE | FFmpeg (local) |
| **TOTAL** | | **~$3.38** | For 30-second video |

---

## 🚀 Quick Start: Complete Test in 15 Minutes

### Option A: Manual UI Testing
```
1. Open http://localhost:5174
2. Create project
3. Generate script (2 min)
4. Run QA (1 min)
5. Generate reference images (2 min)
6. Set 15-second video, 5-second clips (1 clip)
7. Generate clips (5-10 min, costs ~$0.40)
8. Render video (2 min)
9. Watch final video
```

### Option B: API Testing (with cURL/Postman)
```bash
# 1. Create Project
POST http://localhost:8080/api/content-projects
{
  "title": "Test Video",
  "description": "Testing video generation"
}

# 2. Generate Script
POST http://localhost:8080/api/content-projects/{projectId}/generate-assets

# 3. Get Project Status
GET http://localhost:8080/api/content-projects/{projectId}

# 4. Set Clip Plan
PUT http://localhost:8080/api/content-projects/{projectId}
{
  "totalDurationSeconds": 15,
  "clipLengthSeconds": 5
}

# 5. Render Video
POST http://localhost:8080/api/content-projects/{projectId}/render
{
  "burnCaptions": true
}

# 6. Get Rendering Job Status
GET http://localhost:8080/jobs (Hangfire dashboard)
```

---

## 🔧 Troubleshooting

| Error | Cause | Solution |
|-------|-------|----------|
| **"API is unreachable"** | Backend not running | Run `docker ps` - verify API container is up |
| **"Gemini API Key not set"** | Missing credentials | Check `docker exec aicontentfactory-api-1 env \| grep GEMINI` |
| **"Insufficient quota or billing disabled"** | No billing on API key | Enable billing: https://console.cloud.google.com/billing |
| **"Veo generation timed out"** | Veo 3.1 queue is backed up | Retry or increase `VideoTimeoutSeconds` in appsettings.json |
| **"ffmpeg exited with code X"** | FFmpeg not installed | Install: `choco install ffmpeg` (Windows) or `brew install ffmpeg` (Mac) |
| **"Budget limit exceeded"** | Monthly cost over $50 | Increase `Budget__MonthlyLimitUsd` or wait for next month |
| **Port 5173/5174 already in use** | Development server conflict | Kill existing Node process or use different port |

---

## 📊 Monitoring

### View Hangfire Dashboard
- URL: http://localhost:8080/jobs
- Shows all background jobs (asset generation, rendering)
- Real-time status, logs, and error messages

### View Database
```bash
docker exec -it aicontentfactory-postgres-1 psql -U postgres -d aicontentfactory
\dt                    # List all tables
SELECT * FROM "Assets";  # View generated assets
```

### View API Logs
```bash
docker logs -f aicontentfactory-api-1
```

### View Stored Videos
```bash
docker exec aicontentfactory-api-1 ls -la /app/storage/content-projects/
```

---

## 🎬 Expected Results

After completing all phases, you should have:

1. **Project Metadata**: Title, description, status
2. **Generated Script**: Scene-by-scene video script with narration
3. **Reference Images**: 2 character/subject images for visual consistency
4. **Video Clips**: Individual MP4 files per scene (8 seconds each)
5. **Voice Narration**: WAV audio tracks per scene
6. **Final Video**: `final.mp4` with:
   - All clips concatenated
   - Voice-over mixed as audio
   - Captions burned in (optional)
   - Background music (if configured)
   - Resolution: 1080×1920 (9:16 aspect ratio, vertical short-form)

---

## 📝 Next Steps

1. **Test with Real Content**: Create a project with your own topic
2. **Optimize Prompts**: Tweak script generation to match your style
3. **Customize Rendering**: Adjust caption style, music, resolution
4. **Integrate with Production**: Deploy to your infrastructure
5. **Monitor Costs**: Track spending via monthly reports

---

## 💡 Pro Tips

- **Re-render is Free**: Toggle captions OFF and re-render to save costs
- **Reference Images Matter**: Better images = more consistent Veo output
- **Shorter Clips = Faster**: 5-second clips render faster than 8-second
- **Batch Processing**: Generate multiple projects in parallel
- **Error Recovery**: Failed scenes can be regenerated individually

---

**Happy video generating! 🎥✨**
