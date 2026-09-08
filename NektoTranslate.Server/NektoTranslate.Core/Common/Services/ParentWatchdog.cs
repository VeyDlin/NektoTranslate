using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


namespace NektoTranslate.Common.Services;


// Keeps a server the desktop shell spawned from outliving the shell. --ParentPid names the process
// that started this one; once that process is gone there is no window left to ask this server to
// close, which is exactly the case that otherwise leaves it running in the background forever.
//
// isAlive is injected so ParentIsGone - the one branch worth getting wrong - can be driven by a test
// without starting or killing a real process.
public sealed class ParentWatchdog : BackgroundService {

    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(5);


    private readonly int parentPid;
    private readonly IHostApplicationLifetime lifetime;
    private readonly ILogger<ParentWatchdog> logger;
    private readonly Func<int, bool> isAlive;


    public ParentWatchdog(
        int parentPid,
        IHostApplicationLifetime lifetime,
        ILogger<ParentWatchdog> logger,
        Func<int, bool>? isAlive = null
    ) {
        this.parentPid = parentPid;
        this.lifetime = lifetime;
        this.logger = logger;
        this.isAlive = isAlive ?? DefaultIsAlive;
    }


    // GetProcessById throws for a pid nothing holds any more rather than returning null, which is
    // what turns "not found" into the ArgumentException caught below. A pid that was reused by an
    // unrelated process still reads as alive - the same gap a real parent-liveness check has on
    // every platform, and not one this watchdog can close.
    public static bool DefaultIsAlive(int pid) {
        try {
            using Process process = Process.GetProcessById(pid);

            return !process.HasExited;
        } catch (ArgumentException) {
            return false;
        }
    }


    // Exposed on its own so a test can ask the question without waiting out PollInterval, and
    // without touching the timer loop below at all.
    public bool ParentIsGone() {
        return !isAlive(parentPid);
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using PeriodicTimer timer = new PeriodicTimer(PollInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken)) {
            if (!ParentIsGone()) {
                continue;
            }

            logger.LogInformation("Parent process {ParentPid} is gone, stopping.", parentPid);
            lifetime.StopApplication();

            // CancellationToken.None on purpose: stoppingToken is tied to the host's own shutdown,
            // which StopApplication above just started, so it would cancel this delay almost at
            // once and skip the grace period entirely. If the graceful stop finishes first, the
            // whole process exits and this await never returns, which is the intended race - the
            // Environment.Exit below only runs when graceful shutdown did not finish in time.
            await Task.Delay(ShutdownGrace, CancellationToken.None);
            Environment.Exit(0);
        }
    }
}
