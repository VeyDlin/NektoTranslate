import type { Ref } from "vue";
import type { MoveTranslationsRequest } from "@/types/api/requests";
import type { MoveTranslationsResult } from "@/types/models/domain";

import { useMutation, useQuery, useQueryClient } from "@tanstack/vue-query";
import { translationsApi } from "@/api";


// The language is passed as a ref rather than a value so vue-query refetches when it changes — the
// alignment of one language says nothing about another.
export function useAlignment(novelId: number, language: Ref<string>) {
    return useQuery({
        queryKey: ["novels", novelId, "alignment", language],
        queryFn: () => translationsApi.alignment(novelId, language.value),
        enabled: () => language.value !== "",
    });
}


// A refused move is a normal answer, not a failure.
//
// The server replies 409 with the list of what is in the way, and ofetch turns any non-2xx into a
// thrown error. Left as a throw, the collision list — the only thing that tells the user what to
// fix — would end up in an error handler instead of on the screen. So the body is read back off the
// error and returned like any other result; a real transport failure still throws.
export function useMoveTranslations(novelId: number) {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: async (request: MoveTranslationsRequest): Promise<MoveTranslationsResult> => {
            try {
                return await translationsApi.move(novelId, request);
            }
            catch (failure) {
                const body = (failure as { data?: MoveTranslationsResult }).data;

                if (body && Array.isArray(body.collisions)) {
                    return body;
                }

                throw failure;
            }
        },

        onSuccess: (result) => {
            // Only a move that actually landed changes anything. A dry run and a refusal both leave
            // the book exactly as it was, and refetching after them would be pure noise.
            if (result.applied) {
                void queryClient.invalidateQueries({ queryKey: ["novels", novelId] });
            }
        },
    });
}


export function useDeleteTranslations(novelId: number) {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: (input: { language: string; from: number; to: number }) =>
            translationsApi.deleteRange(novelId, input.language, input.from, input.to),

        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ["novels", novelId] });
        },
    });
}
