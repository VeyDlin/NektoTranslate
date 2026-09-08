// The client's side of the desktop shell contract (see NektoTranslate.Desktop/README.md, "Window
// chrome"). `window.__NEKTO_DESKTOP__` is written by lib.rs's desktop_script() before any of this
// module runs; in an ordinary browser tab it is simply never set. Everything here reads that global
// exactly once, defensively, and reaches the rest of the Tauri window API through the
// `window.__TAURI__` global `withGlobalTauri` already exposes - `@tauri-apps/api` is deliberately
// not a dependency of this client, which is a web app that happens to run inside a shell sometimes,
// not a Tauri app.
import { onMounted, onUnmounted, ref } from "vue";


export type DesktopPlatform = "windows" | "macos" | "linux";

export interface DesktopShell {
    platform: DesktopPlatform;
    version: string;
}

const KNOWN_PLATFORMS: readonly DesktopPlatform[] = ["windows", "macos", "linux"];


// Pure and exported on its own so a malformed or missing global - the ordinary case in a browser
// tab, and the only case anywhere in this repository's own test runner - can be exercised without
// touching `window` at all.
export function parseDesktopShell(value: unknown): DesktopShell | null {
    if (typeof value !== "object" || value === null) {
        return null;
    }

    const candidate = value as Record<string, unknown>;
    const platform = candidate.platform;
    const version = candidate.version;

    if (typeof platform !== "string" || !KNOWN_PLATFORMS.includes(platform as DesktopPlatform)) {
        return null;
    }

    if (typeof version !== "string") {
        return null;
    }

    return { platform: platform as DesktopPlatform, version };
}


export interface DesktopChromeLayout {
    barHeightPx: number;
    controlsSide: "left" | "right" | "none";
}

// The other pure part of the contract: per platform, only the bar height and which side the
// controls sit on ever change (macOS draws no controls of its own - the real traffic lights sit in
// the reserved inset instead). Everything else about the bar is identical on all three, per the
// design system's window-chrome reference.
export function chromeLayoutFor(platform: DesktopPlatform): DesktopChromeLayout {
    switch (platform) {
        case "macos":
            return { barHeightPx: 38, controlsSide: "none" };
        case "linux":
            return { barHeightPx: 40, controlsSide: "right" };
        default:
            return { barHeightPx: 32, controlsSide: "right" };
    }
}


// Read once at module load, the same moment the shell's own script has already run. `null` here is
// exactly "not in the shell" - a plain browser tab - and every caller treats it that way.
export const desktopShell: DesktopShell | null = parseDesktopShell(
    (window as { __NEKTO_DESKTOP__?: unknown }).__NEKTO_DESKTOP__,
);

if (desktopShell !== null) {
    document.documentElement.dataset.desktop = desktopShell.platform;
}


// The slice of the Tauri v2 JS window API this client actually calls, typed by hand rather than
// pulled in from `@tauri-apps/api` - see the module comment for why. `withGlobalTauri` in
// tauri.conf.json is what puts this object on `window` at all.
interface TauriWindowHandle {
    minimize: () => Promise<void>;
    toggleMaximize: () => Promise<void>;
    close: () => Promise<void>;
    isMaximized: () => Promise<boolean>;
    isFullscreen: () => Promise<boolean>;
    onResized: (handler: () => void) => Promise<() => void>;
}

interface TauriGlobal {
    window: {
        getCurrentWindow: () => TauriWindowHandle;
    };
}

function currentTauriWindow(): TauriWindowHandle | null {
    const tauri = (window as { __TAURI__?: TauriGlobal }).__TAURI__;

    return tauri === undefined ? null : tauri.window.getCurrentWindow();
}


export interface UseDesktopShellResult {
    desktopShell: DesktopShell | null;
    isMaximized: ReturnType<typeof ref<boolean>>;
    isFullscreen: ReturnType<typeof ref<boolean>>;
    minimize: () => void;
    toggleMaximize: () => void;
    close: () => void;
}

// WindowChrome.vue's one way into the shell: the parsed contract, the two bits of window state it
// cannot get from anywhere else, and the three actions its buttons call. A no-op everywhere but
// inside the shell - every ref stays at its initial value and every action does nothing, the same
// as the chrome component itself, which is never rendered when `desktopShell` is null.
export function useDesktopShell(): UseDesktopShellResult {
    const isMaximized = ref(false);
    const isFullscreen = ref(false);

    let unlistenResized: (() => void) | null = null;

    async function refreshMaximized(handle: TauriWindowHandle): Promise<void> {
        isMaximized.value = await handle.isMaximized();
    }

    function onFullscreenEvent(event: Event): void {
        const detail = (event as CustomEvent<{ fullscreen: boolean }>).detail;

        isFullscreen.value = detail.fullscreen;
    }

    onMounted(() => {
        if (desktopShell === null) {
            return;
        }

        window.addEventListener("nekto:fullscreen", onFullscreenEvent);

        const handle = currentTauriWindow();

        if (handle === null) {
            return;
        }

        void refreshMaximized(handle);
        void handle.isFullscreen().then((value) => {
            isFullscreen.value = value;
        });
        void handle.onResized(() => {
            void refreshMaximized(handle);
        }).then((unlisten) => {
            unlistenResized = unlisten;
        });
    });

    onUnmounted(() => {
        window.removeEventListener("nekto:fullscreen", onFullscreenEvent);
        unlistenResized?.();
    });

    function minimize(): void {
        void currentTauriWindow()?.minimize();
    }

    function toggleMaximize(): void {
        void currentTauriWindow()?.toggleMaximize();
    }

    function close(): void {
        void currentTauriWindow()?.close();
    }

    return { desktopShell, isMaximized, isFullscreen, minimize, toggleMaximize, close };
}
