# Vendored third-party source

Source we compile ourselves instead of consuming as a NuGet package, because it carries a local
patch. Everything here is upstream code under its own licence — treat it as read-only apart from
the recorded patches.

## ClaudeCodeSdk

- Upstream: https://github.com/zxyao145/claude-code-sdk-csharp
- Licence: MIT (`ClaudeCodeSdk/LICENSE.txt`)
- Vendored from commit `af1be8545d1885fad0f5a85965d8c35e13378a0d` (2026-09-01), which is what
  NuGet published as `ClaudeCodeSdk` 10.4.0
- Contents: upstream `src/ClaudeCodeSdk/` verbatim, plus the licence and the patch below

### Why it is vendored

Two defects, both in how `ClaudeProcess` launches the CLI, both general rather than specific to
this project. **A re-sync must re-apply both.**

## Patch 1 — stdin is written in the console's encoding

`ClaudeProcess.BuildStartInfo` sets `StandardOutputEncoding` and `StandardErrorEncoding` to
UTF-8 but leaves `StandardInputEncoding` unset. The prompt is written to the CLI's stdin as JSON,
so its encoding falls back to `Console.InputEncoding`. In a host with no console attached — a
test runner, a Windows service, any GUI app — `GetConsoleCP()` returns 0 and .NET falls back to
the ANSI code page, so every non-ASCII character in the prompt reaches Claude as `?`. Japanese
source text arrives as a row of question marks and the model answers that it received nothing to
translate. The corruption is silent and depends on how the process was launched.

`ClaudeTranslatorLiveTests` pins the behaviour. Run it with `NEKTOTRANSLATE_LIVE_TESTS=1`.

One line, in `ClaudeCodeSdk/ClaudeProcess.cs` -> `BuildStartInfo`:

```diff
             UseShellExecute = false,
             CreateNoWindow = true,
+            StandardInputEncoding = Encoding.UTF8,
             StandardOutputEncoding = Encoding.UTF8,
             StandardErrorEncoding = Encoding.UTF8,
```

It is not specific to this project. The CLI reads stdin as UTF-8 JSON, the same pipe pair is
already declared UTF-8 in both other directions, and ASCII is a subset of UTF-8 — so no caller
that works today can regress.

## Patch 2 — command-line arguments are never quoted

`CommandUtil.BuildCommand` appends option values verbatim and `ClaudeProcess` joins the whole list
with spaces into a single `Arguments` string. Any value containing a space or a newline is then
re-split by the receiving process, so a multi-word `--system-prompt` arrived as its first word and
the remainder became stray positional arguments. Working directories, models and appended prompts
are affected the same way.

This is easy to miss because it half-works: fragments of the lost prompt land in the request text,
and a short instruction still steers the model by accident. It only becomes visible with a long
multi-line system prompt, at which point no instruction survives at all and the model answers the
input as if it had been asked nothing.

The fix is to hand the arguments to `ProcessStartInfo.ArgumentList`, which quotes each entry for
the platform, instead of building one string:

```diff
-        var argsString = string.Join(" ", args);
-        _process = new Process { StartInfo = BuildStartInfo(_cliPath, argsString) };
+        _process = new Process { StartInfo = BuildStartInfo(_cliPath, args) };
```

with `BuildStartInfo` taking `IReadOnlyList<string>`, dropping `Arguments = arguments`, and adding
each entry to `startInfo.ArgumentList`. See `ClaudeProcess.cs` for the applied form.

## Upstream

Patch 1 is submitted as https://github.com/zxyao145/claude-code-sdk-csharp/pull/34, which is the
canonical record of the change.

### Going back to the package

Once the fix is released upstream:

1. Delete `vendor/ClaudeCodeSdk/`.
2. In `NektoTranslate.Core.csproj`, replace the `ProjectReference` with
   `<PackageReference Include="ClaudeCodeSdk" />`.
3. In `Directory.Packages.props`, replace the `Microsoft.Extensions.Logging.Abstractions` entry
   (declared only because the vendored project inherits our central package management) with
   `<PackageVersion Include="ClaudeCodeSdk" Version="<new version>" />`.
4. Drop `vendor/ClaudeCodeSdk/ClaudeCodeSdk.csproj` from `NektoTranslate.Server.slnx`.
5. Run the live test to confirm the published package no longer mangles non-ASCII input.

### Re-syncing before then

Pull upstream, copy `src/ClaudeCodeSdk/` over `vendor/ClaudeCodeSdk/`, re-apply **both** patches,
and update the commit SHA above. Patch 2 has not been submitted upstream yet, so a re-sync that
only takes the merged patch 1 will silently reintroduce it - and its symptom is a model that
answers the source text instead of translating it, which reads like a prompt problem rather than a
plumbing one.
