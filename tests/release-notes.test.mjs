import assert from "node:assert/strict";
import test from "node:test";
import { releaseNotes } from "../scripts/release-notes.mjs";

const changelog = "# Changelog\n\n## [Unreleased]\n\n- Future work\n\n## [0.1.1] - 2026-09-19\n\n### Fixed\n\n- Correct invoice export.\n\n## [0.1.0] - 2026-09-01\n\n- Previous version.\n";

test("release notes contain only the matching version, including CRLF files", () => {
  for (const text of [changelog, changelog.replaceAll("\n", "\r\n")]) {
    assert.equal(releaseNotes("v0.1.1", "0.1.1", text), "### Fixed\n\n- Correct invoice export.\n");
  }
});

test("release publication rejects mismatched tags and missing or empty entries", () => {
  for (const tag of ["v0.1.0", "0.1.1", "v0.1.1-rc.1", undefined]) {
    assert.throws(() => releaseNotes(tag, "0.1.1", changelog), /tag must match/);
  }
  assert.throws(() => releaseNotes("v0.1.2", "0.1.2", changelog), /Missing dated/);
  assert.throws(() => releaseNotes("v0.1.1", "0.1.1", "## [0.1.1] - 2026-09-19\n\n## [0.1.0] - 2026-09-01\n- Old"), /Empty/);
});


test("Release Please linked headings work without including adjacent releases", () => {
  const notes = "# Changelog\n\n## [0.2.0](https://github.com/example/app/compare/v0.1.2...v0.2.0) (2026-09-21)\n\n### Added\n\n- Delete documents.\n\n### [0.1.2](https://github.com/example/app/compare/v0.1.1...v0.1.2) (2026-09-20)\n\n### Fixed\n\n- Fix preview.\n\n## [Unreleased]\n\n- Future work.\n\n## [0.1.1] - 2026-09-19\n\n- Older release.\n";
  assert.equal(releaseNotes("v0.2.0", "0.2.0", notes), "### Added\n\n- Delete documents.\n");
  assert.equal(releaseNotes("v0.1.2", "0.1.2", notes), "### Fixed\n\n- Fix preview.\n");
  assert.equal(releaseNotes("v0.1.1", "0.1.1", notes), "- Older release.\n");
});

test("Release Please unlinked headings and CRLF retain the matching release only", () => {
  const notes = "## 0.2.0 (2026-09-21)\r\n\r\n- New feature.\r\n\r\n### 0.1.2 (2026-09-20)\r\n\r\n- Fix.\r\n";
  assert.equal(releaseNotes("v0.2.0", "0.2.0", notes), "- New feature.\n");
});

test("generated entries must have an exact version, date and nonempty body", () => {
  for (const notes of [
    "## [0.1.10](https://example.test/compare) (2026-09-19)\n- Other version.",
    "## [0.1.1](https://example.test/compare)\n- Missing date.",
    "## [0.1.1](https://example.test/compare) (yesterday)\n- Bad date.",
  ]) assert.throws(() => releaseNotes("v0.1.1", "0.1.1", notes), /Missing dated/);
  assert.throws(() => releaseNotes("v0.1.2", "0.1.2", "### [0.1.2](https://example.test/compare) (2026-09-20)\n\n## [0.1.1] - 2026-09-19\n- Older release."), /Empty/);
});
