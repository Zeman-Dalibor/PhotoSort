using PhotoSort.Models;

namespace PhotoSort.Services;

/// <summary>
/// Serialises every disk read and decode onto one background thread. Requests wait in a
/// priority queue so the photo the user is looking at is always decoded before any prefetch.
/// </summary>
public sealed class SequentialImageLoader : IDisposable
{
    private readonly IImageDecoder _decoder;
    private readonly Dictionary<string, PendingRequest> _pending = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Thread _worker;
    private long _sequence;

    public SequentialImageLoader(IImageDecoder decoder)
    {
        _decoder = decoder;
        _worker = new Thread(RunWorkerLoop)
        {
            IsBackground = true,
            Name = "photo-io",
            Priority = ThreadPriority.BelowNormal
        };
        _worker.Start();
    }

    /// <summary>
    /// Queues a decode. Repeated calls for the same key share one task; a repeated call with a
    /// higher priority promotes the queued request.
    /// </summary>
    public Task<DecodedImage> EnqueueAsync(string key, string path, int maxEdge, LoadPriority priority)
    {
        lock (_gate)
        {
            if (_pending.TryGetValue(key, out var existing))
            {
                if (priority < existing.Priority)
                {
                    existing.Priority = priority;
                    existing.Sequence = ++_sequence;
                }

                return existing.Completion.Task;
            }

            var request = new PendingRequest(key, path, maxEdge, priority, ++_sequence);
            _pending.Add(key, request);
            _signal.Release();
            return request.Completion.Task;
        }
    }

    /// <summary>
    /// Drops queued requests the caller is no longer interested in. A request that is already
    /// being decoded runs to completion; a single decode is short enough not to matter.
    /// </summary>
    public void DropPending(Func<string, LoadPriority, bool> shouldKeep)
    {
        List<PendingRequest> dropped;

        lock (_gate)
        {
            dropped = _pending.Values.Where(r => !r.Started && !shouldKeep(r.Key, r.Priority)).ToList();
            foreach (var request in dropped)
            {
                _pending.Remove(request.Key);
            }
        }

        foreach (var request in dropped)
        {
            request.Completion.TrySetCanceled();
        }
    }

    private void RunWorkerLoop()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                _signal.Wait(_shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var request = TakeNext();
            if (request is null)
            {
                continue;
            }

            DecodedImage result;
            try
            {
                result = _decoder.Decode(request.Path, request.MaxEdge);
            }
            catch (Exception e)
            {
                result = DecodedImage.Failure(e.Message);
            }

            lock (_gate)
            {
                _pending.Remove(request.Key);
            }

            if (!request.Completion.TrySetResult(result))
            {
                result.Dispose();
            }
        }
    }

    private PendingRequest? TakeNext()
    {
        lock (_gate)
        {
            PendingRequest? best = null;

            foreach (var candidate in _pending.Values)
            {
                if (candidate.Started)
                {
                    continue;
                }

                // Highest priority first; within a priority the newest request wins, because it
                // reflects where the user is right now.
                if (best is null ||
                    candidate.Priority < best.Priority ||
                    (candidate.Priority == best.Priority && candidate.Sequence > best.Sequence))
                {
                    best = candidate;
                }
            }

            if (best is not null)
            {
                best.Started = true;
            }

            return best;
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _signal.Release();
        _worker.Join(TimeSpan.FromSeconds(2));
        _shutdown.Dispose();
        _signal.Dispose();
    }

    private sealed class PendingRequest(string key, string path, int maxEdge, LoadPriority priority, long sequence)
    {
        public string Key { get; } = key;
        public string Path { get; } = path;
        public int MaxEdge { get; } = maxEdge;
        public LoadPriority Priority { get; set; } = priority;
        public long Sequence { get; set; } = sequence;
        public bool Started { get; set; }

        public TaskCompletionSource<DecodedImage> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
