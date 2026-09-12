import type { HealthStatus, UpdateAvailability } from "@/types/models/domain";

import { apiClient } from "./client";


export const systemApi = {
    health(): Promise<HealthStatus> {
        return apiClient<HealthStatus>("/api/health");
    },

    update(): Promise<UpdateAvailability> {
        return apiClient<UpdateAvailability>("/api/system/update");
    },
};
