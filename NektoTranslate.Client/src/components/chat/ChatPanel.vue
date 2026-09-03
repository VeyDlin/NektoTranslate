<template>
    <div class="chat">
        <div ref="scroller" class="transcript">
            <p v-if="messages.length === 0" class="blank">
                Corrections are contextual, so this is one conversation about this book rather than a
                set of forms. Say what is wrong with a name or a tone and the agent updates the
                glossary and the style guide.
            </p>

            <ol v-else class="turns">
                <li v-for="message in messages" :key="message.id" class="turn" :class="message.role.toLowerCase()">
                    <span class="who">{{ roleLabel(message.role) }}</span>

                    <p class="text">{{ message.text }}</p>

                    <span v-if="message.costUsd > 0" class="cost">{{ formatCost(message.costUsd) }}</span>
                </li>
            </ol>

            <p v-if="isSending" class="thinking">Working…</p>
        </div>

        <form class="composer" @submit.prevent="send">
            <UTextarea
                v-model="draft"
                :rows="2"
                autoresize
                placeholder="This name is wrong, from now on call him…"
                class="field"
                @keydown.enter.exact.prevent="send"
            />

            <UButton type="submit" :loading="isSending" :disabled="draft.trim() === ''">
                Send
            </UButton>
        </form>
    </div>
</template>

<script setup lang="ts">
    import type { ChatRole } from "@/types/models/domain";

    import { useMutation, useQuery, useQueryClient } from "@tanstack/vue-query";
    import { computed, nextTick, ref, watch } from "vue";
    import { chatApi } from "@/api";
    import { formatCost } from "@/utils/format";


    const props = defineProps<{ novelId: number }>();

    const queryClient = useQueryClient();
    const draft = ref("");
    const scroller = ref<HTMLElement | null>(null);

    const chatKey = computed(() => ["novels", props.novelId, "chat"]);

    const { data } = useQuery({
        queryKey: chatKey,
        queryFn: () => chatApi.history(props.novelId),
    });

    const messages = computed(() => data.value ?? []);

    const { mutateAsync, isPending: isSending } = useMutation({
        mutationFn: (text: string) => chatApi.send(props.novelId, text),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: chatKey.value });
        },
    });


    // Tool turns are labelled rather than hidden. Seeing that the agent ran glossary_record is what
    // makes its answer checkable instead of something to take on trust.
    function roleLabel(role: ChatRole): string {
        switch (role) {
            case "User":
                return "You";
            case "Tool":
                return "Ran a tool";
            default:
                return "Agent";
        }
    }


    async function send(): Promise<void> {
        const text = draft.value.trim();

        if (text === "" || isSending.value) {
            return;
        }

        draft.value = "";
        await mutateAsync(text);
    }


    watch(messages, async () => {
        await nextTick();

        if (scroller.value !== null) {
            scroller.value.scrollTop = scroller.value.scrollHeight;
        }
    });
</script>

<style scoped lang="scss">
    @use "@/assets/scss/variables" as *;

    .chat {
        display: flex;
        flex-direction: column;
        min-height: 0;
        flex: 1;

        .transcript {
            flex: 1;
            min-height: 0;
            overflow-y: auto;
            padding: 1.5rem;
        }

        .blank {
            max-width: $reading-measure-comfortable;
            margin: 2rem auto;
            line-height: 1.6;
            color: var(--ui-text-muted);
        }

        .turns {
            max-width: $reading-measure-wide;
            margin: 0 auto;
            padding: 0;
            list-style: none;
        }

        .turn {
            display: grid;
            grid-template-columns: 7rem 1fr auto;
            gap: 1rem;
            padding: 0.75rem 0;
            border-bottom: 1px solid var(--ui-border);

            .who {
                color: var(--ui-text-dimmed);
            }

            .text {
                margin: 0;
                line-height: 1.6;
                color: var(--ui-text);
                white-space: pre-wrap;
            }

            .cost {
                color: var(--ui-text-dimmed);
            }

            &.user .text {
                color: var(--ui-text-highlighted);
            }

            // A tool turn is a note about what happened, not something anyone said. It is set apart
            // so the eye can skip it while reading the conversation and come back when checking.
            &.tool {
                .who,
                .text {
                    font-family: ui-monospace, "Cascadia Mono", "Consolas", monospace;
                    font-size: var(--nt-text-sm);
                    color: var(--ui-text-muted);
                }
            }
        }

        .thinking {
            max-width: $reading-measure-wide;
            margin: 0.75rem auto;
            color: var(--ui-text-muted);
        }

        .composer {
            flex: none;
            display: flex;
            align-items: flex-end;
            gap: 0.75rem;
            padding: 0.75rem 1.5rem;
            border-top: 1px solid var(--ui-border);

            .field {
                flex: 1;
                min-width: 0;
            }
        }
    }
</style>
