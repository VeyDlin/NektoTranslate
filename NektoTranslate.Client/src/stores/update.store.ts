import { defineStore } from "pinia";

import { ref } from "vue";


export type UpdateState = "idle" | "available" | "downloading" | "ready";


// The one piece of update state shared between the shell's own multi-stage flow (idle -> available
// -> downloading -> ready, see UpdateBanner.vue) and the browser's much shorter one (idle ->
// available, with nothing past it - there is no download to run from a browser tab). The banner
// that shows it is mounted by every screen's own AppBar, so it comes and goes with every
// navigation: anything that has to outlive one screen - the dismissal, the download's byte count,
// when the shell last asked the endpoint - lives here, not in the component.
export const useUpdateStore = defineStore("update", () => {
    const state = ref<UpdateState>("idle");
    const version = ref<string | null>(null);
    const progress = ref(0);

    // The download's content length, when the server sent one - what lets the banner show a
    // percentage rather than only a moving bar. Null until the first progress event says otherwise.
    const total = ref<number | null>(null);

    // The person said *Not now* on the ready dialog this session - it then stays a quiet
    // "<version> ready · Restart" line instead of the dialog reappearing on its own.
    const deferred = ref(false);

    // *Later* was clicked on the "available" line: hidden until the next launch, which is a fresh
    // store. Session-wide, so the next screen does not bring the line straight back.
    const laterDismissed = ref(false);

    // When the shell last asked its endpoint, so a banner mounting on every screen change does not
    // turn into a network request on every screen change.
    const lastCheckedAt = ref<number | null>(null);


    // A later, still-newer check must not clobber a download or a ready state already under way -
    // the six-hour poll re-announcing the same version (or an even newer one arriving mid-download)
    // is not a reason to restart the flow from "available".
    function offer(nextVersion: string): void {
        if (state.value !== "idle") {
            return;
        }

        state.value = "available";
        version.value = nextVersion;
        deferred.value = false;
    }


    function startDownload(): void {
        state.value = "downloading";
        progress.value = 0;
        total.value = null;
    }


    function setProgress(downloaded: number, contentLength: number | null): void {
        total.value = contentLength;
        progress.value = contentLength !== null && contentLength > 0 ? Math.min(1, downloaded / contentLength) : progress.value;
    }


    function dismissLater(): void {
        laterDismissed.value = true;
    }


    // Whether enough time has passed since the last check to ask again; marks the check as made
    // when it says yes, so two banners mounting in quick succession do not both ask.
    function shouldCheck(intervalMs: number): boolean {
        const now = Date.now();

        if (lastCheckedAt.value !== null && now - lastCheckedAt.value < intervalMs) {
            return false;
        }

        lastCheckedAt.value = now;

        return true;
    }


    function ready(): void {
        state.value = "ready";
        progress.value = 1;
    }


    function defer(): void {
        deferred.value = true;
    }


    // Back to nothing offered - a download that failed, or one that turned out not to be available
    // after all (the endpoint disagreed by the time the download command actually ran).
    function reset(): void {
        state.value = "idle";
        version.value = null;
        progress.value = 0;
        total.value = null;
        deferred.value = false;
    }


    return {
        state,
        version,
        progress,
        total,
        deferred,
        laterDismissed,
        lastCheckedAt,
        offer,
        startDownload,
        setProgress,
        ready,
        defer,
        dismissLater,
        shouldCheck,
        reset,
    };
});
