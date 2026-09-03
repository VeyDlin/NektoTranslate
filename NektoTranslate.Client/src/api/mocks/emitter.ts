import type { TranslationEventName, TranslationEvents } from "@/realtime/events";


type Handler = (payload: never) => void;


// The mock backend and the mock stream are two halves of one fake server, so they share this
// channel: the run simulator publishes, the stream subscribes. Scoped by novel to match the
// server's one-group-per-novel behaviour — watching one book must not deliver another's traffic.
class MockEmitter {

    private handlers = new Map<string, Set<Handler>>();


    public on<K extends TranslationEventName>(
        novelId: number,
        event: K,
        handler: (payload: TranslationEvents[K]) => void,
    ): () => void {
        const key = `${novelId}:${event}`;
        const set = this.handlers.get(key) ?? new Set<Handler>();

        set.add(handler as Handler);
        this.handlers.set(key, set);

        return () => {
            set.delete(handler as Handler);
        };
    }


    public emit<K extends TranslationEventName>(novelId: number, event: K, payload: TranslationEvents[K]): void {
        const set = this.handlers.get(`${novelId}:${event}`);

        if (set === undefined) {
            return;
        }

        for (const handler of set) {
            (handler as (value: TranslationEvents[K]) => void)(payload);
        }
    }
}


export const mockEmitter = new MockEmitter();
