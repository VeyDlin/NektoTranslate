import type { HealthStatus } from "@/types/models/domain";

import { apiClient } from "./client";


export const systemApi = {
    health(): Promise<HealthStatus> {
        return apiClient<HealthStatus>("/api/health");
    },
};
