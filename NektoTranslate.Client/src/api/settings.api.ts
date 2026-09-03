import type { UpdateSettingsRequest } from "@/types/api/requests";
import type { AppSettings } from "@/types/models/domain";

import { apiClient } from "./client";


export const settingsApi = {
    get(): Promise<AppSettings> {
        return apiClient<AppSettings>("/api/settings");
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
