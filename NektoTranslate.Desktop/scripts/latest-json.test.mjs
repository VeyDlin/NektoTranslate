// node --test, not vitest: NektoTranslate.Desktop has no test runner of its own (unlike the client),
// and Node's built-in one needs nothing added to package.json to run these.
import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";

import { buildLatestJson, extractNotes, parseArgs, releaseUrl } from "./latest-json.mjs";


test("parseArgs reads the three named arguments", () => {
    const options = parseArgs(["--version=0.2.1", "--artifacts=dist/artifacts", "--notes=changelog.md"]);

    assert.deepEqual(options, { version: "0.2.1", artifacts: "dist/artifacts", notes: "changelog.md" });
});


test("parseArgs refuses when any of the three is missing", () => {
    assert.throws(() => parseArgs(["--version=0.2.1", "--artifacts=dist/artifacts"]));
});


test("extractNotes pulls only the bullet lines written under this version's own heading", () => {
    const changelog = [
        "## Which file is mine?",
        "",
        "| You are on | Take this file |",
        "",
        "## v0.2.1",
        "",
        "- Add the update banner",
        "- Fix a crash on startup",
        "",
        "## v0.2.0",
        "",
        "- An older release's own bullet, which must not leak into v0.2.1's notes",
    ].join("\n");

    assert.equal(extractNotes(changelog, "0.2.1"), "- Add the update banner\n- Fix a crash on startup");
});


test("extractNotes falls back to a link to the release when the heading is missing", () => {
    assert.equal(extractNotes("nothing recognisable here", "0.2.1"), releaseUrl("0.2.1"));
});


test("extractNotes falls back to a link to the release when the heading has no bullets under it", () => {
    const changelog = "## v0.2.1\n\nNo bullet lines here, just prose.";

    assert.equal(extractNotes(changelog, "0.2.1"), releaseUrl("0.2.1"));
});


test("buildLatestJson reads the installer and signature out of each platform's own bundle folder", () => {
    const artifacts = mkdtempSync(join(tmpdir(), "nekto-latest-json-"));

    try {
        mkdirSync(join(artifacts, "nsis"));
        writeFileSync(join(artifacts, "nsis", "NektoTranslate_0.2.1_x64-setup.exe"), "fake installer");
        writeFileSync(join(artifacts, "nsis", "NektoTranslate_0.2.1_x64-setup.exe.sig"), "windows-signature\n");

        mkdirSync(join(artifacts, "appimage"));
        writeFileSync(join(artifacts, "appimage", "NektoTranslate_0.2.1_amd64.AppImage"), "fake appimage");
        writeFileSync(join(artifacts, "appimage", "NektoTranslate_0.2.1_amd64.AppImage.sig"), "linux-signature\n");

        const latestJson = buildLatestJson({
            version: "0.2.1",
            artifactsDirectory: artifacts,
            notes: "- Add the update banner",
            pubDate: "2026-01-01T00:00:00.000Z",
        });

        assert.deepEqual(latestJson, {
            version: "0.2.1",
            notes: "- Add the update banner",
            pub_date: "2026-01-01T00:00:00.000Z",
            platforms: {
                "windows-x86_64": {
                    signature: "windows-signature",
                    url: "https://github.com/VeyDlin/NektoTranslate/releases/download/v0.2.1/NektoTranslate_0.2.1_x64-setup.exe",
                },
                "linux-x86_64": {
                    signature: "linux-signature",
                    url: "https://github.com/VeyDlin/NektoTranslate/releases/download/v0.2.1/NektoTranslate_0.2.1_amd64.AppImage",
                },
            },
        });
    }
    finally {
        rmSync(artifacts, { recursive: true, force: true });
    }
});


test("buildLatestJson skips a platform whose bundle folder does not exist, for a local single-platform build", () => {
    const artifacts = mkdtempSync(join(tmpdir(), "nekto-latest-json-"));

    try {
        mkdirSync(join(artifacts, "nsis"));
        writeFileSync(join(artifacts, "nsis", "NektoTranslate_9.9.9_x64-setup.exe"), "fake installer");
        writeFileSync(join(artifacts, "nsis", "NektoTranslate_9.9.9_x64-setup.exe.sig"), "windows-signature\n");

        // No "appimage" directory at all - a Windows machine's own `npm run build` never makes one.

        const latestJson = buildLatestJson({
            version: "9.9.9",
            artifactsDirectory: artifacts,
            notes: "",
            pubDate: "2026-01-01T00:00:00.000Z",
        });

        assert.deepEqual(Object.keys(latestJson.platforms), ["windows-x86_64"]);
    }
    finally {
        rmSync(artifacts, { recursive: true, force: true });
    }
});


test("buildLatestJson refuses when a platform's installer is missing", () => {
    const artifacts = mkdtempSync(join(tmpdir(), "nekto-latest-json-"));

    try {
        mkdirSync(join(artifacts, "nsis"));
        mkdirSync(join(artifacts, "appimage"));

        assert.throws(() => buildLatestJson({
            version: "0.2.1",
            artifactsDirectory: artifacts,
            notes: "",
            pubDate: "2026-01-01T00:00:00.000Z",
        }));
    }
    finally {
        rmSync(artifacts, { recursive: true, force: true });
    }
});
