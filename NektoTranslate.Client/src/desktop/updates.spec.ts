// @vitest-environment jsdom
//
// `window.__NEKTO_DESKTOP__` is never set here, the same as an ordinary browser tab - `desktopShell`
// (see shell.ts) is null, so every function below is exercised on its no-op path, which is the only
// path this project's own test runner - a plain browser tab, never a real shell - ever reaches.
import { describe, expect, it, vi } from "vitest";
import { checkForUpdate, downloadUpdate, installUpdate, pendingUpdate } from "./updates";


describe("updates.ts outside the shell", () => {
    it("checkForUpdate resolves null rather than reaching for window.__TAURI__", async () => {
        await expect(checkForUpdate()).resolves.toBeNull();
    });


    it("downloadUpdate resolves null and never calls the progress callback", async () => {
        const onProgress = vi.fn();

        await expect(downloadUpdate(onProgress)).resolves.toBeNull();
        expect(onProgress).not.toHaveBeenCalled();
    });


    it("installUpdate resolves without throwing", async () => {
        await expect(installUpdate()).resolves.toBeUndefined();
    });


    it("pendingUpdate resolves null", async () => {
        await expect(pendingUpdate()).resolves.toBeNull();
    });
});
