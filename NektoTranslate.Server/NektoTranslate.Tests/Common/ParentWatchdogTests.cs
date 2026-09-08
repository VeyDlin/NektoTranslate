using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NektoTranslate.Common.Services;
using Xunit;


namespace NektoTranslate.Tests.Common;


// ParentIsGone is the one branch worth getting wrong, and it is checked here through the injected
// Func<int, bool> rather than against a real process - starting and killing one just to prove a
// boolean flip would make every run of this suite slower for no more confidence.
public class ParentWatchdogTests {

    [Fact]
    public void TheParentIsNotGoneWhileTheInjectedCheckReportsItAlive() {
        ParentWatchdog watchdog = new ParentWatchdog(4321, new RecordingLifetime(), NullLogger<ParentWatchdog>.Instance, pid => true);

        Assert.False(watchdog.ParentIsGone());
    }


    [Fact]
    public void TheParentIsGoneOnceTheInjectedCheckReportsItMissing() {
        ParentWatchdog watchdog = new ParentWatchdog(4321, new RecordingLifetime(), NullLogger<ParentWatchdog>.Instance, pid => false);

        Assert.True(watchdog.ParentIsGone());
    }


    [Fact]
    public void TheConfiguredParentPidIsWhatTheInjectedCheckIsAskedAbout() {
        int? seenPid = null;

        ParentWatchdog watchdog = new ParentWatchdog(9999, new RecordingLifetime(), NullLogger<ParentWatchdog>.Instance, pid => {
            seenPid = pid;

            return true;
        });

        watchdog.ParentIsGone();

        Assert.Equal(9999, seenPid);
    }


    // The default check, used whenever nothing is injected, must at least find the process running
    // the test itself - the one pid a test can assert is alive without depending on anything else.
    [Fact]
    public void TheDefaultCheckFindsTheCurrentProcessAlive() {
        Assert.True(ParentWatchdog.DefaultIsAlive(Environment.ProcessId));
    }


    [Fact]
    public void TheDefaultCheckReportsAPidNothingHoldsAsGone() {
        // Larger than any pid a real system hands out, and never reused within a test run - the
        // only thing DefaultIsAlive needs to prove the ArgumentException branch works.
        Assert.False(ParentWatchdog.DefaultIsAlive(int.MaxValue));
    }


    // IHostApplicationLifetime has no in-box fake; the three tests above only need StopApplication
    // to be callable without throwing, which this bare implementation is enough for.
    private sealed class RecordingLifetime : IHostApplicationLifetime {

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;


        public void StopApplication() {
        }
    }
}
