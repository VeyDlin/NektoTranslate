#!/usr/bin/env node
// Writes the version read from NektoTranslate.Server/Directory.Build.props - the project's single
// source of truth (see publish-server.mjs) - into the three desktop files that also carry a version
// number: tauri.conf.json, Cargo.toml and this package's own package.json. Idempotent: a file whose
// version already matches is left untouched, so running this never dirties a tree that is already in
// sync, and a plain `tauri dev` still works from whatever is committed.
//
// `--check` compares instead of writing and exits 1 on any mismatch, for CI to call so a commit
// cannot let the desktop files drift away from Directory.Build.props.
import { readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";


const here = dirname(fileURLToPath(import.meta.url));
const desktopRoot = join(here, "..");
const repoRoot = join(desktopRoot, "..");
const directoryBuildProps = join(repoRoot, "NektoTranslate.Server", "Directory.Build.props");
const tauriConfPath = join(desktopRoot, "src-tauri", "tauri.conf.json");
const cargoTomlPath = join(desktopRoot, "src-tauri", "Cargo.toml");
const packageJsonPath = join(desktopRoot, "package.json");

const checkOnly = process.argv.includes("--check");


function versionFromDirectoryBuildProps() {
    const contents = readFileSync(directoryBuildProps, "utf8");
    const match = contents.match(/<Version>([^<]+)<\/Version>/);

    if (match === null) {
        throw new Error(`No <Version> element found in ${directoryBuildProps}`);
    }

    return match[1].trim();
}


// Rewritten by regex rather than parse-and-stringify, so everything else about the file - key
// order, indentation, trailing newline - survives untouched. Returns whether the file was (or, in
// check mode, would be) changed.
function syncJsonVersion(path, version) {
    const contents = readFileSync(path, "utf8");
    const match = contents.match(/"version"\s*:\s*"([^"]*)"/);

    if (match === null) {
        throw new Error(`No "version" field found in ${path}`);
    }

    if (match[1] === version) {
        return false;
    }

    if (!checkOnly) {
        writeFileSync(path, contents.replace(/"version"\s*:\s*"[^"]*"/, `"version": "${version}"`));
    }

    return true;
}


// Cargo.toml's version lives under [package] as version = "..."; the regex matches the first such
// line, which is that one - src-tauri/Cargo.toml has no other top-level "version" key.
function syncCargoVersion(path, version) {
    const contents = readFileSync(path, "utf8");
    const match = contents.match(/^version\s*=\s*"([^"]*)"/m);

    if (match === null) {
        throw new Error(`No "version" field found in ${path}`);
    }

    if (match[1] === version) {
        return false;
    }

    if (!checkOnly) {
        writeFileSync(path, contents.replace(/^version\s*=\s*"[^"]*"/m, `version = "${version}"`));
    }

    return true;
}


const version = versionFromDirectoryBuildProps();
const drifted = [];

if (syncJsonVersion(tauriConfPath, version)) {
    drifted.push(tauriConfPath);
}

if (syncCargoVersion(cargoTomlPath, version)) {
    drifted.push(cargoTomlPath);
}

if (syncJsonVersion(packageJsonPath, version)) {
    drifted.push(packageJsonPath);
}

if (checkOnly) {
    if (drifted.length > 0) {
        console.error(`Desktop files out of sync with Directory.Build.props (v${version}):`);

        for (const path of drifted) {
            console.error(`  ${path}`);
        }

        console.error("Run `node scripts/sync-version.mjs` to fix.");
        process.exit(1);
    }

    console.log(`Desktop files already match v${version}.`);
}
else if (drifted.length > 0) {
    console.log(`Synced ${drifted.length} file(s) to v${version}.`);
}
else {
    console.log(`Desktop files already match v${version}.`);
}
