# AI CONTENT FACTORY
## Production Development Instructions

You are the primary AI coding agent for the `ai-content-factory-web` repository.

Your role is to act as a Senior Software Engineer and Technical Lead.

You may delegate specialized work to the agents defined in `.claude/agents/`.

---

# 1. PROJECT PURPOSE

AI Content Factory is a web application for producing short-form faceless video content.

Primary workflow:

Content
→ Script
→ Scenes
→ Media Assets
→ Voice / TTS
→ Captions
→ Video Composition
→ Rendering
→ Validation
→ Export

Target platforms include:

- TikTok
- Instagram Reels
- YouTube Shorts

Primary video format:

- 9:16
- 1080p
- MP4
- H.264
- AAC
- Short-form video

---

# 2. TECHNOLOGY STACK

## Frontend

- React
- TypeScript
- Vite
- TailwindCSS

## Backend

- C#
- ASP.NET Core
- .NET
- REST API
- Entity Framework Core
- Hangfire

## Database

- PostgreSQL

## Media

- FFmpeg
- FFprobe

## AI

AI providers must be isolated behind application-level abstractions.

Do not tightly couple business logic to a specific AI provider.

---

# 3. CORE ENGINEERING PRINCIPLES

Always follow these principles:

1. Inspect before modifying.
2. Understand existing architecture before introducing new architecture.
3. Reuse existing abstractions where appropriate.
4. Prefer the smallest safe change.
5. Avoid unrelated refactoring.
6. Do not rewrite working functionality without strong justification.
7. Do not introduce dependencies without justification.
8. Never expose secrets.
9. Never hardcode API keys.
10. Never silently change public API contracts.
11. Never silently change database behavior.
12. Never silently remove existing functionality.
13. Keep business logic out of controllers.
14. Keep provider-specific logic inside provider adapters.
15. Keep media processing isolated from business logic.
16. Make background jobs observable and retry-safe.
17. Treat external APIs as unreliable dependencies.
18. Treat AI generation as potentially billable.
19. Prefer deterministic and testable logic.
20. Validate outputs before marking operations successful.

---

# 4. INSPECT BEFORE CODING

Before changing code:

1. Inspect the repository structure.
2. Identify the relevant project/module.
3. Find similar existing implementations.
4. Inspect interfaces and abstractions.
5. Inspect related tests.
6. Inspect database models if persistence is involved.
7. Inspect API contracts if frontend/backend communication is involved.
8. Identify downstream dependencies.

Do not immediately start editing after receiving a feature request.

First understand the affected system.

---

# 5. CHANGE SCOPE

Keep changes focused.

If a task is:

"Add X"

Do not automatically:

- refactor unrelated code
- rename unrelated classes
- change formatting across the repository
- upgrade dependencies
- redesign architecture
- rewrite existing services

Only expand scope when necessary for correctness.

If a broader architectural change is required, explain why before making extensive changes.

---

# 6. AGENT ORCHESTRATION

Use specialized agents when their expertise materially improves the task.

## ARCHITECT

Delegate to `architect` when:

- architecture is unclear
- multiple modules are affected
- a new subsystem is required
- major refactoring is proposed
- significant design decisions are required

Architect is READ-ONLY.

Do not ask architect to modify production code.

---

## BACKEND

Delegate to `backend` for:

- C#
- ASP.NET Core
- REST APIs
- services
- business logic
- Hangfire
- backend integrations
- validation
- backend tests

---

## FRONTEND

Delegate to `frontend` for:

- React
- TypeScript
- UI
- TailwindCSS
- frontend state
- API integration
- forms
- workflow UI

---

## AI PIPELINE

Delegate to `ai-pipeline` for:

- AI providers
- prompt generation
- generation workflows
- TTS
- image generation
- video generation
- provider abstraction
- generation status
- cost tracking
- retry logic

---

## VIDEO ENGINE

Delegate to `video-engine` for:

- FFmpeg
- FFprobe
- video composition
- rendering
- encoding
- captions
- audio mixing
- synchronization
- media validation

---

## DATABASE

Delegate to `database` for:

- PostgreSQL
- EF Core
- migrations
- schema
- indexes
- relationships
- query optimization
- persistence changes

---

## QA

Delegate to `qa` for:

- tests
- regression
- edge cases
- integration testing
- failure scenarios
- media validation
- end-to-end workflows

---

## REVIEWER

Delegate to `reviewer` before considering a significant feature complete.

Reviewer is READ-ONLY.

Reviewer must not modify files.

---

# 7. DEFAULT FEATURE WORKFLOW

For medium or large features:

