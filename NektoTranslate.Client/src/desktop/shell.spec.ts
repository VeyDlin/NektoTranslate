// @vitest-environment jsdom
//
// shell.ts reads `window` once at module load - parsing the desktop global is what is under test
// here, so the module has to load somewhere `window` exists at all, unlike the rest of this
// project's specs, which stay in the default node environment.
import { describe, expect, it } from "vitest";
import { barLayoutFor, parseDesktopShell } from "./shell";


describe("parseDesktopShell", () => {
    it("accepts a well-formed object for each known platform", () => {
        expect(parseDesktopShell({ platform: "windows", version: "0.1.0" }))
            .toEqual({ platform: "windows", version: "0.1.0" });
        expect(parseDesktopShell({ platform: "macos", version: "1.2.3" }))
            .toEqual({ platform: "macos", version: "1.2.3" });
        expect(parseDesktopShell({ platform: "linux", version: "2.0.0" }))
            .toEqual({ platform: "linux", version: "2.0.0" });
    });

    it("is null when the global was never set - an ordinary browser tab", () => {
        expect(parseDesktopShell(undefined)).toBeNull();
    });

    it("is null for anything that is not an object", () => {
        expect(parseDesktopShell(null)).toBeNull();
        expect(parseDesktopShell("windows")).toBeNull();
        expect(parseDesktopShell(42)).toBeNull();
    });

    it("is null when the platform is missing or not one of the three known ones", () => {
        expect(parseDesktopShell({ version: "0.1.0" })).toBeNull();
        expect(parseDesktopShell({ platform: "android", version: "0.1.0" })).toBeNull();
        expect(parseDesktopShell({ platform: 1, version: "0.1.0" })).toBeNull();
    });

    it("is null when the version is missing or not a string", () => {
        expect(parseDesktopShell({ platform: "windows" })).toBeNull();
        expect(parseDesktopShell({ platform: "windows", version: 1 })).toBeNull();
    });

    it("ignores extra fields rather than rejecting them", () => {
        expect(parseDesktopShell({ platform: "windows", version: "0.1.0", extra: true }))
            .toEqual({ platform: "windows", version: "0.1.0" });
    });
});


describe("barLayoutFor", () => {
    it("gives Windows the Fluent glyph buttons and no traffic-light inset", () => {
        expect(barLayoutFor("windows")).toEqual({ controlStyle: "fluent", macInset: false });
    });

    it("gives Linux the round buttons and no traffic-light inset", () => {
        expect(barLayoutFor("linux")).toEqual({ controlStyle: "round", macInset: false });
    });

    it("gives macOS no buttons of its own and the traffic-light inset", () => {
        expect(barLayoutFor("macos")).toEqual({ controlStyle: "none", macInset: true });
    });
});
