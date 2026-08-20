using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace.OpenTelemetry;

public sealed class OpenTelemetryExportOptions
{
    public bool IncludeFlowsAsLinks { get; init; } = true;
    public bool IncludeThreadIdTag { get; init; } = true;
    public DateTimeOffset? BaseUtc { get; init; }
}

public static class OpenTelemetryExport
{
    public static IReadOnlyList<Activity> CreateSpans(
        TraceSession session,
        ITraceMetadataProvider? meta = null,
        OpenTelemetryExportOptions? options = null)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));

        options ??= new OpenTelemetryExportOptions();
        meta ??= TraceMetadata.CreateDefault();

        var baseUtc = options.BaseUtc ?? DateTimeOffset.UtcNow - TimeSpan.FromSeconds(session.DurationMs / 1000.0);
        var spans = new List<Activity>(capacity: (int)Math.Min(int.MaxValue, session.EventCount / 2));
        var live = new Dictionary<int, List<Activity>>();
        var reader = new ScopeReader(session);

        using var flows = FlowEvents(session, options.IncludeFlowsAsLinks).GetEnumerator();
        var pendingFlow = flows.MoveNext();

        var ambient = Activity.Current;
        try
        {
            foreach (var step in reader.Read())
            {
                var timestamp = step.Kind == ScopeStepKind.Open ? step.StartTimestamp : step.EndTimestamp;

                while (pendingFlow && flows.Current.Timestamp <= timestamp)
                {
                    AddFlowLink(live, flows.Current);
                    pendingFlow = flows.MoveNext();
                }

                if (step.Kind == ScopeStepKind.Open)
                {
                    Resolve(meta, step.Id, out var name, out var category);

                    var activity = new Activity(name);
                    activity.SetIdFormat(ActivityIdFormat.W3C);
                    activity.SetStartTime(ToUtc(session, baseUtc, step.StartTimestamp));

                    if (step.ParentTag is Activity parent)
                        activity.SetParentId(parent.TraceId, parent.SpanId, parent.ActivityTraceFlags);

                    Activity.Current = null;
                    activity.Start();

                    activity.SetTag("embertrace.id", step.Id);

                    if (!string.IsNullOrEmpty(category))
                        activity.SetTag("embertrace.category", category);

                    if (options.IncludeThreadIdTag)
                        activity.SetTag("thread.id", step.ThreadId);

                    if (step.IsAsync)
                        activity.SetTag("embertrace.async_scope_id", step.AsyncScopeId);

                    step.Tag = activity;
                    Track(live, step.ThreadId).Add(activity);
                    continue;
                }

                if (step.Tag is not Activity span)
                    continue;

                Untrack(live, step.ThreadId, span);

                span.SetEndTime(ToUtc(session, baseUtc, step.EndTimestamp));
                spans.Add(span);
            }

            while (pendingFlow)
            {
                AddFlowLink(live, flows.Current);
                pendingFlow = flows.MoveNext();
            }
        }
        finally
        {
            Activity.Current = ambient;
        }

        return spans;
    }

    public static void Export(
        TraceSession session,
        Action<Activity> onSpan,
        ITraceMetadataProvider? meta = null,
        OpenTelemetryExportOptions? options = null)
    {
        if (onSpan is null) throw new ArgumentNullException(nameof(onSpan));

        var spans = CreateSpans(session, meta, options);
        for (int i = 0; i < spans.Count; i++)
            onSpan(spans[i]);
    }

    private static IEnumerable<TraceEventRecord> FlowEvents(TraceSession session, bool include)
    {
        if (!include)
            yield break;

        foreach (var e in session.EnumerateEventsSorted())
        {
            if (e.IsScope || e.FlowId == 0)
                continue;

            yield return e;
        }
    }

    private static List<Activity> Track(Dictionary<int, List<Activity>> live, int threadId)
    {
        if (!live.TryGetValue(threadId, out var stack))
        {
            stack = new List<Activity>(capacity: 64);
            live.Add(threadId, stack);
        }

        return stack;
    }

    private static void Untrack(Dictionary<int, List<Activity>> live, int threadId, Activity activity)
    {
        if (!live.TryGetValue(threadId, out var stack))
            return;

        var index = stack.LastIndexOf(activity);
        if (index >= 0)
            stack.RemoveAt(index);
    }

    private static void AddFlowLink(Dictionary<int, List<Activity>> live, TraceEventRecord e)
    {
        if (!live.TryGetValue(e.ThreadId, out var stack) || stack.Count == 0)
            return;

        stack[^1].AddLink(CreateFlowLink(e.FlowId, e.Id, e.Timestamp));
    }

    private static DateTime ToUtc(TraceSession session, DateTimeOffset baseUtc, long timestamp)
    {
        var delta = timestamp - session.StartTimestamp;
        if (delta <= 0)
            return baseUtc.UtcDateTime;

        var seconds = delta / (double)session.TimestampFrequency;
        return baseUtc.UtcDateTime + TimeSpan.FromSeconds(seconds);
    }

    private static ActivityLink CreateFlowLink(long flowId, int id, long timestamp)
    {
        var traceId = FlowTraceId(flowId);
        var spanId = FlowSpanId(flowId, id, timestamp);
        var context = new ActivityContext(traceId, spanId, ActivityTraceFlags.Recorded);
        return new ActivityLink(context);
    }

    private static ActivityTraceId FlowTraceId(long flowId)
    {
        var value = (ulong)flowId;
        if (value == 0)
            value = 1;

        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        BinaryPrimitives.WriteUInt64BigEndian(bytes[8..], value ^ 0x9e3779b97f4a7c15);
        return ActivityTraceId.CreateFromBytes(bytes);
    }

    private static ActivitySpanId FlowSpanId(long flowId, int id, long timestamp)
    {
        unchecked
        {
            const ulong offset = 14695981039346656037;
            const ulong prime = 1099511628211;

            ulong hash = offset;
            hash = (hash ^ (ulong)flowId) * prime;
            hash = (hash ^ (ulong)id) * prime;
            hash = (hash ^ (ulong)timestamp) * prime;

            if (hash == 0)
                hash = 1;

            Span<byte> bytes = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(bytes, hash);
            return ActivitySpanId.CreateFromBytes(bytes);
        }
    }

    private static void Resolve(ITraceMetadataProvider meta, int id, out string name, out string category)
    {
        if (meta.TryGet(id, out var m))
        {
            name = m.Name;
            category = m.Category ?? string.Empty;
            return;
        }

        name = id.ToString();
        category = string.Empty;
    }
}
