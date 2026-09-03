import type { UpdateSettingsRequest } from "@/types/api/requests";
import type { AppSettings, LocalModelList, ModelOption, ModelProbeResult } from "@/types/models/domain";

import { apiClient } from "./client";


export const settingsApi = {
    get(): Promise<AppSettings> {
        return apiClient<AppSettings>("/api/settings");
    },


    // The model dropdown's contents. Free and immediate — safe to call on page load.
    models(): Promise<ModelOption[]> {
        return apiClient<ModelOption[]>("/api/settings/models");
    },


    // Asks the CLI whether this subscription can run the model, the only way it can be asked: by
    // using it. A rejected name costs nothing, a valid one costs a trivial turn — so this belongs
    // behind an explicit button, never on page load or in a loop.
    probe(model: string): Promise<ModelProbeResult> {
        return apiClient<ModelProbeResult>(
            `/api/settings/models/${encodeURIComponent(model)}/probe`,
            { method: "POST" },
        );
    },


    // What the configured local server reports it has loaded. Answers `reachable: false` with a
    // reason rather than failing when nothing is running, which is the ordinary case.
    localModels(): Promise<LocalModelList> {
        return apiClient<LocalModelList>("/api/settings/models/local");
    },

    // Only the fields being changed are sent: null means "leave alone", and an empty string is what
    // clears a nullable one. Sending the whole object back would silently overwrite anything another
    // screen had changed in the meantime.
    update(request: UpdateSettingsRequest): Promise<AppSettings> {
        return apiClient<AppSettings>("/api/settings", {
            method: "PUT",
            body: request,
        });
    },
};
