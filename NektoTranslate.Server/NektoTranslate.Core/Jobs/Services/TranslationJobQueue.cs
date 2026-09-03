using System.Collections.Concurrent;
using System.Threading.Channels;


namespace NektoTranslate.Jobs.Services;


// Singleton. Holds the pending run identifiers and the cancellation handles of the run in flight,
// which is state that must outlive the request scope a job was created in.
public class TranslationJobQueue {

    // Unbounded because the queue holds identifiers, not work: a hundred queued runs cost a hundred
    // longs. The runs themselves are executed strictly one at a time.
    private readonly Channel<long> pending = Channel.CreateUnbounded<long>();

    private readonly ConcurrentDictionary<long, CancellationTokenSource> running = new();


    public ValueTask EnqueueAsync(long jobId, CancellationToken cancellationToken = default) {
        return pending.Writer.WriteAsync(jobId, cancellationToken);
    }


    public ValueTask<long> DequeueAsync(CancellationToken cancellationToken) {
        return pending.Reader.ReadAsync(cancellationToken);
    }


    public CancellationTokenSource Register(long jobId) {
        CancellationTokenSource source = new CancellationTokenSource();
        running[jobId] = source;

        return source;
    }


    public bool Cancel(long jobId) {
        if (!running.TryGetValue(jobId, out CancellationTokenSource? source)) {
            return false;
        }

        source.Cancel();

        return true;
    }


    public void Release(long jobId) {
        if (running.TryRemove(jobId, out CancellationTokenSource? source)) {
            source.Dispose();
        }
    }
}
