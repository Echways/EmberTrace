using System.Reflection;
using System.Text;
using System.Xml.Linq;
using EmberTrace.Abstractions.Attributes;
using EmberTrace.Analysis.Model;
using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.OpenTelemetry;
using EmberTrace.Testing;

namespace EmberTrace.ApiDocs.Tests;

[TestClass]
public class ApiDocsTests
{
    private static readonly Lazy<HashSet<string>> EveryId = new(() =>
        new[]
            {
                typeof(Tracer), typeof(TraceAttribute), typeof(ProcessedTrace), typeof(TraceExport),
                typeof(TraceFormat), typeof(TraceText), typeof(TraceBudget), typeof(OpenTelemetryExport),
                typeof(EmberTraceOptions)
            }
            .SelectMany(type => PublicApi.Of(type.Assembly))
            .Select(member => member.Id)
            .ToHashSet());

    [TestMethod]
    public void EmberTrace_IsDocumented()
    {
        AssertDocumented(typeof(Tracer).Assembly);
    }

    [TestMethod]
    public void Abstractions_IsDocumented()
    {
        AssertDocumented(typeof(TraceAttribute).Assembly);
    }

    [TestMethod]
    public void Analysis_IsDocumented()
    {
        AssertDocumented(typeof(ProcessedTrace).Assembly);
    }

    [TestMethod]
    public void Export_IsDocumented()
    {
        AssertDocumented(typeof(TraceExport).Assembly);
    }

    [TestMethod]
    public void Format_IsDocumented()
    {
        AssertDocumented(typeof(TraceFormat).Assembly);
    }

    [TestMethod]
    public void ReportText_IsDocumented()
    {
        AssertDocumented(typeof(TraceText).Assembly);
    }

    [TestMethod]
    public void Testing_IsDocumented()
    {
        AssertDocumented(typeof(TraceBudget).Assembly);
    }

    [TestMethod]
    public void OpenTelemetry_IsDocumented()
    {
        AssertDocumented(typeof(OpenTelemetryExport).Assembly);
    }

    [TestMethod]
    public void Hosting_IsDocumented()
    {
        AssertDocumented(typeof(EmberTraceOptions).Assembly);
    }

    private static void AssertDocumented(Assembly assembly)
    {
        var api = PublicApi.Of(assembly);
        var path = Path.ChangeExtension(assembly.Location, ".xml");
        var documented = File.Exists(path)
            ? XDocument.Load(path).Descendants("member").ToDictionary(m => (string)m.Attribute("name")!)
            : [];

        var report = new StringBuilder();

        foreach (var member in api)
        {
            if (!documented.TryGetValue(member.Id, out var element))
            {
                report.AppendLine(Skeleton(member));
                continue;
            }

            if (string.IsNullOrWhiteSpace(element.Element("summary")?.Value))
                report.AppendLine($"empty <summary>: {member.Id}");

            var parameters = element.Elements("param")
                .Where(p => !string.IsNullOrWhiteSpace(p.Value))
                .Select(p => (string)p.Attribute("name")!)
                .ToHashSet();

            foreach (var parameter in member.Parameters.Where(p => !parameters.Contains(p)))
                report.AppendLine($"missing <param name=\"{parameter}\">: {member.Id}");
        }

        var known = api.Select(m => m.Id).ToHashSet();
        foreach (var orphan in documented.Keys.Where(id => !known.Contains(id)).Order(StringComparer.Ordinal))
            report.AppendLine($"documents a member that does not exist: {orphan}");

        var references = documented.Values
            .SelectMany(member => member.Descendants().Attributes("cref"))
            .Select(cref => cref.Value)
            .Where(cref => cref.Contains("EmberTrace", StringComparison.Ordinal))
            .Distinct();

        foreach (var cref in references.Where(cref => !EveryId.Value.Contains(cref)))
            report.AppendLine($"cref to a member that does not exist: {cref}");

        if (report.Length > 0)
            Assert.Fail($"{Path.GetFileName(path)} is out of date:{Environment.NewLine}{report}");
    }

    private static string Skeleton(ApiMember member)
    {
        var element = new XElement("member", new XAttribute("name", member.Id), new XElement("summary"));
        foreach (var parameter in member.Parameters)
            element.Add(new XElement("param", new XAttribute("name", parameter)));

        return element.ToString();
    }
}
