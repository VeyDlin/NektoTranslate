#!/usr/bin/env node
// Publishes NektoTranslate.Api - self-contained, for one platform - into src-tauri/server/, where
// tauri.conf.json's bundle.resources picks it up and server.rs looks for it beside the shell.
//
// The dotnet publish itself builds the client into wwwroot (NektoTranslate.Api.csproj's own
// BuildClient target); nothing here duplicates that.
import { execFileSync } from "node:child_process";
import { mkdirSync, readdirSync, rmSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";


const here = dirname(fileURLToPath(import.meta.url));
const desktopRoot = join(here, "..");
const repoRoot = join(desktopRoot, "..");
const apiProject = join(repoRoot, "NektoTranslate.Server", "NektoTranslate.Api");
const outputDirectory = join(desktopRoot, "src-tauri", "server");


// The RID this machine would publish for if nothing overrides it - matches the four platforms the
// root README already documents for the server on its own.
function currentRid() {
    if (process.platform === "win32") {
        return "win-x64";
    }

    if (process.platform === "darwin") {
        return process.arch === "arm64" ? "osx-arm64" : "osx-x64";
    }

    if (process.platform === "linux") {
        return "linux-x64";
    }

    throw new Error(`No default runtime identifier for platform "${process.platform}" - pass --rid explicitly.`);
}


// CI needs to reach both flags without them landing on the `tauri build` half of `npm run build`
// (npm appends trailing CLI args to the end of that whole two-command script, not to whichever
// command should read them) - so both also read from an environment variable, and CI sets those
// instead of passing arguments through npm.
function parseOptions() {
    let rid = process.env.NEKTO_RID ?? null;
    let version = process.env.NEKTO_BUILD_VERSION ?? null;

    for (const arg of process.argv.slice(2)) {
        if (arg.startsWith("--rid=")) {
            rid = arg.slice("--rid=".length);
        } else if (arg.startsWith("--version=")) {
            version = arg.slice("--version=".length);
        }
    }

    return { rid: rid ?? currentRid(), version };
}


// *.pdb carries no runtime value in a published bundle and only adds weight to every installer -
// dotnet publish writes one beside every assembly it produces, self-contained or not.
function removePdbFiles(directory) {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
        const full = join(directory, entry.name);

        if (entry.isDirectory()) {
            removePdbFiles(full);
        } else if (entry.name.endsWith(".pdb")) {
            rmSync(full);
        }
    }
}


const { rid, version } = parseOptions();

console.log(`Publishing NektoTranslate.Api (${rid}) into ${outputDirectory}`);

// A stale binary from a previous RID left behind would still get bundled as a resource even though
// nothing published it this run - the directory starts empty every time instead.
rmSync(outputDirectory, { recursive: true, force: true });
mkdirSync(outputDirectory, { recursive: true });

const publishArgs = [
    "publish",
    apiProject,
    "-c", "Release",
    "-r", rid,
    "--self-contained",
    "-o", outputDirectory
];

if (version) {
    publishArgs.push(`-p:Version=${version}`);
}

execFileSync("dotnet", publishArgs, { stdio: "inherit" });

removePdbFiles(outputDirectory);

console.log("Server publish complete.");
