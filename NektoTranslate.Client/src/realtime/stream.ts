import type { TranslationEventName, TranslationEvents, TranslationStream } from "./events";

import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { USE_MOCKS } from "@/api/client";
import { mockEmitter } from "@/api/mocks/emitter";


function createMockStream(): TranslationStream {
    let watched: number | null = null;
    const unsubscribes: (() => void)[] = [];

    // Handlers register before the novel is known, so each one is kept as a closure that subscribes
    // when a novel arrives. Storing the event name and handler as a pair instead loses the link
    // between them the moment the name widens to a union.
    const subscribers: ((novelId: number) => () => void)[] = [];

    function release(): void {
        for (const unsubscribe of unsubscribes) {
            unsubscribe();
        }

        unsubscribes.length = 0;
        watched = null;
    }

    return {
        async watch(novelId: number): Promise<void> {
            watched = novelId;

            for (const subscribe of subscribers) {
                unsubscribes.push(subscribe(novelId));
            }
        },

        async unwatch(): Promise<void> {
            release();
        },

        on<K extends TranslationEventName>(event: K, handler: (payload: TranslationEvents[K]) => void): () => void {
            const subscribe = (novelId: number): (() => void) => mockEmitter.on(novelId, event, handler);

            subscribers.push(subscribe);

            if (watched !== null) {
                const unsubscribe = subscribe(watched);
                unsubscribes.push(unsubscribe);

                return unsubscribe;
            }

            return () => {
                const at = subscribers.indexOf(subscribe);

                if (at >= 0) {
                    subscribers.splice(at, 1);
                }
            };
        },

        async dispose(): Promise<void> {
            release();
            subscribers.length = 0;
        },
    };
}


function createHubStream(): TranslationStream {
    const connection = new HubConnectionBuilder()
        // Relative, so it follows whatever origin the page is on: the dev proxy while developing,
        // the server's own wwwroot in production. Nothing here needs to know the port.
        .withUrl("/hubs/translation")
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Warning)
        .build();

    let started: Promise<void> | null = null;
    let watched: number | null = null;

    function ensureStarted(): Promise<void> {
        started ??= connection.start();

        return started;
    }

    // The hub keeps one group per novel, and a reconnect drops group membership. Rejoining on
    // reconnect is what stops a run going silent after a blip rather than after a failure.
    connection.onreconnected(() => {
        if (watched !== null) {
            void connection.invoke("Watch", watched);
        }
    });

    return {
        async watch(novelId: number): Promise<void> {
            await ensureStarted();
            watched = novelId;
            await connection.invoke("Watch", novelId);
        },

        async unwatch(novelId: number): Promise<void> {
            if (connection.state !== "Connected") {
                watched = null;
                return;
            }

            watched = null;
            await connection.invoke("Unwatch", novelId);
        },

        on<K extends TranslationEventName>(event: K, handler: (payload: TranslationEvents[K]) => void): () => void {
            connection.on(event, handler as (payload: unknown) => void);

            return () => connection.off(event, handler as (payload: unknown) => void);
        },

        async dispose(): Promise<void> {
            watched = null;
            started = null;
            await connection.stop();
        },
    };
}


export function createTranslationStream(): TranslationStream {
    return USE_MOCKS ? createMockStream() : createHubStream();
}
