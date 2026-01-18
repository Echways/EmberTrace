using System.Collections.Generic;
using EmberTrace.Internal.Buffering;
using EmberTrace.Processing;
using EmberTrace.Reporting;
using EmberTrace.Processing.Model;



namespace EmberTrace.Public;

public sealed class TraceSession
{
    internal TraceSession(IReadOnlyList<Chunk> chunks, long startTimestamp, long endTimestamp, SessionOptions options)
    {
        Chunks = chunks;
        StartTimestamp = startTimestamp;
        EndTimestamp = endTimestamp;
        Options = options;
    }

    internal IReadOnlyList<Chunk> Chunks { get; }
    internal long StartTimestamp { get; }
    internal long EndTimestamp { get; }
    public SessionOptions Options { get; }
    public TraceStats Analyze() => TraceProcessor.Analyze(this);
    public ProcessedTrace Process() => TraceProcessor.Process(this);



    public double DurationMs =>
        (EndTimestamp - StartTimestamp) * 1000.0 / EmberTrace.Internal.Time.Timestamp.Frequency;

    public long EventCount
    {
        get
        {
            long total = 0;
            for (int i = 0; i < Chunks.Count; i++)
                total += Chunks[i].Count;
            return total;
        }
    }
}
