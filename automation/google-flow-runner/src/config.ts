/**
 * Shared defaults for the CLI. Kept tiny and dependency-free on purpose so
 * both cli.ts and tests can import it.
 */

/**
 * Matches the `applicationUrl` of the "http" launch profile in
 * src/AiContentFactory.Api/Properties/launchSettings.json - i.e. what you get
 * from `dotnet run` against the API project directly.
 *
 * If you're running the API via the repo's docker-compose.yml instead, pass
 * `--base-url http://localhost:8080` (the port docker-compose.yml publishes),
 * since that is a different process with a different port than the
 * `dotnet run` dev profile this default targets.
 */
export const DEFAULT_BASE_URL = "http://localhost:5126";

export const DEFAULT_DOWNLOADS_DIR = "./downloads";

export const DEFAULT_AUTH_TIMEOUT_MS = 5 * 60 * 1000; // 5 minutes to let a human log in
export const DEFAULT_RENDER_TIMEOUT_MS = 6 * 60 * 1000; // 6 minutes per clip - unverified guess, tune once real render times are known

/**
 * Default Chrome DevTools Protocol endpoint this tool attaches to (the
 * standard `--remote-debugging-port=9222` port). Attaching to a real Chrome
 * window the user launched and logged into themselves is the DEFAULT mode
 * (see browserSession.ts) - Google's sign-in flow reliably blocks Playwright
 * launching its own browser ("Chrome for Testing"), flagging it as an
 * untrusted automation environment. Overridable via the GFR_CDP_ENDPOINT env
 * var / --cdp-endpoint CLI flag / cdpEndpoint in a POST /run body.
 */
export const DEFAULT_CDP_ENDPOINT = "http://localhost:9222";

/**
 * Port for the optional local HTTP "companion service" (src/server.ts) that
 * lets the web app trigger a run instead of the user typing a CLI command.
 * Overridable via the GFR_SERVE_PORT env var.
 */
export const DEFAULT_SERVE_PORT = 4545;

/**
 * The only browser origin the companion service's /run and /status endpoints
 * will accept requests from (CORS allowlist of exactly one origin). Matches
 * Vite's default dev server port for frontend/ai-content-factory-web - no
 * custom port is configured in that project's vite.config.ts. Overridable
 * via the GFR_UI_ORIGIN env var.
 */
export const DEFAULT_UI_ORIGIN = "http://localhost:5173";
