using EmberTrace;
using EmberTrace.Analysis;
using EmberTrace.AutoInstrumentation;
using EmberTrace.Sessions;

Tracer.Start(new SessionOptions());

var service = new OrderService();
for (var i = 0; i < 20; i++)
    await service.PlaceAsync(i).ConfigureAwait(false);

var session = Tracer.Stop();

Console.WriteLine(TraceText.Write(session.Process(), session.Metadata));
