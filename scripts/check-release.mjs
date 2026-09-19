import { readFileSync } from "node:fs";
import { releaseNotes } from "./release-notes.mjs";

const version = readFileSync(new URL("../version.txt", import.meta.url), "utf8").trim();
const manifest = JSON.parse(readFileSync(new URL("../.release-please-manifest.json", import.meta.url), "utf8"));
if (!/^\d+\.\d+\.\d+$/.test(version) || manifest["."] !== version) {
  throw new Error("version.txt must contain a stable version matching the Release Please manifest.");
}
// 0.0.0 is an unpublished bootstrap marker, never an API release.
if (version !== "0.0.0") {
  const changelog = readFileSync(new URL("../CHANGELOG.md", import.meta.url), "utf8");
  releaseNotes(`v${version}`, version, changelog);
}
