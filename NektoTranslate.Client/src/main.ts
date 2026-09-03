import ui from "@nuxt/ui/vue-plugin";
import { VueQueryPlugin } from "@tanstack/vue-query";
import { createPinia } from "pinia";
import piniaPluginPersistedstate from "pinia-plugin-persistedstate";
import { createApp } from "vue";

import App from "@/App.vue";
import router from "@/router";

import "@fontsource-variable/montserrat";

import "@/assets/css/tailwind.css";
import "@/assets/scss/main.scss";


const app = createApp(App);

const pinia = createPinia();
pinia.use(piniaPluginPersistedstate);

app.use(pinia);
app.use(router);
app.use(VueQueryPlugin);
app.use(ui);

app.mount("#app");
