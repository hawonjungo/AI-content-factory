# AI Content Factory - Video Generation API Test Script
# Usage: .\test-video-generation.ps1

param(
    [string]$ApiUrl = "http://localhost:8080/api",
    [string]$ProjectTitle = "Test Video $(Get-Date -Format 'yyyyMMdd-HHmmss')",
    [int]$TotalDurationSeconds = 15,
    [int]$ClipLengthSeconds = 5
)

Write-Host "🎬 AI Content Factory - Video Generation Test" -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Green
Write-Host ""

function Invoke-ApiCall {
    param(
        [string]$Method,
        [string]$Endpoint,
        [object]$Body = $null
    )
    
    $FullUrl = "$ApiUrl$Endpoint"
    Write-Host "→ $Method $FullUrl" -ForegroundColor Cyan
    
    $params = @{
        Uri     = $FullUrl
        Method  = $Method
        Headers = @{ "Content-Type" = "application/json" }
        UseBasicParsing = $true
    }
    
    if ($Body) {
        $params.Body = $Body | ConvertTo-Json
        Write-Host "  Payload: $($params.Body)" -ForegroundColor Gray
    }
    
    try {
        $response = Invoke-WebRequest @params
        Write-Host "  ✓ Response: $($response.StatusCode)" -ForegroundColor Green
        return $response.Content | ConvertFrom-Json
    }
    catch {
        Write-Host "  ✗ Error: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "  Response: $($_.ErrorDetails.Message)" -ForegroundColor Red
        return $null
    }
}

# Step 1: Create Project
Write-Host "Step 1: Creating Content Project..." -ForegroundColor Yellow
$createProjectPayload = @{
    title       = $ProjectTitle
    description = "Automated test video generated at $(Get-Date)"
} | ConvertTo-Json

$projectResponse = Invoke-ApiCall -Method "POST" -Endpoint "/content-projects" -Body $createProjectPayload
if (-not $projectResponse) { exit 1 }

$projectId = $projectResponse.id
Write-Host "  Project ID: $projectId" -ForegroundColor Cyan
Write-Host ""

# Step 2: Wait and check project status
Write-Host "Step 2: Checking Project Status..." -ForegroundColor Yellow
Start-Sleep -Seconds 2

$statusResponse = Invoke-ApiCall -Method "GET" -Endpoint "/content-projects/$projectId"
if ($statusResponse) {
    Write-Host "  Status: $($statusResponse.status)" -ForegroundColor Cyan
    Write-Host "  Title: $($statusResponse.title)" -ForegroundColor Cyan
}
Write-Host ""

# Step 3: Generate Script & Assets
Write-Host "Step 3: Generating Script & Assets (this may take a few minutes)..." -ForegroundColor Yellow
$generatePayload = @{} | ConvertTo-Json

$jobResponse = Invoke-ApiCall -Method "POST" -Endpoint "/content-projects/$projectId/generate-assets" -Body $generatePayload
if ($jobResponse) {
    Write-Host "  Job Enqueued: $($jobResponse.jobId)" -ForegroundColor Cyan
}

# Poll for completion
$maxWait = 600  # 10 minutes
$waitInterval = 5
$elapsed = 0

Write-Host "  Polling for completion..." -ForegroundColor Gray
while ($elapsed -lt $maxWait) {
    Start-Sleep -Seconds $waitInterval
    $elapsed += $waitInterval
    
    $statusResponse = Invoke-ApiCall -Method "GET" -Endpoint "/content-projects/$projectId"
    if ($statusResponse) {
        $status = $statusResponse.status
        Write-Host "  Status ($($elapsed)s): $status" -ForegroundColor Cyan
        
        # Check if all phases are ready
        if ($status -eq "AwaitingClipSettings" -or $status -eq "ReadyForRendering") {
            Write-Host "  ✓ Assets ready!" -ForegroundColor Green
            break
        }
    }
}

if ($elapsed -ge $maxWait) {
    Write-Host "  ⚠ Timeout waiting for assets" -ForegroundColor Yellow
}
Write-Host ""

# Step 4: Set Clip Plan
Write-Host "Step 4: Setting Clip Plan..." -ForegroundColor Yellow
$clipPlanPayload = @{
    totalDurationSeconds = $TotalDurationSeconds
    clipLengthSeconds    = $ClipLengthSeconds
} | ConvertTo-Json

$clipPlanResponse = Invoke-ApiCall -Method "PUT" -Endpoint "/content-projects/$projectId" -Body $clipPlanPayload
if ($clipPlanResponse) {
    Write-Host "  Duration: $($clipPlanResponse.totalDurationSeconds)s" -ForegroundColor Cyan
    Write-Host "  Clip Length: $($clipPlanResponse.clipLengthSeconds)s" -ForegroundColor Cyan
}
Write-Host ""

