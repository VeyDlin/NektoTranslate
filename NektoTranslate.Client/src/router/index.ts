import type { RouteRecordRaw } from "vue-router";
import { createRouter, createWebHistory } from "vue-router";

import LibraryView from "@/views/LibraryView.vue";


const routes: RouteRecordRaw[] = [
    {
        path: "/",
        name: "library",
        component: LibraryView,
    },
    {
        path: "/novels/:novelId",
        name: "novel",
        component: () => import("@/views/NovelView.vue"),
        props: true,
    },
    {
        path: "/novels/:novelId/import",
        name: "import-chapter",
        component: () => import("@/views/ImportChapterView.vue"),
        props: true,
    },
    {
        path: "/novels/:novelId/import-url",
        name: "import-from-url",
        component: () => import("@/views/ImportFromUrlView.vue"),
        props: true,
    },
    {
        path: "/novels/:novelId/import-translation",
        name: "import-translation",
        component: () => import("@/views/ImportTranslationView.vue"),
        props: true,
    },
    {
        path: "/novels/:novelId/alignment",
        name: "alignment",
        component: () => import("@/views/AlignmentView.vue"),
        props: true,
    },
    {
        path: "/novels/:novelId/settings",
        name: "novel-settings",
        component: () => import("@/views/NovelSettingsView.vue"),
        props: true,
    },
    {
        path: "/settings",
        name: "settings",
        component: () => import("@/views/SettingsView.vue"),
    },
    {
        path: "/parsers",
        name: "parsers",
        component: () => import("@/views/ParsersView.vue"),
    },
    {
        path: "/novels/:novelId/chapters/:chapterId",
        name: "reader",
        component: () => import("@/views/ReaderView.vue"),
        props: true,
    },
    {
        path: "/:pathMatch(.*)*",
        redirect: "/",
    },
];


export default createRouter({
    history: createWebHistory(),
    routes,
    scrollBehavior() {
        return { top: 0 };
    },
});
