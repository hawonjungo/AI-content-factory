import { test } from "node:test";
import assert from "node:assert/strict";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import {
  clipFilePath,
  ensureProjectDownloadsDir,
  hasExistingValidClip,
  validateDownloadedFile,
} from "../src/download";

function makeTmpDir(): string {
  return fs.mkdtempSync(path.join(os.tmpdir(), "flow-runner-test-"));
}

test("clipFilePath is deterministic and namespaced per project, preserving sceneNumber verbatim", () => {
  const downloadsDir = path.join("downloads-root");
  const a = clipFilePath(downloadsDir, "proj-1", 4);
  const b = clipFilePath(downloadsDir, "proj-1", 4);
  assert.equal(a, b);
  assert.equal(a, path.join(downloadsDir, "project_proj-1", "clip_4.mp4"));
});

test("clipFilePath namespaces by project - two different projects never collide on the same scene number", () => {
  const downloadsDir = path.join("downloads-root");
  const a = clipFilePath(downloadsDir, "proj-1", 4);
  const b = clipFilePath(downloadsDir, "proj-2", 4);
  assert.notEqual(a, b);
});

test("ensureProjectDownloadsDir creates a real, per-project directory", () => {
  const base = makeTmpDir();
  const dir = ensureProjectDownloadsDir(base, "proj-xyz");
  assert.ok(fs.existsSync(dir));
  assert.ok(fs.statSync(dir).isDirectory());
  assert.equal(dir, path.join(base, "project_proj-xyz"));
});

test("a missing file is classified as invalid, never as success", () => {
  const base = makeTmpDir();
  const target = path.join(base, "does-not-exist.mp4");

  const result = validateDownloadedFile(target);
  assert.equal(result.ok, false);
  assert.match(result.reason ?? "", /does not exist/i);
  assert.equal(hasExistingValidClip(target), false);
});

test("a zero-byte file is classified as invalid, never as success", () => {
  const base = makeTmpDir();
  const target = path.join(base, "empty.mp4");
  fs.writeFileSync(target, Buffer.alloc(0));

  const result = validateDownloadedFile(target);
  assert.equal(result.ok, false);
  assert.match(result.reason ?? "", /zero bytes/i);
  assert.equal(hasExistingValidClip(target), false);
});

test("a non-.mp4 extension is classified as invalid even if the file has content", () => {
  const base = makeTmpDir();
  const target = path.join(base, "clip.mov");
  fs.writeFileSync(target, Buffer.from("some non-empty content"));

  const result = validateDownloadedFile(target);
  assert.equal(result.ok, false);
  assert.match(result.reason ?? "", /\.mp4/);
});

test("a directory named *.mp4 is classified as invalid (not a regular file)", () => {
  const base = makeTmpDir();
  const target = path.join(base, "clip.mp4");
  fs.mkdirSync(target);

  const result = validateDownloadedFile(target);
  assert.equal(result.ok, false);
  assert.match(result.reason ?? "", /not a regular file/i);
});

test("a real non-empty .mp4 file is classified as valid", () => {
  const base = makeTmpDir();
  const target = path.join(base, "clip.mp4");
  fs.writeFileSync(target, Buffer.from("fake-mp4-bytes-for-test-purposes"));

  const result = validateDownloadedFile(target);
  assert.equal(result.ok, true);
  assert.equal(result.reason, undefined);
  assert.equal(hasExistingValidClip(target), true);
});
