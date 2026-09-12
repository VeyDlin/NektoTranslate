<template>
    <div v-if="showBanner" class="banner">
        <div class="row">
            <template v-if="update.state === 'ready'">
                <span class="text">{{ update.version }} ready</span>
                <span class="dot">·</span>
                <UButton
                    size="xs"
                    variant="link"
                    :padded="false"
                    :disabled="run.isActive"
                    :loading="installing"
                    @click="restart"
                >
                    Restart
                </UButton>
            </template>

            <template v-else>
                <span class="text">NektoTranslate {{ update.version }} is available</span>

                <span v-if="percentLabel !== null" class="percent">{{ percentLabel }}</span>

                <UButton
                    v-if="isShell"
                    size="xs"
                    :disabled="update.state === 'downloading'"
                    :loading="update.state === 'downloading'"
                    @click="startUpdate"
                >
                    Update
                </UButton>

                <UButton
                    v-else-if="releaseUrl !== null"
                    size="xs"
                    variant="soft"
                    :to="releaseUrl"
                    target="_blank"
                    rel="noopener noreferrer"
                >
                    Download
                </UButton>

                <UButton
                    v-if="update.state === 'available'"
                    size="xs"
                    color="neutral"
                    variant="ghost"
                    @click="dismissLater"
                >
                    Later
                </UButton>
            </template>
        </div>

        <UProgress v-if="update.state === 'downloading'" class="track" size="2xs" :model-value="update.progress" :max="1" />
    </div>

    <UModal
        v-model:open="readyModalOpen"
        title="Update ready"
        :description="`NektoTranslate ${update.version} is ready. Restart now to use it, or let the next launch pick it up.`"
    >
        <template #body>
            <p v-if="run.isActive" class="reason">
                A translation is running - restarting would stop it.
            </p>

            <div class="actions">
                <UButton color="neutral" variant="ghost" :disabled="installing" @click="readyModalOpen = false">
                    Not now
                </UButton>

                <UButton :disabled="run.isActive" :loading="installing" @click="restart">
                    Restart now
                </UButton>
            </div>
        </template>
    </UModal>
</template>

<script setup lang="ts">
    import { computed, onMounted, onUnmounted, ref, watch } from "vue";

    import { useUpdateAvailability } from "@/composables/useSystem";
    import { desktopShell } from "@/desktop/shell";
    import { checkForUpdate, downloadUpdate, installUpdate } from "@/desktop/updates";
    import { useRunStore } from "@/stores/run.store";
    import { useUpdateStore } from "@/stores/update.store";
    import { describeFailure } from "@/utils/failure";
    import { notifyFailure } from "@/utils/notify";


    const SIX_HOURS_MS = 6 * 60 * 60 * 1000;

    const isShell = desktopShell !== null;

    const update = useUpdateStore();
    const run = useRunStore();

    // Browser only: the query itself is the six-hour poll the contract asks for on that side, and
    // its own `url` is read straight off it here rather than duplicated into the store, whose shape
    // is shared with the shell's own, very different multi-stage flow - the browser's "available"
    // never advances past a link to the release page.
    const browserUpdate = useUpdateAvailability();

    const releaseUrl = computed(() => browserUpdate.data.value?.url ?? null);

    watch(() => browserUpdate.data.value, (data) => {
        if (data !== undefined && data.isNewer && data.latest !== null) {
            update.offer(data.latest);
        }
    });

    // Shell only: update_check is a Tauri command, not an HTTP endpoint, so there is no query to
    // poll here - a plain interval does the same six-hour job the contract asks for.
    let pollHandle: ReturnType<typeof setInterval> | null = null;

    async function pollShell(): Promise<void> {
        const available = await checkForUpdate();

        if (available !== null) {
            update.offer(available.version);
        }
    }

    onMounted(() => {
        if (!isShell) {
            return;
        }

        void pollShell();
        pollHandle = setInterval(() => void pollShell(), SIX_HOURS_MS);
    });

    onUnmounted(() => {
        if (pollHandle !== null) {
            clearInterval(pollHandle);
        }
    });

    // *Later* was clicked - local to this component (always mounted for the life of the window)
    // rather than the store, since nothing else needs to know about it; a real restart is what
    // clears it, and a real restart is a fresh mount of everything.
    const laterDismissed = ref(false);

    // Whether the running download ever reported a content length - kept apart from the store's own
    // `progress` (always a plain 0..1 fraction) so the percentage label can tell "0%, just started"
    // from "unknown, do not show a number" without adding a field the contract does not ask for.
    const totalBytes = ref<number | null>(null);

    const installing = ref(false);

    const percentLabel = computed(() => {
        if (update.state !== "downloading" || totalBytes.value === null) {
            return null;
        }

        return `${Math.round(update.progress * 100)}%`;
    });

    const showBanner = computed(() => (
        !laterDismissed.value
        && (update.state === "available" || update.state === "downloading" || (update.state === "ready" && update.deferred))
    ));

    const readyModalOpen = computed({
        get: () => update.state === "ready" && !update.deferred,
        set: (open: boolean) => {
            if (!open) {
                update.defer();
            }
        },
    });


    async function startUpdate(): Promise<void> {
        update.startDownload();
        totalBytes.value = null;

        try {
            const result = await downloadUpdate((progress) => {
                totalBytes.value = progress.total;
                update.setProgress(progress.downloaded, progress.total);
            });

            if (result === null) {
                update.reset();

                return;
            }

            update.ready();
        }
        catch (error) {
            notifyFailure("Could not download the update", describeShellFailure(error));
            update.reset();
        }
    }


    // A successful install replaces this process (Windows) or triggers its own restart (Linux) from
    // inside installUpdate - there is nothing to reset back to on success, only on failure.
    async function restart(): Promise<void> {
        if (run.isActive || installing.value) {
            return;
        }

        installing.value = true;

        try {
            await installUpdate();
        }
        catch (error) {
            notifyFailure("Could not install the update", describeShellFailure(error));
            installing.value = false;
        }
    }


    function dismissLater(): void {
        laterDismissed.value = true;
    }


    // A rejected `invoke()` carries the plain string a Rust command's `Err(String)` returned, not
    // an `Error` - `describeFailure` was built for ofetch's own failures and falls back to a generic
    // sentence for anything else, which would otherwise swallow a perfectly good reason like "No
    // update is available to download".
    function describeShellFailure(error: unknown): string {
        return typeof error === "string" ? error : describeFailure(error);
    }
</script>

<style scoped lang="scss">
    .banner {
        position: relative;
        flex: none;
        border-bottom: 1px solid var(--ui-border);
        background: var(--ui-bg-elevated);

        .row {
            display: flex;
            align-items: center;
            gap: 0.75rem;
            height: 2.25rem;
            padding: 0 1rem;
        }

        .text {
            flex: 1;
            min-width: 0;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        .dot {
            color: var(--ui-text-muted);
        }

        .percent {
            flex: none;
            font-variant-numeric: tabular-nums;
            color: var(--ui-text-muted);
        }

        .track {
            position: absolute;
            inset: auto 0 0;
        }
    }

    .reason {
        margin: -0.5rem 0 0.75rem;
        font-size: var(--nt-text-sm);
        color: var(--ui-warning);
    }

    .actions {
        display: flex;
        justify-content: flex-end;
        gap: 0.5rem;
    }
</style>
