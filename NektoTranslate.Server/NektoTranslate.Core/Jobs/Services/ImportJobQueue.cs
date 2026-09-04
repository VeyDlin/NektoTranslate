using System.Collections.Concurrent;
using System.Threading.Channels;


namespace NektoTranslate.Jobs.Services;


// The live handles of an import in flight: how it is cancelled, and how it is paused and resumed.
//
// Pause is a request, honoured between chapters. The chapter being fetched finishes and is kept -
// stopping mid-fetch would throw away a page load the site already served - and the run then waits
// until it is resumed or cancelled. The persisted state follows what the worker observes, so a
// paused run reads as paused after a reload too.
public sealed class ImportRunHandle : IDisposable {

    private readonly CancellationTokenSource cancellation = new CancellationTokenSource();

    private TaskCompletionSource resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    private volatile bool pauseRequested;


    public CancellationToken cancellationToken => cancellation.Token;

    public bool isPauseRequested => pauseRequested;


    public void Cancel() {
        cancellation.Cancel();
        // A run waiting on a pause has to wake to notice the cancellation.
        resumed.TrySetResult();
    }


    public void Pause() {
        pauseRequested = true;
    }


    public void Resume() {
        pauseRequested = false;
        resumed.TrySetResult();
    }


    // Waits until Resume or Cancel. Called by the worker once it has recorded the pause.
    public async Task WaitWhilePausedAsync(CancellationToken stoppingToken) {
        while (pauseRequested && !cancellation.IsCancellationRequested) {
            Task wake = resumed.Task;

            using CancellationTokenRegistration registration = stoppingToken.Register(() => resumed.TrySetResult());
            await wake;

            if (stoppingToken.IsCancellationRequested) {
                return;
            }

            resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }


    public void Dispose() {
        cancellation.Dispose();
    }
}


// Singleton. Pending job identifiers and the handles of the run in flight - state that must outlive
// the request scope a job was created in.
public class ImportJobQueue {

    private readonly Channel<long> pending = Channel.CreateUnbounded<long>();

    private readonly ConcurrentDictionary<long, ImportRunHandle> running = new();


    public ValueTask EnqueueAsync(long jobId, CancellationToken cancellationToken = default) {
        return pending.Writer.WriteAsync(jobId, cancellationToken);
    }


    public ValueTask<long> DequeueAsync(CancellationToken cancellationToken) {
        return pending.Reader.ReadAsync(cancellationToken);
    }


    public ImportRunHandle Register(long jobId) {
        ImportRunHandle handle = new ImportRunHandle();
        running[jobId] = handle;

        return handle;
    }


    public bool Cancel(long jobId) {
        if (!running.TryGetValue(jobId, out ImportRunHandle? handle)) {
            return false;
        }

        handle.Cancel();

        return true;
    }


    public bool Pause(long jobId) {
        if (!running.TryGetValue(jobId, out ImportRunHandle? handle)) {
            return false;
        }

        handle.Pause();

        return true;
    }


    public bool Resume(long jobId) {
        if (!running.TryGetValue(jobId, out ImportRunHandle? handle)) {
            return false;
        }

        handle.Resume();

        return true;
    }


    public bool IsInFlight(long jobId) {
        return running.ContainsKey(jobId);
    }


    public void Release(long jobId) {
        if (running.TryRemove(jobId, out ImportRunHandle? handle)) {
            handle.Dispose();
        }
    }
}
