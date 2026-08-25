using EmberTrace;
using EmberTrace.Sessions;

var id = Tracer.Id("NativeAot.Scope");
Tracer.Start(new SessionOptions { ChunkCapacity = 1024 });

using (Tracer.Scope(id))
{
    Tracer.Instant(id);
}

var session = Tracer.Stop();
Console.WriteLine($"NativeAOT sample collected {session.EventCount} events.");

using var buffer = new MemoryStream();
TraceFormat.Write(session, buffer);
buffer.Position = 0;

var reloaded = TraceFormat.Read(buffer);
Console.WriteLine($"NativeAOT sample reloaded {reloaded.EventCount} events from {buffer.Length} bytes.");