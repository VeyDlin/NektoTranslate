import type { Ref } from "vue";
import type { UpdateSettingsRequest } from "@/types/api/requests";

import { useMutation, useQuery, useQueryClient } from "@tanstack/vue-query";
import { settingsApi } from "@/api";


export function settingsKey(): unknown[] {
    return ["settings"];
}


export function modelsKey(): unknown[] {
    return ["settings", "models"];
}


export function localModelsKey(): unknown[] {
    return ["settings", "models", "local"];
}


export function useSettings() {
    return useQuery({
        queryKey: settingsKey(),
        queryFn: () => settingsApi.get(),
    });
}


// The catalogue does not change while the application runs, so it is fetched once and kept.
export function useModels() {
    return useQuery({
        queryKey: modelsKey(),
        queryFn: () => settingsApi.models(),
        staleTime: Infinity,
    });
}


// Not fetched on mount: it reaches out to a server that is usually not running, and a failed
// request on every visit to the settings screen is noise. `enabled` is driven by the caller, so the
// refresh button is what triggers it.
export function useLocalModels(enabled: Ref<boolean>) {
    return useQuery({
        queryKey: localModelsKey(),
        queryFn: () => settingsApi.localModels(),
        enabled,
        retry: false,
    });
}


// A deliberate action rather than a query: it spends money when the model is valid, so nothing may
// call it automatically.
export function useProbeModel() {
    return useMutation({
        mutationFn: (model: string) => settingsApi.probe(model),
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
