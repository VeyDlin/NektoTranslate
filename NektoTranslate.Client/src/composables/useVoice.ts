import type { MaybeRefOrGetter } from "vue";
import { useMutation, useQuery, useQueryClient } from "@tanstack/vue-query";

import { computed, toValue } from "vue";
import { voiceApi } from "@/api";


export function voiceProfileKey(novelId: number, language: string): unknown[] {
    return ["novels", novelId, "voice", language];
}


export function voiceTermsKey(novelId: number, language: string): unknown[] {
    return ["novels", novelId, "terms", language];
}


export function useVoiceProfile(novelId: MaybeRefOrGetter<number | null>, language: MaybeRefOrGetter<string>) {
    return useQuery({
        queryKey: computed(() => voiceProfileKey(toValue(novelId) as number, toValue(language))),
        queryFn: () => voiceApi.profile(toValue(novelId) as number, toValue(language)),
        enabled: computed(() => toValue(novelId) !== null && toValue(language) !== ""),
    });
}


// The whole list, fetched once and sorted in the browser — the same call GlossaryList makes for the
// same reason: even a long book's term list is a few hundred rows, and the sort itself is what "how
// often they occur" means, not something worth a query parameter.
export function useVoiceTerms(novelId: MaybeRefOrGetter<number | null>, language: MaybeRefOrGetter<string>) {
    return useQuery({
        queryKey: computed(() => voiceTermsKey(toValue(novelId) as number, toValue(language))),
        queryFn: () => voiceApi.terms(toValue(novelId) as number, toValue(language)),
        enabled: computed(() => toValue(novelId) !== null && toValue(language) !== ""),
    });
}


export function useDeleteTerm(novelId: MaybeRefOrGetter<number>, language: MaybeRefOrGetter<string>) {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: (termId: number) => voiceApi.removeTerm(toValue(novelId), termId),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: voiceTermsKey(toValue(novelId), toValue(language)) });
        },
    });
}
