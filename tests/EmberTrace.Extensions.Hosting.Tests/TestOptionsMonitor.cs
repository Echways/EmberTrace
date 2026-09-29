using Microsoft.Extensions.Options;

namespace EmberTrace.Extensions.Hosting.Tests;

internal sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
{
    private T _value;

    public TestOptionsMonitor(T value)
    {
        _value = value;
    }

    public Exception? Failure { get; set; }

    public T CurrentValue
    {
        get => Failure is null ? _value : throw Failure;
        set => _value = value;
    }

    public T Get(string? name)
    {
        return CurrentValue;
    }

    public IDisposable? OnChange(Action<T, string?> listener)
    {
        return null;
    }
}
