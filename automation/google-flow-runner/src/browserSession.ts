/**
 * Owns the actual Playwright browser lifecycle. Two modes:
 *
 * 1. CDP-ATTACH (default, recommended): connects to a real Chrome window the
 *    human already launched themselves (with --remote-debugging-port) and
 *    logged into Google in normally. Playwright never launches or controls
 *    the browser's *startup* - it only attaches afterwards. This is the
 *    documented fix for a real, confirmed problem: Google's sign-in flow
 *    reliably blocks a Playwright-launched browser ("Chrome for Testing" -
 *    Playwright's bundled Chromium build) as an untrusted automation
 *    environment, even before any credentials are entered. Since the human
 *    launches and authenticates in a completely ordinary Chrome window here,
 *    Google never observes an automated launch.
 *
 * 2. LAUNCH-OWN-BROWSER (legacy/fallback): Playwright launches its own
 *    Chromium in a persistent on-disk profile. Known to often be blocked at
 *    the Google sign-in step for the reason above - kept only as an explicit
 *    opt-out for anyone who wants it anyway (e.g. a Flow session that's
 *    already authenticated some other way, or testing).
 *
 * Safety rule (non-negotiable), applies to BOTH modes: this module NEVER
 * attempts to fill in credentials, click through a Google login form, or
 * defeat a CAPTCHA/anti-bot challenge. If the page isn't authenticated, it
 * waits for a human to log in themselves in the real, visible window. If
 * Flow itself looks broken/unreachable, or the human never logs in within
 * the timeout, this surfaces AUTH_REQUIRED / FLOW_UNAVAILABLE and stops
 * cleanly - it does not retry indefinitely or fall back to anything unsafe.
 *
 * UNVERIFIED against the real site (see googleFlowPage.ts / flowSelectors.ts
 * for the honesty note that applies here too) - the CDP-attach mode change
 * has not been exercised against a live Google Flow session either.
 */
import { chromium, type Browser, type BrowserContext } from "playwright";
import { GoogleFlowPage } from "./googleFlowPage";
import { flowSelectors } from "./flowSelectors";

export interface BrowserSessionOptions {
  /**
   * Chrome DevTools Protocol endpoint of a Chrome window the user already
   * launched and logged into themselves (e.g. "http://localhost:9222", from
   * launching Chrome with --remote-debugging-port=9222). When set (the
   * default), this is used and `userDataDir` is ignored. See the README for
   * the exact command to launch Chrome this way - it needs a NON-default
   * --user-data-dir alongside --remote-debugging-port, since recent Chrome
   * versions refuse remote debugging on your regular day-to-day profile.
   */
  cdpEndpoint?: string;
  /**
   * Only used when `cdpEndpoint` is not set - launches Playwright's own
   * bundled Chromium ("Chrome for Testing") in this persistent profile
   * directory. Prefer `cdpEndpoint`; this mode is frequently blocked by
   * Google's sign-in bot detection (see module doc above).
   */
  userDataDir?: string;
  /** Only applies to the launch-own-browser fallback. Defaults to false (visible window) - a human needs to actually see the page to log in. */
  headless?: boolean;
}

export interface FlowSession {
  context: BrowserContext;
  flowPage: GoogleFlowPage;
  close(): Promise<void>;
}

/**
 * Thrown when `cdpEndpoint` is set but nothing answers at that address -
 * almost always means Chrome wasn't launched with --remote-debugging-port
 * (or was launched on a different port). Distinct from AUTH_REQUIRED /
 * FLOW_UNAVAILABLE (those assume the browser itself is reachable).
 */
export class CdpConnectionError extends Error {
  constructor(
    public readonly cdpEndpoint: string,
    public readonly cause: unknown,
  ) {
    super(
      `Could not connect to Chrome's remote debugging port at ${cdpEndpoint}. ` +
        `Launch a real Chrome window with --remote-debugging-port and a dedicated --user-data-dir first ` +
        `(see automation/google-flow-runner/README.md for the exact command), log into Google in it, then retry. ` +
        `This is NOT a credits/budget issue - the tool never got as far as talking to Flow.`,
    );
    this.name = "CdpConnectionError";
  }
}

export async function openFlowSession(options: BrowserSessionOptions): Promise<FlowSession> {
  let context: BrowserContext;
  let close: () => Promise<void>;

  if (options.cdpEndpoint) {
    let browser: Browser;
    try {
      browser = await chromium.connectOverCDP(options.cdpEndpoint);
    } catch (cause) {
      throw new CdpConnectionError(options.cdpEndpoint, cause);
    }
    // Reuse whatever the human already has open (their real, already
    // logged-in profile) rather than creating a fresh context - a fresh
    // context on a CDP-attached browser would start logged out again.
    context = browser.contexts()[0] ?? (await browser.newContext());
    // browser.close() on a CDP-attached Browser disconnects Playwright's
    // session; it does not terminate the real Chrome process the human
    // started (per Playwright's connectOverCDP semantics) - their window
    // stays open exactly as they left it.
    close = () => browser.close();
  } else {
    if (!options.userDataDir) {
      throw new Error("openFlowSession: either cdpEndpoint or userDataDir must be provided.");
    }
    context = await chromium.launchPersistentContext(options.userDataDir, {
      headless: options.headless ?? false,
    });
    close = () => context.close();
  }

  const page = context.pages()[0] ?? (await context.newPage());
  const flowPage = new GoogleFlowPage(page, flowSelectors);
  await flowPage.goto();

  return { context, flowPage, close };
}

export interface WaitForHumanLoginOptions {
  timeoutMs: number;
  pollIntervalMs: number;
  /** Called once, the first time we detect we need to wait. */
  onWaiting?: () => void;
}

/**
 * Polls `isAuthenticated()` until it returns true or the timeout elapses.
 * Never types credentials, never clicks a login button, never attempts to
 * bypass any challenge - purely observes and waits for a human.
 */
export async function waitForHumanLogin(
  flowPage: GoogleFlowPage,
  options: WaitForHumanLoginOptions,
): Promise<boolean> {
  const deadline = Date.now() + options.timeoutMs;
  let announced = false;

  // eslint-disable-next-line no-constant-condition
  while (true) {
    if (await flowPage.isAuthenticated()) {
      return true;
    }
    if (Date.now() >= deadline) {
      return false;
    }
    if (!announced) {
      options.onWaiting?.();
      announced = true;
    }
    await sleep(options.pollIntervalMs);
  }
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
