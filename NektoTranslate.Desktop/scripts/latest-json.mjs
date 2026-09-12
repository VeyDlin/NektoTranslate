#!/usr/bin/env node
// Writes latest.json - the static update manifest tauri-plugin-updater polls on every check - from
// the exact version and artifacts the release workflow already built, rather than splicing the file
// together with shell string interpolation. See NektoTranslate.Desktop/README.md for how to point a
// local build at a hand-made one of these instead of the real GitHub release.
//
// Usage: node latest-json.mjs --version=<v> --artifacts=<dir> --notes=<changelog file>
// Writes <artifacts>/latest.json, the same folder the release job already gathers every file it
// attaches from.
import { readdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";


const REPO = "VeyDlin/NektoTranslate";

// Which platform key latest.json wants for each bundle glob's own subdirectory (see release.yml's
// `bundle_glob` per matrix leg), and the extension that tells the installer apart from the detached
// signature `createUpdaterArtifacts` writes right beside it.
const PLATFORMS = [
    { key: "windows-x86_64", directory: "nsis", extension: ".exe" },
    { key: "linux-x86_64", directory: "appimage", extension: ".AppImage" },
];


export function parseArgs(argv) {
    const options = {};

    for (const arg of argv) {
        const match = arg.match(/^--([a-z]+)=(.*)$/);

        if (match !== null) {
            options[match[1]] = match[2];
        }
    }

    if (!options.version || !options.artifacts || !options.notes) {
        throw new Error("Usage: latest-json.mjs --version=<v> --artifacts=<dir> --notes=<file>");
    }

    return options;
}


export function releaseUrl(version) {
    return `https://github.com/${REPO}/releases/tag/v${version}`;
}


// The notes file is the release job's own changelog.md - the "Which file is mine?" block and every
// past release's own heading live in there too, so only the bullet lines actually written for this
// version are pulled out. The intro is for a person reading the release page, not for an update
// dialog. A changelog with nothing recognisable in it (a first release with a hand-edited notes
// file, say) falls back to a link to the release itself, which is never wrong.
export function extractNotes(changelog, version) {
    const heading = `## v${version}`;
    const at = changelog.indexOf(heading);

    if (at === -1) {
        return releaseUrl(version);
    }

    const afterHeading = changelog.slice(at + heading.length);

    // Stops at the next "## " heading - an older release's own section, immediately below this
    // one in the same changelog.md - rather than reading to the end of the file and picking up
    // every bullet ever written.
    const nextHeadingAt = afterHeading.indexOf("\n## ");
    const section = nextHeadingAt === -1 ? afterHeading : afterHeading.slice(0, nextHeadingAt);

    const bullets = section
        .split("\n")
        .filter(line => line.startsWith("- "))
        .join("\n");

    return bullets.length > 0 ? bullets : releaseUrl(version);
}


// One installer and its detached .sig, found by extension inside the one subdirectory that bundle
// glob ever writes to - never by name. The file name carries the version this script was also given,
// but the two are never compared; the folder alone is enough to know which file is meant.
//
// `null` when the platform's own bundle folder does not exist at all, rather than an error - the CI
// release job always has both (the matrix builds Windows and Linux together), but a local
// `npm run build` only ever produces the one folder for whatever platform it ran on, and the dry run
// described in the README has nothing else to point this script at.
function findArtifact(artifactsDirectory, platform) {
    const directory = join(artifactsDirectory, platform.directory);
    let entries;

    try {
        entries = readdirSync(directory);
    }
    catch (error) {
        if (error.code === "ENOENT") {
            return null;
        }

        throw error;
    }

    const installer = entries.find(name => name.endsWith(platform.extension));
    const signatureName = entries.find(name => name.endsWith(`${platform.extension}.sig`));

    if (installer === undefined) {
        throw new Error(`No ${platform.extension} file found in ${directory}`);
    }

    if (signatureName === undefined) {
        throw new Error(`No ${platform.extension}.sig file found in ${directory} - was createUpdaterArtifacts on?`);
    }

    return {
        installer,
        signature: readFileSync(join(directory, signatureName), "utf8").trim(),
    };
}


export function buildLatestJson({ version, artifactsDirectory, notes, pubDate }) {
    const platforms = {};

    for (const platform of PLATFORMS) {
        const found = findArtifact(artifactsDirectory, platform);

        if (found === null) {
            continue;
        }

        platforms[platform.key] = {
            signature: found.signature,
            url: `https://github.com/${REPO}/releases/download/v${version}/${found.installer}`,
        };
    }

    if (Object.keys(platforms).length === 0) {
        throw new Error(`No platform bundle found under ${artifactsDirectory} for any of: ${PLATFORMS.map(p => p.directory).join(", ")}`);
    }

    return { version, notes, pub_date: pubDate, platforms };
}


function main() {
    const options = parseArgs(process.argv.slice(2));
    const changelog = readFileSync(options.notes, "utf8");

    const latestJson = buildLatestJson({
        version: options.version,
        artifactsDirectory: options.artifacts,
        notes: extractNotes(changelog, options.version),
        pubDate: new Date().toISOString(),
    });

    const outPath = join(options.artifacts, "latest.json");

    writeFileSync(outPath, `${JSON.stringify(latestJson, null, 2)}\n`);

    console.log(`Wrote ${outPath} for v${options.version}`);
}


// Only run as a script - importing this module for its unit tests must not also write a file.
if (process.argv[1] !== undefined && fileURLToPath(import.meta.url) === process.argv[1]) {
    main();
}
