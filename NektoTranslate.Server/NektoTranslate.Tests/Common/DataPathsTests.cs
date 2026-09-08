using System.Runtime.InteropServices;
using NektoTranslate.Common.Services;
using Xunit;


namespace NektoTranslate.Tests.Common;


// One machine, three platforms: the platform parameter lets every branch of Resolve run in the
// same test process, which is the only way to pin all three without three CI runners.
public class DataPathsTests {

    [Fact]
    public void WindowsUsesLocalApplicationData() {
        DataPaths.Layout layout = DataPaths.Resolve(null, OSPlatform.Windows);

        string expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NektoTranslate"
        );

        Assert.Equal(expectedRoot, layout.root);
    }


    [Fact]
    public void MacUsesApplicationSupport() {
        DataPaths.Layout layout = DataPaths.Resolve(null, OSPlatform.OSX);

        string expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Application Support",
            "NektoTranslate"
        );

        Assert.Equal(expectedRoot, layout.root);
    }


    [Fact]
    public void LinuxUsesXdgDataHomeWhenSet() {
        string? previous = Environment.GetEnvironmentVariable("XDG_DATA_HOME");

        try {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", "/tmp/xdg-data");

            DataPaths.Layout layout = DataPaths.Resolve(null, OSPlatform.Linux);

            Assert.Equal(Path.Combine("/tmp/xdg-data", "NektoTranslate"), layout.root);
        } finally {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", previous);
        }
    }


    [Fact]
    public void LinuxFallsBackToLocalShareWhenXdgDataHomeIsUnset() {
        string? previous = Environment.GetEnvironmentVariable("XDG_DATA_HOME");

        try {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);

            DataPaths.Layout layout = DataPaths.Resolve(null, OSPlatform.Linux);

            string expectedRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "share",
                "NektoTranslate"
            );

            Assert.Equal(expectedRoot, layout.root);
        } finally {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", previous);
        }
    }


    // A configured value wins on every platform, and is made absolute rather than trusted as
    // given - a relative path would otherwise point somewhere different depending on the process's
    // current directory on the day it happens to be started.
    [Fact]
    public void AConfiguredValueOverridesEveryPlatform() {
        DataPaths.Layout layout = DataPaths.Resolve("configured-library", OSPlatform.Linux);

        Assert.Equal(Path.GetFullPath("configured-library"), layout.root);
    }


    [Fact]
    public void ABlankConfiguredValueIsTreatedAsAbsent() {
        DataPaths.Layout windows = DataPaths.Resolve("   ", OSPlatform.Windows);

        string expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NektoTranslate"
        );

        Assert.Equal(expectedRoot, windows.root);
    }


    // The names under the root, not just the root itself - Program.cs and the browser session read
    // these by name rather than rebuilding the paths themselves.
    [Fact]
    public void TheLayoutNamesTheFilesAndFoldersUnderTheRoot() {
        DataPaths.Layout layout = DataPaths.Resolve("configured-library", OSPlatform.Windows);

        Assert.Equal(Path.Combine(layout.root, "nekto.db"), layout.database);
        Assert.Equal(Path.Combine(layout.root, "browsers"), layout.browsers);
        Assert.Equal(Path.Combine(layout.root, "browserState.json"), layout.browserState);
        Assert.Equal(Path.Combine(layout.root, "logs"), layout.logs);
    }
}