# Step 5: Generate Clips
Write-Host "Step 5: Generating Video Clips (⚠️  THIS COSTS MONEY - $0.40 per second)..." -ForegroundColor Yellow
Write-Host "  Estimated cost: \$$($TotalDurationSeconds * 0.40)" -ForegroundColor Yellow

$confirmed = Read-Host "  Continue? (y/N)"
if ($confirmed -ne "y") {
    Write-Host "  ✓ Skipped clip generation" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "📊 Summary:" -ForegroundColor Green
    Write-Host "  Project ID: $projectId" -ForegroundColor Cyan
    Write-Host "  Status: Ready for next phase" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "To continue testing, run these API calls:" -ForegroundColor Yellow
    Write-Host "  PUT /content-projects/$projectId  (if clip plan not set)" -ForegroundColor Gray
    Write-Host "  POST /content-projects/$projectId/generate-assets  (to generate clips)" -ForegroundColor Gray
    Write-Host "  POST /content-projects/$projectId/render  (to render video)" -ForegroundColor Gray
    exit 0
}

$generateClipsPayload = @{} | ConvertTo-Json
$clipsJobResponse = Invoke-ApiCall -Method "POST" -Endpoint "/content-projects/$projectId/generate-assets" -Body $generateClipsPayload
if ($clipsJobResponse) {
    Write-Host "  Job Enqueued" -ForegroundColor Cyan
}

# Poll for clips completion
$maxWait = 1800  # 30 minutes for video generation
$waitInterval = 10
$elapsed = 0

Write-Host "  Polling for clips completion (this can take 10-30 minutes)..." -ForegroundColor Gray
while ($elapsed -lt $maxWait) {
    Start-Sleep -Seconds $waitInterval
    $elapsed += $waitInterval
    
    $statusResponse = Invoke-ApiCall -Method "GET" -Endpoint "/content-projects/$projectId"
    if ($statusResponse) {
        $status = $statusResponse.status
        $readyClips = @($statusResponse.scenes | Where-Object { $_.assets -and $_.assets.type -eq "Video" }).Count
        $totalScenes = @($statusResponse.scenes).Count
        Write-Host "  Progress ($($elapsed)s): $readyClips/$totalScenes clips ready - $status" -ForegroundColor Cyan
        
        if ($status -eq "ReadyForRendering") {
            Write-Host "  ✓ All clips ready!" -ForegroundColor Green
            break
        }
    }
}

if ($elapsed -ge $maxWait) {
    Write-Host "  ⚠ Timeout waiting for clips" -ForegroundColor Yellow
}
Write-Host ""

# Step 6: Render Video
Write-Host "Step 6: Rendering Final Video..." -ForegroundColor Yellow
$renderPayload = @{
    burnCaptions = $true
} | ConvertTo-Json

$renderResponse = Invoke-ApiCall -Method "POST" -Endpoint "/content-projects/$projectId/render" -Body $renderPayload
if ($renderResponse) {
    Write-Host "  Job Enqueued" -ForegroundColor Cyan
}

# Poll for render completion
$maxWait = 300  # 5 minutes for rendering
$waitInterval = 3
$elapsed = 0

Write-Host "  Polling for render completion..." -ForegroundColor Gray
while ($elapsed -lt $maxWait) {
    Start-Sleep -Seconds $waitInterval
    $elapsed += $waitInterval
    
    $statusResponse = Invoke-ApiCall -Method "GET" -Endpoint "/content-projects/$projectId"
    if ($statusResponse) {
        $status = $statusResponse.status
        Write-Host "  Status ($($elapsed)s): $status" -ForegroundColor Cyan
        
        if ($status -eq "AwaitingApproval") {
            Write-Host "  ✓ Video rendered!" -ForegroundColor Green
            break
        }
    }
}

if ($elapsed -ge $maxWait) {
    Write-Host "  ⚠ Timeout waiting for render" -ForegroundColor Yellow
}
Write-Host ""

# Final Summary
Write-Host "📊 Test Complete!" -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Green
Write-Host "Project ID: $projectId" -ForegroundColor Cyan
Write-Host ""
Write-Host "View your video:" -ForegroundColor Yellow
Write-Host "  Frontend: http://localhost:5174/project/$projectId" -ForegroundColor Cyan
Write-Host "  API: GET http://localhost:8080/api/content-projects/$projectId" -ForegroundColor Cyan
Write-Host ""
Write-Host "View job logs:" -ForegroundColor Yellow
Write-Host "  Hangfire: http://localhost:8080/jobs" -ForegroundColor Cyan
Write-Host ""
Write-Host "Database:" -ForegroundColor Yellow
Write-Host "  docker exec -it aicontentfactory-postgres-1 psql -U postgres -d aicontentfactory" -ForegroundColor Cyan
