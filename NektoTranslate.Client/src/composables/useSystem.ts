import { useQuery } from "@tanstack/vue-query";
import { systemApi } from "@/api";


const SIX_HOURS_MS = 6 * 60 * 60 * 1000;


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


// The browser's own half of the update story - UpdateBanner.vue reads this in a plain browser tab,
// where there is no shell updater to ask instead (see src/desktop/updates.ts for that side). Quiet
// on failure for the same reason useHealth is: the endpoint itself never throws (`checked: false`
// is its own answer for "GitHub could not be reached"), so a query error here means something is
// wrong with this application's own server, not with the update check - not what a banner about a
// new version should be the one to report.
export function useUpdateAvailability() {
    return useQuery({
        queryKey: ["system", "update"],
        queryFn: () => systemApi.update(),
        staleTime: SIX_HOURS_MS,
        refetchInterval: SIX_HOURS_MS,
        meta: { quiet: true },
    });
}
