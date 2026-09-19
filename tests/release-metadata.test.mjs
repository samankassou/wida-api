import assert from "node:assert/strict";
import test from "node:test";
import { realpathSync, mkdtempSync, mkdirSync, copyFileSync, writeFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { spawnSync } from "node:child_process";

function fixture(t, version, manifestVersion, changelog = "") {
  const root = realpathSync(mkdtempSync(join(tmpdir(), "wida-api-release-")));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  mkdirSync(join(root, "scripts"));
  for (const name of ["release-notes.mjs", "check-release.mjs"]) {
    copyFileSync(new URL(`../scripts/${name}`, import.meta.url), join(root, "scripts", name));
  }
  writeFileSync(join(root, "version.txt"), version + "\n");
  writeFileSync(join(root, ".release-please-manifest.json"), JSON.stringify({ ".": manifestVersion }));
  writeFileSync(join(root, "CHANGELOG.md"), changelog);
  return (script, ...args) => spawnSync(process.execPath, [join(root, "scripts", script), ...args], { encoding: "utf8" });
}

test("bootstrap metadata passes CI but cannot be published", t => {
  const run = fixture(t, "0.0.0", "0.0.0", "# Changelog\n\n## [Unreleased]\n");
  assert.equal(run("check-release.mjs").status, 0);
  assert.notEqual(run("release-notes.mjs", "v0.0.0").status, 0);
});

test("metadata requires matching versions and dated notes once bootstrapped", t => {
  assert.notEqual(fixture(t, "0.1.0", "0.0.0")("check-release.mjs").status, 0);
  assert.notEqual(fixture(t, "invalid", "invalid")("check-release.mjs").status, 0);
  assert.notEqual(fixture(t, "0.1.0", "0.1.0")("check-release.mjs").status, 0);
  const run = fixture(t, "0.1.0", "0.1.0", "## [0.1.0](https://example.test/releases) (2026-09-19)\n\n- Document deletion.\n");
  assert.equal(run("check-release.mjs").status, 0);
  assert.equal(run("release-notes.mjs", "v0.1.0").stdout, "- Document deletion.\n");
});
