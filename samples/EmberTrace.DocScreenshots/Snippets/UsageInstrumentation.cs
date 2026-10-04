using EmberTrace;
using EmberTrace.Abstractions.Attributes;

[assembly: TraceId(UsageInstrumentation.Checkout, "Checkout", "App")]
[assembly: TraceId(UsageInstrumentation.Payment, "Payment", "IO")]
[assembly: TraceId(UsageInstrumentation.Receipt, "Receipt", "CPU")]

internal static class UsageInstrumentation
{
    public const int Checkout = 4000;
    public const int Payment = 4100;
    public const int Receipt = 4200;

    public static async Task RunAsync(string path)
    {
        Tracer.Start();

        await using (Tracer.ScopeAsync(Checkout))
        {
            await using (Tracer.ScopeAsync(Payment))
            {
                await Task.Delay(10);
            }

            using (Tracer.Scope(Receipt))
            {
                Thread.SpinWait(50_000);
            }
        }

        var session = Tracer.Stop();
        var meta = Tracer.CreateMetadata();

        using var fs = File.Create(path);
        TraceExport.WriteChromeComplete(session, fs, meta);
    }
}
