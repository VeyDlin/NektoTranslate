import { useQuery } from "@tanstack/vue-query";
import { systemApi } from "@/api";


// The build never changes while the application runs, so this is fetched once and kept - and quiet
// on failure, since the version badge next to the wordmark simply stays blank rather than putting a
// toast in front of a screen nobody asked to see a health check for.
export function useHealth() {
    return useQuery({
        queryKey: ["health"],
        queryFn: () => systemApi.health(),
        staleTime: Infinity,
        meta: { quiet: true },
    });
}
