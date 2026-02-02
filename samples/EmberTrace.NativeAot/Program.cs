using System;
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
