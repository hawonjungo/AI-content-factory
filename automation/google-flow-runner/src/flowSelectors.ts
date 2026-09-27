/**
 * ============================================================================
 *  PLACEHOLDER SELECTORS - NOT VERIFIED AGAINST THE REAL GOOGLE FLOW UI
 * ============================================================================
 *
 * This file was written without any ability to browse or authenticate to the
 * real labs.google/flow. Every selector below is a plausible-looking GUESS,
 * not something inspected on the live page. Do NOT trust these values.
 *
 * Before running this tool for real:
 *   1. Open labs.google/flow in a real browser, log in, and start a
 *      generation manually.
 *   2. Use the browser DevTools inspector to find the actual elements for
 *      each field below (prefer stable attributes - data-testid, aria-label,
 *      role - over CSS classes, which Google changes frequently).
 *   3. Replace every value here. Nothing outside this file and
 *      googleFlowPage.ts needs to change - that's the whole point of
 *      isolating Flow's DOM knowledge in these two files.
 *   4. Pay special attention to `renderCompleteIndicator` /
 *      `renderFailedIndicator` - the actual "generation finished" signal
 *      (a specific DOM element appearing, a spinner disappearing, a network
 *      response, a data-attribute changing) is currently unknown and is the
 *      single riskiest guess in this file.
 */
export interface FlowSelectors {
  /** Element that reliably indicates the user IS logged in (e.g. an avatar/profile menu). PLACEHOLDER - verify against the live Google Flow UI before use. */
  authenticatedIndicator: string;
  /** Element that reliably indicates a "sign in" prompt/button is showing. PLACEHOLDER - verify against the live Google Flow UI before use. */
  loginIndicator: string;
  /** The prompt text input/textarea where the Flow video prompt is typed/pasted. PLACEHOLDER - verify against the live Google Flow UI before use. */
  promptTextarea: string;
  /** The button that submits the prompt and starts generation. PLACEHOLDER - verify against the live Google Flow UI before use. */
  generateButton: string;
  /** Element/attribute that appears once rendering has finished successfully. PLACEHOLDER - verify against the live Google Flow UI before use - this is the least-certain selector in this file, see header comment. */
  renderCompleteIndicator: string;
  /** Element/attribute that appears if generation fails inside Flow (quota, content policy, provider error, etc). PLACEHOLDER - verify against the live Google Flow UI before use. */
  renderFailedIndicator: string;
  /** The clickable download button/link for the finished clip. PLACEHOLDER - verify against the live Google Flow UI before use. */
  downloadButton: string;
  /** Element indicating Flow itself is down/unreachable (maintenance page, generic error banner, etc). PLACEHOLDER - verify against the live Google Flow UI before use. */
  unavailableIndicator: string;
}

export const flowSelectors: FlowSelectors = {
  authenticatedIndicator: '[data-testid="user-avatar"]', // PLACEHOLDER - verify against the live Google Flow UI before use
  loginIndicator: 'text=/sign in/i', // PLACEHOLDER - verify against the live Google Flow UI before use
  promptTextarea: 'textarea[placeholder*="prompt" i]', // PLACEHOLDER - verify against the live Google Flow UI before use
  generateButton: 'button:has-text("Generate")', // PLACEHOLDER - verify against the live Google Flow UI before use
  renderCompleteIndicator: '[data-state="complete"]', // PLACEHOLDER - verify against the live Google Flow UI before use
  renderFailedIndicator: '[data-state="failed"]', // PLACEHOLDER - verify against the live Google Flow UI before use
  downloadButton: 'button:has-text("Download")', // PLACEHOLDER - verify against the live Google Flow UI before use
  unavailableIndicator: 'text=/something went wrong/i', // PLACEHOLDER - verify against the live Google Flow UI before use
};
