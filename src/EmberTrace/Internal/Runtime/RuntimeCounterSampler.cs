using EmberTrace.Internal.Time;
using EmberTrace.Sessions;
using EmberTrace.Tracing;

namespace EmberTrace.Internal.Runtime;

internal sealed class RuntimeCounterSampler : IRuntimeCounterSink, IDisposable
{
    internal const string ThreadName = "EmberTrace.Runtime";

    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan MaxInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly RuntimeCounterCollector _collector;
    private readonly TimeSpan _interval;
    private readonly SystemRuntimeMetrics _metrics;
    private readonly Profiler _profiler;
    private readonly ManualResetEventSlim _stop = new(false);

    private Thread? _thread;

    public RuntimeCounterSampler(Profiler profiler, RuntimeCounters enabled, TimeSpan interval)
    {
        _profiler = profiler;
        _interval = Clamp(interval);
        _metrics = new SystemRuntimeMetrics((enabled & RuntimeCounters.Exceptions) != 0);
        _collector = new RuntimeCounterCollector(enabled, _metrics);
    }

    public void Counter(int id, long value)
    {
        _profiler.WriteRuntime(id, TraceEventKind.Counter, value, 0);
    }

    public void Span(int id, long startTimestamp, long endTimestamp)
    {
        _profiler.WriteRuntime(id, TraceEventKind.Begin, 0, startTimestamp);
        _profiler.WriteRuntime(id, TraceEventKind.End, 0, endTimestamp);
    }

    public void Start()
    {
        var thread = new Thread(Loop)
        {
            Name = ThreadName,
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal
        };

        _thread = thread;
        thread.Start();
    }

    public bool Stop()
    {
        _stop.Set();

        var thread = _thread;
        _thread = null;
        return thread is null || thread.Join(StopTimeout);
    }

    public void Dispose()
    {
        var stopped = Stop();
        _metrics.Dispose();

        if (stopped)
            _stop.Dispose();
    }

    private static TimeSpan Clamp(TimeSpan interval)
    {
        if (interval < MinInterval) return MinInterval;
        if (interval > MaxInterval) return MaxInterval;
        return interval;
    }

    private void Loop()
    {
        while (!_stop.IsSet)
        {
            try
            {
                _collector.Sample(Timestamp.Now(), this);
            }
            catch (Exception)
            {
            }

            if (_stop.Wait(_interval))
                return;
        }
    }
}
