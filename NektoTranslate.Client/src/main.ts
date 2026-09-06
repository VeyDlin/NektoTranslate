import ui from "@nuxt/ui/vue-plugin";
import { MutationCache, QueryCache, QueryClient, VueQueryPlugin } from "@tanstack/vue-query";
import { createPinia } from "pinia";
import piniaPluginPersistedstate from "pinia-plugin-persistedstate";
import { createApp } from "vue";

import App from "@/App.vue";
import router from "@/router";
import { describeFailure } from "@/utils/failure";
import { notifyFailure } from "@/utils/notify";

import "@fontsource-variable/montserrat";

import "@/assets/css/tailwind.css";
import "@/assets/scss/main.scss";


const app = createApp(App);

const pinia = createPinia();
pinia.use(piniaPluginPersistedstate);

// Every request that fails says so, here, once, for the whole application. A screen does not get to
// swallow a failure by forgetting to handle it: a mutation that throws leaves its dialog open with
// the reason on screen, and a query that cannot load says what it could not load. A caller that
// shows the failure in its own words - a dialog with a line reserved for it - marks its mutation
// `meta: { quiet: true }` and is left alone, so the same failure is never said twice.
const queryClient = new QueryClient({
    mutationCache: new MutationCache({
        // Only the first and the last of the four arguments matter here; the holes skip the
        // variables and the context without naming something that is never read.
        onError: (...args) => {
            const [error, , , mutation] = args;

            if (mutation.meta?.quiet === true) {
                return;
            }

            notifyFailure("That did not go through", describeFailure(error));
        },
    }),
    queryCache: new QueryCache({
        onError: (error, query) => {
            if (query.meta?.quiet === true) {
                return;
            }

            notifyFailure("Could not load this", describeFailure(error));
        },
    }),
});

app.use(pinia);
app.use(router);
app.use(VueQueryPlugin, { queryClient });
app.use(ui);

app.mount("#app");
