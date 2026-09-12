// The client's side of the shell's own updater contract (see NektoTranslate.Desktop/README.md,
// "Updater"). Reaches `window.__TAURI__.core.invoke` and `window.__TAURI__.event.listen` the same
// way shell.ts reaches `window.__TAURI__.window` - typed by hand, no `@tauri-apps/api` dependency
// (see that file's own module comment for why). Every function here is a no-op outside the shell,
// so UpdateBanner.vue can call them unconditionally in a browser tab too.
import { desktopShell } from "./shell";


export interface UpdateAvailable {
    version: string;
    notes: string | null;
    date: string | null;
}

export interface DownloadResult {
    version: string;
    file: string;
}

export interface PendingUpdate {
    version: string;
    file: string;
    downloadedAt: string;
}

export interface DownloadProgress {
    downloaded: number;
    total: number | null;
}


// The slice of the Tauri v2 JS core/event API this client actually calls, typed by hand rather than
// pulled in from `@tauri-apps/api` - see the module comment above.
interface TauriCore {
    invoke: <T>(command: string) => Promise<T>;
}

interface TauriEvent {
    listen: <T>(event: string, handler: (event: { payload: T }) => void) => Promise<() => void>;
}

interface TauriGlobal {
    core: TauriCore;
    event: TauriEvent;
}

function tauriGlobal(): TauriGlobal | null {
    if (desktopShell === null) {
        return null;
    }

    return (window as { __TAURI__?: TauriGlobal }).__TAURI__ ?? null;
}


export async function checkForUpdate(): Promise<UpdateAvailable | null> {
    const tauri = tauriGlobal();

    if (tauri === null) {
        return null;
    }

    return tauri.core.invoke<UpdateAvailable | null>("update_check");
}


// Subscribes to `nekto:update-progress` for the duration of the download and unsubscribes again
// once it settles either way - a caller that never downloads again (the common case: one update per
// session) leaves nothing listening behind it.
export async function downloadUpdate(
    onProgress: (progress: DownloadProgress) => void,
): Promise<DownloadResult | null> {
    const tauri = tauriGlobal();

    if (tauri === null) {
        return null;
    }

    const unlisten = await tauri.event.listen<DownloadProgress>("nekto:update-progress", (event) => {
        onProgress(event.payload);
    });

    try {
        return await tauri.core.invoke<DownloadResult>("update_download");
    }
    finally {
        unlisten();
    }
}


export async function installUpdate(): Promise<void> {
    const tauri = tauriGlobal();

    if (tauri === null) {
        return;
    }

    await tauri.core.invoke("update_install");
}


export async function pendingUpdate(): Promise<PendingUpdate | null> {
    const tauri = tauriGlobal();

    if (tauri === null) {
        return null;
    }

    return tauri.core.invoke<PendingUpdate | null>("update_pending");
}
