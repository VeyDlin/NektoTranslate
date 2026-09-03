import type { UpdateSettingsRequest } from "@/types/api/requests";

import { useMutation, useQuery, useQueryClient } from "@tanstack/vue-query";
import { settingsApi } from "@/api";


export function settingsKey(): unknown[] {
    return ["settings"];
}


export function useSettings() {
    return useQuery({
        queryKey: settingsKey(),
        queryFn: () => settingsApi.get(),
    });
}


export function useUpdateSettings() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: (request: UpdateSettingsRequest) => settingsApi.update(request),
        onSuccess: (settings) => {
            queryClient.setQueryData(settingsKey(), settings);
        },
    });
}
