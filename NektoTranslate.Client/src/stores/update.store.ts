import { defineStore } from "pinia";

import { ref } from "vue";


export type UpdateState = "idle" | "available" | "downloading" | "ready";


// The one piece of update state shared between the shell's own multi-stage flow (idle -> available
// -> downloading -> ready, see UpdateBanner.vue) and the browser's much shorter one (idle ->
// available, with nothing past it - there is no download to run from a browser tab). Kept this
// small on purpose: everything a browser tab alone needs (the release URL) or that only matters for
// one session's own UI (the *Later* dismissal) lives in UpdateBanner.vue itself rather than here.
export const useUpdateStore = defineStore("update", () => {
    const state = ref<UpdateState>("idle");
    const version = ref<string | null>(null);
    const progress = ref(0);

    // The person said *Not now* on the ready dialog this session - it then stays a quiet
    // "<version> ready · Restart" line instead of the dialog reappearing on its own.
    const deferred = ref(false);


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
    }


    function setProgress(downloaded: number, total: number | null): void {
        progress.value = total !== null && total > 0 ? Math.min(1, downloaded / total) : progress.value;
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
        deferred.value = false;
    }


    return { state, version, progress, deferred, offer, startDownload, setProgress, ready, defer, reset };
});
