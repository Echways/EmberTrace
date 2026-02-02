using System.Collections.Concurrent;
using System.Collections.Generic;
using EmberTrace.Metadata;
using EmberTrace;

namespace EmberTrace.Tracing;

internal sealed class CategoryFilter
{
    private readonly ITraceMetadataProvider _meta;
    private readonly HashSet<int>? _enabled;
    private readonly HashSet<int>? _disabled;
    private readonly ConcurrentDictionary<int, int> _idToCategoryId = new();

    public CategoryFilter(ITraceMetadataProvider meta, int[]? enabled, int[]? disabled)
    {
        _meta = meta;

        if (enabled is { Length: > 0 })
            _enabled = new HashSet<int>(enabled);

        if (disabled is { Length: > 0 })
            _disabled = new HashSet<int>(disabled);
    }

    public bool IsActive => _enabled is not null || _disabled is not null;

    public bool Allows(int id)
    {
        if (!IsActive)
            return true;

        var categoryId = ResolveCategoryId(id);

        if (_enabled is not null)
            return _enabled.Contains(categoryId);

        if (_disabled is not null)
            return !_disabled.Contains(categoryId);

        return true;
    }

    private int ResolveCategoryId(int id)
    {
        if (_idToCategoryId.TryGetValue(id, out var categoryId))
            return categoryId;

        if (_meta.TryGet(id, out var meta) && !string.IsNullOrEmpty(meta.Category))
            categoryId = Tracer.CategoryId(meta.Category);
        else
            categoryId = 0;

        _idToCategoryId.TryAdd(id, categoryId);
        return categoryId;
    }
}
