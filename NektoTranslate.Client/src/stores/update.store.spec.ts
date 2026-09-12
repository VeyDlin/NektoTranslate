import { createPinia, setActivePinia } from "pinia";
import { beforeEach, describe, expect, it } from "vitest";
import { useUpdateStore } from "./update.store";


describe("useUpdateStore", () => {
    beforeEach(() => {
        setActivePinia(createPinia());
    });


    it("starts idle with nothing offered", () => {
        const update = useUpdateStore();

        expect(update.state).toBe("idle");
        expect(update.version).toBeNull();
    });


    it("offer moves idle to available and records the version", () => {
        const update = useUpdateStore();

        update.offer("0.2.1");

        expect(update.state).toBe("available");
        expect(update.version).toBe("0.2.1");
        expect(update.deferred).toBe(false);
    });


    it("offer is a no-op once a download or install is already under way", () => {
        const update = useUpdateStore();

        update.offer("0.2.1");
        update.startDownload();
        update.offer("0.2.2");

        expect(update.state).toBe("downloading");
        expect(update.version).toBe("0.2.1");
    });


    it("setProgress is the downloaded fraction of total when total is known", () => {
        const update = useUpdateStore();

        update.offer("0.2.1");
        update.startDownload();
        update.setProgress(50, 200);

        expect(update.progress).toBeCloseTo(0.25);
    });


    it("setProgress leaves progress unchanged when total is not known yet", () => {
        const update = useUpdateStore();

        update.offer("0.2.1");
        update.startDownload();
        update.setProgress(50, 200);
        update.setProgress(80, null);

        expect(update.progress).toBeCloseTo(0.25);
    });


    it("setProgress never exceeds 1 even if downloaded overshoots total", () => {
        const update = useUpdateStore();

        update.offer("0.2.1");
        update.startDownload();
        update.setProgress(500, 200);

        expect(update.progress).toBe(1);
    });


    it("ready sets state to ready and progress to 1", () => {
        const update = useUpdateStore();

        update.offer("0.2.1");
        update.startDownload();
        update.ready();

        expect(update.state).toBe("ready");
        expect(update.progress).toBe(1);
    });


    it("defer marks the ready dialog as dismissed for the rest of the session", () => {
        const update = useUpdateStore();

        update.offer("0.2.1");
        update.startDownload();
        update.ready();
        update.defer();

        expect(update.deferred).toBe(true);
        expect(update.state).toBe("ready");
    });


    it("reset returns to idle with nothing offered", () => {
        const update = useUpdateStore();

        update.offer("0.2.1");
        update.startDownload();
        update.reset();

        expect(update.state).toBe("idle");
        expect(update.version).toBeNull();
        expect(update.progress).toBe(0);
        expect(update.deferred).toBe(false);
    });


    it("a fresh offer after reset is accepted again", () => {
        const update = useUpdateStore();

        update.offer("0.2.1");
        update.startDownload();
        update.reset();
        update.offer("0.2.2");

        expect(update.state).toBe("available");
        expect(update.version).toBe("0.2.2");
    });
});
