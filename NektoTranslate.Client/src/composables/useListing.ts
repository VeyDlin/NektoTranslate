import type { MaybeRefOrGetter } from "vue";
import type { ImportKind } from "@/types/models/domain";
import { useMutation, useQuery, useQueryClient } from "@tanstack/vue-query";

import { computed, toValue } from "vue";
import { listingsApi } from "@/api";


export function listingKey(novelId: number, kind: ImportKind): unknown[] {
    return ["novels", novelId, "listings", kind];
}


// The contents of a site as read for this book. Server-owned on purpose: a page load behind a
// one-tab-per-site queue is too expensive to lose to a navigation, and too slow to leave without a
// visible state, so both the entries and the state of the read live where a reopened screen can find
// them.
export function useListing(novelId: MaybeRefOrGetter<number>, kind: ImportKind) {
    return useQuery({
        queryKey: computed(() => listingKey(toValue(novelId), kind)),
        queryFn: () => listingsApi.get(toValue(novelId), kind),
    });
}


export function useReadListing(novelId: MaybeRefOrGetter<number>, kind: ImportKind) {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: (url: string) => listingsApi.read(toValue(novelId), kind, url),
        // The response is the listing in its Reading state, so the screen shows the read starting
        // without waiting for the first hub event.
        onSuccess: (listing) => {
            queryClient.setQueryData(listingKey(toValue(novelId), kind), listing);
        },
    });
}


export function useCancelListing(novelId: MaybeRefOrGetter<number>, kind: ImportKind) {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: () => listingsApi.cancel(toValue(novelId), kind),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: listingKey(toValue(novelId), kind) });
        },
    });
}