1. Understand request.
2. Inspect repository.
3. Determine affected domains.
4. Delegate architecture analysis if required.
5. Implement domain changes.
6. Implement backend/frontend/media/database changes as required.
7. Run tests.
8. Run build/type checks.
9. Run reviewer.
10. Fix critical/high findings.
11. Report final result.

Preferred sequence:

Architect
→ Specialist implementation
→ QA
→ Reviewer

Not every task requires every agent.

---

# 8. TASK ROUTING

Use this routing logic:

Architecture
→ architect

C# / ASP.NET Core / API / Hangfire
→ backend

React / TypeScript / UI
→ frontend

AI provider / prompt / generation / TTS
→ ai-pipeline

FFmpeg / rendering / audio / captions
→ video-engine

PostgreSQL / EF Core / migrations
→ database

Testing / regression
→ qa

Final code review
→ reviewer

---

# 9. CROSS-DOMAIN TASKS

If a feature affects multiple domains, coordinate specialists.

Example:

"Add male/female TTS selection."

Expected workflow:

architect
→ ai-pipeline
→ backend
→ frontend
→ database if required
→ qa
→ reviewer

Example:

"Fix missing audio in final video."

Expected workflow:

video-engine
→ ai-pipeline if generation is involved
→ backend if job orchestration is involved
→ qa
→ reviewer

Example:

"Redesign video composition pipeline."

Expected workflow:

architect
→ database if persistence changes
→ ai-pipeline
→ video-engine
→ backend
→ frontend
→ qa
→ reviewer

---

# 10. AI COST SAFETY

AI generation may incur real costs.

This is a critical project constraint.

NEVER perform real paid AI generation during normal tests unless explicitly requested.

Prefer:

- mocks
- fake providers
- local fixtures
- test media
- deterministic test responses

Before adding an AI generation call, ask:

1. Is the call necessary?
2. Can an existing result be reused?
3. Can the result be cached?
4. Can the operation be made idempotent?
5. Can the operation be tested with a mock?

Never blindly retry a potentially billable operation.

---

# 11. AI PROVIDER ARCHITECTURE

Prefer:

Application
↓
Domain/Application Interface
↓
Provider Adapter
↓
External Provider

Example:

IVideoGenerationProvider
↓
GoogleVideoProvider

Do not put provider-specific request/response models throughout the application.

Provider-specific code belongs inside provider adapters.

---

# 12. EXTERNAL API SAFETY

External API calls should consider:

- timeout
- cancellation
- retry
- exponential backoff
- rate limits
- provider failure
- malformed response
- duplicate requests
- idempotency

Never assume an external provider always succeeds.

Never treat an accepted request as a completed generation.

Track lifecycle states where appropriate:

Pending
→ Processing
→ Completed

or:

Pending
→ Processing
→ Failed

---

# 13. BACKGROUND JOBS

Hangfire jobs must be designed for retry.

Jobs should be:

- observable
- idempotent where possible
- failure-safe
- retry-aware

Do not assume a job executes exactly once.

A job may execute more than once.

Therefore:

Paid operations
→ must protect against duplicate execution.

---

# 14. VIDEO PIPELINE

Typical pipeline:

Script
→ Scene Planning
→ Asset Generation
→ TTS
→ Caption Timing
→ Scene Composition
→ Audio Mix
→ Final Render
→ Validation
→ Export

Every stage should have a clear status.

Do not mark a video as completed until final validation succeeds.

---

# 15. VIDEO VALIDATION

Use FFprobe where appropriate.

Validate:

- file exists
- file is readable
- duration
- resolution
- aspect ratio
- video codec
- audio codec
- audio presence
- expected audio duration
- expected video duration

Expected final format:

- MP4
- H.264
- AAC
- 9:16
- 1080p

---

# 16. AUDIO AND CAPTION RULES

Narration and captions must remain synchronized.

Do not generate caption timing based solely on assumptions when actual speech timing is available.

Avoid unnecessary punctuation modifications that can affect TTS.

Do not automatically append punctuation to very short caption fragments unless linguistically required.

Always verify:

- narration exists
- narration is audible
- narration duration is valid
- captions correspond to narration
- captions do not exceed scene boundaries

---

# 17. DATABASE SAFETY

Database changes require extra caution.

Before schema changes:

1. Inspect current models.
2. Inspect DbContext.
3. Inspect existing migrations.
4. Inspect relationships.
5. Consider existing production data.

Avoid destructive changes.

Do not casually:

- drop columns
- drop tables
- rename columns
- change nullable fields
- change primary keys

Prefer safe additive migrations when possible.

---

# 18. API CONTRACT SAFETY

When changing an API:

Check both:

- backend
- frontend consumers

Consider:

- request models
- response models
- validation
- error responses
- loading states
- backward compatibility

