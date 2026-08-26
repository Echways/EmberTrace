using Microsoft.Extensions.Options;

namespace EmberTrace.Extensions.Hosting.Tests;

internal sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
{
    public TestOptionsMonitor(T value)
    {
        CurrentValue = value;
    }

    public T CurrentValue { get; set; }

    public T Get(string? name)
    {
        return CurrentValue;
    }

    public IDisposable? OnChange(Action<T, string?> listener)
    {
        return null;
    }
}
