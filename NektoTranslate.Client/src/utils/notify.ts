// The one place the application speaks up unprompted.
//
// Nuxt UI's `useToast` needs a component to be called from, and the places that most need to notify
// - the query client's global error hooks, wired up in main.ts before any component exists - are
// not components. So App.vue hands its toaster over once, and everything else goes through here.
// Until it does, a notification is a console line rather than a lost message.
//
// Two rules a toast has to follow to be worth having. It says what went wrong in the server's own
// words when there are any, so "Could not start the run" always comes with the reason under it. And
// the same failure is said once: a screen whose every query fails because the server is down would
// otherwise stack five identical toasts, which reads as five things broken rather than one.
interface Toaster {
    add: (toast: { title: string; description?: string; color?: "error" | "success" | "primary"; icon?: string }) => unknown;
}


const RepeatWindowMs = 4000;

let toaster: Toaster | null = null;

const lastSaid = new Map<string, number>();


export function registerToaster(instance: Toaster): void {
    toaster = instance;
}


export function notifyFailure(title: string, description: string): void {
    say({ title, description, color: "error", icon: "i-material-symbols:error-outline-rounded" });
}


export function notifySuccess(title: string, description?: string): void {
    say({ title, description, color: "success", icon: "i-material-symbols:check-circle-outline-rounded" });
}


function say(toast: { title: string; description?: string; color: "error" | "success"; icon: string }): void {
    const key = `${toast.title}\n${toast.description ?? ""}`;
    const now = Date.now();
    const previous = lastSaid.get(key);

    if (previous !== undefined && now - previous < RepeatWindowMs) {
        return;
    }

    lastSaid.set(key, now);

    if (toaster === null) {
        console.error(`${toast.title}: ${toast.description ?? ""}`);

        return;
    }

    toaster.add(toast);
}
