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
        var stacks = new Dictionary<int, List<SpanFrame>>();

        foreach (var e in session.EnumerateEventsSorted())
        {
            if (e.Kind == TraceEventKind.Begin)
            {
                if (!stacks.TryGetValue(e.ThreadId, out var stack))
                {
                    stack = new List<SpanFrame>(capacity: 64);
                    stacks.Add(e.ThreadId, stack);
                }

                Resolve(meta, e.Id, out var name, out var category);

                var activity = new Activity(name);
                activity.SetIdFormat(ActivityIdFormat.W3C);
                activity.SetStartTime(ToUtc(session, baseUtc, e.Timestamp));

                if (stack.Count > 0)
                {
                    var parent = stack[^1].Activity;
                    activity.SetParentId(parent.TraceId, parent.SpanId, parent.ActivityTraceFlags);
                }

                activity.Start();
                activity.SetTag("embertrace.id", e.Id);

                if (!string.IsNullOrEmpty(category))
                    activity.SetTag("embertrace.category", category);

                if (options.IncludeThreadIdTag)
                    activity.SetTag("thread.id", e.ThreadId);

                stack.Add(new SpanFrame(e.Id, activity));
                continue;
            }

            if (e.Kind == TraceEventKind.End)
            {
                if (!stacks.TryGetValue(e.ThreadId, out var stack) || stack.Count == 0)
                    continue;

                var idx = FindMatch(stack, e.Id);
                if (idx < 0)
                    continue;

                var endTime = ToUtc(session, baseUtc, e.Timestamp);
                for (int i = stack.Count - 1; i >= idx; i--)
                    CloseSpan(stack[i], endTime, spans);

                stack.RemoveRange(idx, stack.Count - idx);
                continue;
            }

            if (!options.IncludeFlowsAsLinks)
                continue;

            if (e.FlowId == 0)
                continue;

            if (!stacks.TryGetValue(e.ThreadId, out var flowStack) || flowStack.Count == 0)
                continue;

            var link = CreateFlowLink(e.FlowId, e.Id, e.Timestamp);
            flowStack[^1].Activity.AddLink(link);
        }

        if (stacks.Count > 0)
        {
            var endTime = ToUtc(session, baseUtc, session.EndTimestamp);
            foreach (var kvp in stacks)
            {
                var stack = kvp.Value;
                for (int i = stack.Count - 1; i >= 0; i--)
                    CloseSpan(stack[i], endTime, spans);
            }
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

    private static int FindMatch(List<SpanFrame> stack, int id)
    {
        for (int i = stack.Count - 1; i >= 0; i--)
        {
            if (stack[i].Id == id)
                return i;
        }

        return -1;
    }

    private static void CloseSpan(SpanFrame frame, DateTime endTime, List<Activity> spans)
    {
        frame.Activity.SetEndTime(endTime);
        frame.Activity.Stop();
        spans.Add(frame.Activity);
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

    private readonly struct SpanFrame
    {
        public readonly int Id;
        public readonly Activity Activity;

        public SpanFrame(int id, Activity activity)
        {
            Id = id;
            Activity = activity;
        }
    }
}