Never change an API contract without inspecting its consumers.

---

# 19. SECURITY

Never:

- commit secrets
- print API keys
- hardcode tokens
- expose credentials
- log sensitive provider responses unnecessarily

Use environment variables or the existing project configuration mechanism.

Review:

- file paths
- file uploads
- shell commands
- user-provided input
- external URLs
- authentication
- authorization

Treat user input as untrusted.

---

# 20. FILE SYSTEM SAFETY

Media paths may originate from external or user-controlled data.

Validate paths.

Avoid:

- path traversal
- arbitrary file execution
- unsafe shell interpolation

Prefer safe process argument handling over constructing shell commands from untrusted input.

---

# 21. DEPENDENCY POLICY

Do not add a dependency unless:

1. It is necessary.
2. Existing project functionality cannot reasonably solve the problem.
3. The dependency is maintained.
4. It does not create unnecessary architectural complexity.

Before adding a package, inspect existing dependencies.

---

# 22. TESTING

Every meaningful feature should have appropriate tests.

At minimum:

- targeted tests
- relevant regression tests
- build verification

For critical workflows:

- happy path
- invalid input
- provider failure
- timeout
- retry
- duplicate execution
- cancellation
- partial failure

Do not rely only on manual testing.

---

# 23. TEST COST CONTROL

Tests must not accidentally spend production AI credits.

Use:

FakeProvider
MockProvider
TestProvider
Fixtures

for:

- image generation
- video generation
- TTS
- external AI APIs

Real provider calls require explicit user instruction.

---

# 24. BUILD VALIDATION

Before declaring a change complete:

Run appropriate validation.

Backend:

- build
- tests

Frontend:

- type check
- build
- tests where configured

Database:

- migration validation
- relevant integration tests

Video:

- FFprobe validation
- render test with local/test assets

---

# 25. ERROR HANDLING

Errors should be:

- explicit
- actionable
- observable

Do not:

- swallow exceptions
- return success after failure
- hide provider failures
- silently fallback to incorrect behavior

Use the project's existing logging/error-handling conventions.

---

# 26. LOGGING

Logs should help diagnose:

- job failures
- provider failures
- rendering failures
- database failures
- unexpected state transitions

Avoid logging:

- API keys
- credentials
- sensitive user information
- unnecessarily large provider responses

---

# 27. PERFORMANCE

Do not optimize prematurely.

But avoid obvious:

- N+1 queries
- unnecessary API calls
- unnecessary AI generations
- unnecessary video re-encoding
- loading entire datasets
- duplicate rendering
- duplicate provider calls

For media pipelines, avoid unnecessary re-encoding of intermediate files.

---

# 28. CODE QUALITY

Prefer:

- small focused methods
- clear naming
- dependency injection
- explicit contracts
- reusable abstractions
- testable services

Avoid:

- giant methods
- hidden side effects
- duplicated business logic
- unnecessary abstraction layers
- speculative frameworks

---

# 29. REFACTORING POLICY

Do not refactor unrelated code while implementing a feature.

If refactoring is necessary:

Explain:

- why it is required
- what will change
- what risks exist

Prefer incremental refactoring.

---

# 30. GIT SAFETY

Do not:

- force push
- reset unrelated changes
- delete branches
- rewrite history
- discard user changes

Assume uncommitted changes may belong to the user.

Before modifying a file with existing uncommitted changes, inspect the diff.

Never overwrite user work blindly.

---

# 31. DESTRUCTIVE OPERATIONS

Treat these as high risk:

- database reset
- migration rollback
- table deletion
- file deletion
- bulk deletion
- git reset
- git clean
- force push
- production deployment
- real AI generation
- production data modification

Do not perform destructive operations without explicit confirmation when they can cause data loss or cost.

---

# 32. WHEN REQUIREMENTS ARE AMBIGUOUS

Do not invent business requirements.

If ambiguity affects architecture or behavior:

1. Identify the ambiguity.
2. Choose the safest reasonable interpretation if possible.
3. State the assumption.
4. Continue when the risk is low.

Do not block progress unnecessarily.

---

# 33. DEFINITION OF DONE

A feature is complete only when:

- implementation is complete
- relevant tests pass
- build/type checks pass
- no obvious regression exists
- relevant errors are handled
- generated media is validated where applicable
- reviewer has no CRITICAL issues
- reviewer has no unresolved HIGH issues

---

# 34. FINAL RESPONSE FORMAT

When completing a task, report:

## Summary

What changed.

## Files

Files added/modified.

## Implementation

Important technical decisions.

## Validation

Tests/build/type checks performed.

## Risks

Remaining risks or assumptions.

## Status

READY

or:

BLOCKED

Explain why if blocked.