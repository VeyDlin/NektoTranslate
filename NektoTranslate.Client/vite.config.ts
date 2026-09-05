import { fileURLToPath, URL } from "node:url";

import ui from "@nuxt/ui/vite";
import vue from "@vitejs/plugin-vue";
import { defineConfig } from "vite";


// Where the dev server sends /api and /hubs. The default is the application's own port; the
// override exists so a second server - a scratch build on another port, against a scratch
// library - can be driven from a second dev server without editing this file under the first.
const apiTarget: string = process.env.NEKTO_API ?? "http://127.0.0.1:5080";


export default defineConfig({
    plugins: [
        vue(),
        ui({
            // Without this, icons are fetched from the Iconify API at runtime — which for an
            // application that runs on one machine, on loopback, with no network assumption means
            // the interface loses its icons the moment it is offline. Scanning bundles every icon
            // named as a literal string in the source; the collections themselves are installed as
            // @iconify-json/* so there is nothing left to fetch. Nuxt UI's own defaults come from
            // lucide, which is why that collection is a dependency even though no screen names it.
            icon: {
                clientBundle: {
                    scan: true,
                },
            },
            ui: {
                // The semantic aliases are the application's state vocabulary. `sage`, `brick` and
                // `ochre` are defined as full ramps in assets/css/tailwind.css; indigo and zinc are
                // Tailwind's own.
                colors: {
                    primary: "steel",
                    neutral: "neutral",
                    success: "sage",
                    error: "brick",
                    warning: "ochre",
                },

                // The library ships `data-disabled:cursor-not-allowed` on these slots but leaves the
                // enabled state alone, so a list item that is plainly clickable shows the text
                // cursor. Set on the slot rather than through a global `[role=...]` rule so the
                // classes merge with the component's own instead of racing them by specificity.
                select: {
                    slots: { item: "cursor-pointer" },
                },
                selectMenu: {
                    slots: { item: "cursor-pointer" },
                },
                dropdownMenu: {
                    slots: { item: "cursor-pointer" },
                },
                tabs: {
                    slots: { trigger: "cursor-pointer" },
                },
                checkbox: {
                    slots: { base: "cursor-pointer", label: "cursor-pointer" },
                },
                radioGroup: {
                    slots: { item: "cursor-pointer", label: "cursor-pointer" },
                },

                // The library sizes text controls to their content — the root slot is `inline-flex`,
                // so a field in a form column only occupies its placeholder's width. Every text
                // control in this application lives in a form or a search row that wants the full
                // measure, so the width belongs in the theme rather than repeated at each call site.
                // The three inline usages (`.search`, `.url`) set `flex: 1; min-width: 0`, which
                // takes precedence over the percentage width.
                //
                // Two components are left out on purpose. `select`, because the one in the reader bar
                // has to stay as wide as the translation it names; and `inputNumber`, whose decrement
                // and increment sit at opposite ends of the field — stretched across a form column
                // they end up a whole modal apart, which is worse than a field narrower than its
                // neighbours.
                input: {
                    slots: { root: "w-full" },
                },
                textarea: {
                    slots: { root: "w-full" },
                },
            },
        }),
    ],
    resolve: {
        alias: {
            "@": fileURLToPath(new URL("./src", import.meta.url)),
        },
    },
    server: {
        port: 5173,

        // The server binds to loopback and configures no CORS, by design — in production the built
        // frontend is served from its own wwwroot, so there is no cross-origin call to allow. The
        // dev server proxies instead of asking for CORS, which keeps development on the same origin
        // as production rather than on a looser one.
        proxy: {
            "/api": {
                target: apiTarget,
                changeOrigin: true,
            },
            "/hubs": {
                target: apiTarget,
                changeOrigin: true,
                ws: true,
            },
        },
    },
});
