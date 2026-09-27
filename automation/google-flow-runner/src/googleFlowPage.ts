/**
 * Page-object/adapter for labs.google/flow. This is the ONLY place (besides
 * flowSelectors.ts) that is allowed to know about Google Flow's DOM.
 * Everything else in this package (runner.ts, stateMachine.ts, queue.ts,
 * etc.) must go through this class and must never import Playwright's `Page`
 * type for Flow-specific logic.
 *
 * IMPORTANT - HONESTY NOTE: the method bodies below are a reasonable-looking
 * implementation shape, but they have NEVER been run against the real
 * labs.google/flow site, because this environment cannot browse or
 * authenticate to it. `flowSelectors` are placeholders (see flowSelectors.ts)
 * and `waitForRender`'s notion of "complete" is an unverified guess. Treat
 * this class as structurally-ready-but-unverified, not working code.
 */
import type { Locator, Page } from "playwright";
import type { FlowSelectors } from "./flowSelectors";

/**
 * Entry URL for Google Flow. PLACEHOLDER - confirm the exact path (it may
 * require a specific project/workspace URL rather than the bare marketing
 * page) once real access is available.
 */
export const FLOW_URL = "https://labs.google/flow";

export interface SubmitPromptOptions {
  /** "Fast" | "Lite" (FlowPlanScene.recommendedModel) - model-selection UI is not modeled yet, see note in submitPrompt(). */
  model?: string | null;
}

export type RenderOutcome = "complete" | "failed" | "timeout";

export class GoogleFlowPage {
  constructor(
    private readonly page: Page,
    private readonly selectors: FlowSelectors,
  ) {}

  async goto(): Promise<void> {
    await this.page.goto(FLOW_URL, { waitUntil: "domcontentloaded" });
  }

  /** Best-effort "am I logged in" check. Never attempts to log in itself. */
  async isAuthenticated(): Promise<boolean> {
    const authedCount = await this.locator(this.selectors.authenticatedIndicator).count();
    if (authedCount > 0) {
      return true;
    }
    const loginPromptCount = await this.locator(this.selectors.loginIndicator).count();
    // Neither indicator present: rather than guessing, treat as "not
    // authenticated" so the caller waits for a human rather than proceeding
    // on an ambiguous signal.
    return loginPromptCount === 0 && authedCount > 0;
  }

  /** Best-effort "is Flow itself broken/unreachable right now" check. */
  async isFlowUnavailable(): Promise<boolean> {
    return (await this.locator(this.selectors.unavailableIndicator).count()) > 0;
  }

  async submitPrompt(prompt: string, options: SubmitPromptOptions = {}): Promise<void> {
    // NOTE: model selection (Fast vs Lite) is not modeled here yet - Flow's
    // actual UI for choosing a model tier is unknown. `options.model` is
    // accepted for forward-compatibility (so callers/tests don't need to
    // change once this is implemented) but is currently unused.
    void options.model;

    const box = this.locator(this.selectors.promptTextarea);
    await box.fill(prompt);
    await this.locator(this.selectors.generateButton).click();
  }

  /**
   * Waits for either the "render complete" or "render failed" signal, or the
   * given timeout, whichever comes first.
   *
   * UNVERIFIED: the real completion signal (a specific element appearing, a
   * spinner disappearing, a network response resolving, a data-attribute
   * flipping) is not known. `renderCompleteIndicator` /
   * `renderFailedIndicator` in flowSelectors.ts are guesses and MUST be
   * confirmed by a human watching a real generation run before this method
   * can be trusted.
   */
  async waitForRender(timeoutMs: number): Promise<RenderOutcome> {
    const complete = this.locator(this.selectors.renderCompleteIndicator)
      .waitFor({ state: "visible", timeout: timeoutMs })
      .then((): RenderOutcome => "complete");
    const failed = this.locator(this.selectors.renderFailedIndicator)
      .waitFor({ state: "visible", timeout: timeoutMs })
      .then((): RenderOutcome => "failed");

    try {
      return await Promise.race([complete, failed]);
    } catch {
      return "timeout";
    }
  }

  /** Clicks the download control and saves the resulting file to targetPath. */
  async download(targetPath: string): Promise<void> {
    const [download] = await Promise.all([
      this.page.waitForEvent("download"),
      this.locator(this.selectors.downloadButton).click(),
    ]);
    await download.saveAs(targetPath);
  }

  private locator(selector: string): Locator {
    return this.page.locator(selector);
  }
}
