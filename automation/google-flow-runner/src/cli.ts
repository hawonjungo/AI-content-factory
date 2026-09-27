#!/usr/bin/env node
/**
 * CLI entry point. Deliberately uses only Node's built-in `util.parseArgs` -
 * no argument-parsing dependency.
 */
import { parseArgs } from "node:util";
import { run, RunAbortedError } from "./runner";
import { ApiError, NetworkError } from "./apiClient";
import { CdpConnectionError } from "./browserSession";
import { DEFAULT_BASE_URL, DEFAULT_CDP_ENDPOINT, DEFAULT_DOWNLOADS_DIR } from "./config";

function printUsage(): void {
  console.log(`
Google Flow Runner - OPTIONAL local automation for the Step 5 Google Flow
video-generation workflow. Drives labs.google/flow with Playwright using your
own logged-in session and free daily credits; never calls a paid API on your
behalf. The manual copy/paste + upload workflow in the app keeps working
without this tool.

Usage:
  npm start -- --project <contentProjectId> [options]

Options:
  --project <id>          Content project GUID (required)
  --base-url <url>        API base URL (default: ${DEFAULT_BASE_URL})
  --downloads-dir <dir>   Local downloads directory (default: ${DEFAULT_DOWNLOADS_DIR})
  --force                 Re-generate/re-download/re-import even if already done
  --cdp-endpoint <url>    Chrome DevTools Protocol endpoint of a Chrome window
                          YOU already launched and logged into Google yourself
                          (default: ${DEFAULT_CDP_ENDPOINT}). This is the
                          recommended mode - see README.md for the exact
                          command to launch Chrome this way. Google's sign-in
                          reliably blocks a Playwright-launched browser, so
                          this tool no longer launches its own by default.
  --launch-own-browser    Opt OUT of --cdp-endpoint and instead let Playwright
                          launch its own bundled Chromium ("Chrome for
                          Testing") in a persistent profile. NOT recommended -
                          Google's sign-in commonly blocks this as an
                          untrusted automation browser.
  --headless              Only applies with --launch-own-browser. Run headless
                          (NOT recommended - the first login needs a visible window)
  --help                  Show this help and exit
`);
}

async function main(): Promise<void> {
  const { values } = parseArgs({
    options: {
      project: { type: "string" },
      "base-url": { type: "string", default: DEFAULT_BASE_URL },
      "downloads-dir": { type: "string", default: DEFAULT_DOWNLOADS_DIR },
      force: { type: "boolean", default: false },
      "cdp-endpoint": { type: "string", default: DEFAULT_CDP_ENDPOINT },
      "launch-own-browser": { type: "boolean", default: false },
      headless: { type: "boolean", default: false },
      help: { type: "boolean", default: false },
    },
  });

  if (values.help) {
    printUsage();
    process.exit(0);
  }

  if (!values.project) {
    console.error("Error: --project <contentProjectId> is required.\n");
    printUsage();
    process.exit(1);
  }

  console.log(`Google Flow Runner starting for project ${values.project} against ${values["base-url"]}`);
  console.log("This drives labs.google/flow with your own free daily credits - it never calls a paid API on your behalf.");

  const result = await run({
    projectId: values.project,
    baseUrl: values["base-url"] as string,
    downloadsDir: values["downloads-dir"] as string,
    force: Boolean(values.force),
    launchOwnBrowser: Boolean(values["launch-own-browser"]),
    cdpEndpoint: values["cdp-endpoint"] as string,
    headless: Boolean(values.headless),
  });

  console.log("\n=== Run summary ===");
  console.log(`Completed: ${result.completed}`);
  console.log(`Skipped (already had a valid clip/import): ${result.skipped}`);
  console.log(
    `Failed: ${result.failed}${result.failedScenes.length > 0 ? ` (scenes: ${result.failedScenes.join(", ")})` : ""}`,
  );

  process.exitCode = result.failed > 0 ? 1 : 0;
}

main().catch((err) => {
  if (err instanceof RunAbortedError) {
    // The whole run stopped before any scene was attempted (Chrome's debug
    // port unreachable, Flow itself unreachable, or login timed out) -
    // message already explains why.
    console.error(`\nFatal error: ${err.message}`);
  } else if (err instanceof CdpConnectionError || err instanceof NetworkError) {
    // Message already explains the connectivity problem (either the app's
    // API, or Chrome's debug port) - this is never a credits/budget issue.
    console.error(`\nFatal error: ${err.message}`);
  } else if (err instanceof ApiError) {
    console.error(
      `\nFatal error: the API responded but rejected the request (HTTP ${err.status}).` +
        (err.body ? `\n${err.body}` : ""),
    );
  } else {
    console.error("\nFatal error:", err);
  }
  process.exitCode = 1;
});
