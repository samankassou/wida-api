import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

// Support both the historical Keep a Changelog headings and Release Please's
// linked headings (including level-three patch release headings).
function headingVersion(line) {
  const heading = /^#{2,3} (?:\[v?(\d+\.\d+\.\d+)\](?:\([^\s)]+\))?|v?(\d+\.\d+\.\d+)) (?:- \d{4}-\d{2}-\d{2}|\(\d{4}-\d{2}-\d{2}\))$/.exec(line);
  return heading?.[1] ?? heading?.[2];
}

export function releaseNotes(tag, version, changelog) {
  if (!/^v\d+\.\d+\.\d+$/.test(tag) || tag !== `v${version}`) {
    throw new Error("Release tag must match the stable version in version.txt.");
  }
  const lines = changelog.split(/\r?\n/);
  const start = lines.findIndex(line => headingVersion(line) === version);
  if (start < 0) throw new Error(`Missing dated changelog entry for ${version}.`);
  const end = lines.findIndex((line, index) => index > start && (line.startsWith("## ") || headingVersion(line) !== undefined));
  const notes = lines.slice(start + 1, end < 0 ? undefined : end).join("\n").trim();
  if (!notes) throw new Error(`Empty changelog entry for ${version}.`);
  return `${notes}\n`;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const version = readFileSync(new URL("../version.txt", import.meta.url), "utf8").trim();
  const changelog = readFileSync(new URL("../CHANGELOG.md", import.meta.url), "utf8");
  process.stdout.write(releaseNotes(process.argv[2], version, changelog));
}
