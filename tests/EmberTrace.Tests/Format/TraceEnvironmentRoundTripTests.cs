using EmberTrace.Format.Internal;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Format;

[TestClass]
public class TraceEnvironmentRoundTripTests
{
    private const string VersionOneFile =
        "454D425254524300010000005000000040420F00000000000000000000000000E8030000000000000200000000000000" +
        "00000000000000000000000000000000000000000000000000000000000000000100020003028164020202E2F4030200";

    [TestMethod]
    public void RoundTrip_PreservesEnvironmentAndTags()
    {
        var start = Environment(("os", "Linux"), ("cpu.governor", "performance"), ("tag.arm", "A"), ("tag.round", "3"));
        var stop = Environment(("os", "Linux"), ("cpu.governor", "powersave"), ("tag.arm", "A"));
        var session = new TraceScript().Span(1, 10, 20).ToSession(environment: start, environmentAtStop: stop);

        var read = RoundTrip(session);

        CollectionAssert.AreEquivalent(start.Values.ToArray(), read.Environment!.Values.ToArray());
        CollectionAssert.AreEquivalent(stop.Values.ToArray(), read.EnvironmentAtStop!.Values.ToArray());
    }

    [TestMethod]
    public void RoundTrip_StartOnly_KeepsStopNull()
    {
        var session = new TraceScript().Span(1, 10, 20).ToSession(environment: Environment(("os", "Linux")));

        var read = RoundTrip(session);

        Assert.IsNotNull(read.Environment);
        Assert.IsNull(read.EnvironmentAtStop);
    }

    [TestMethod]
    public void RoundTrip_WithoutEnvironment_WritesNoSectionAndReadsNull()
    {
        var session = new TraceScript().Span(1, 10, 20).ToSession();

        using var ms = new MemoryStream();
        TraceFormat.Write(session, ms);

        CollectionAssert.DoesNotContain(ms.ToArray(), (byte)FormatConstants.Section.Environment);
        ms.Position = 0;
        var read = TraceFormat.Read(ms);
        Assert.IsNull(read.Environment);
        Assert.IsNull(read.EnvironmentAtStop);
    }

    [TestMethod]
    public void Write_StampsVersionTwo()
    {
        using var ms = new MemoryStream();
        TraceFormat.Write(new TraceScript().Span(1, 10, 20).ToSession(), ms);

        Assert.AreEqual(2, BitConverter.ToUInt16(ms.ToArray(), 8));
    }

    [TestMethod]
    public void Read_Version1File_HasNullEnvironment()
    {
        using var ms = new MemoryStream(Convert.FromHexString(VersionOneFile));

        var session = TraceFormat.Read(ms);

        Assert.IsNull(session.Environment);
        Assert.IsNull(session.EnvironmentAtStop);
        Assert.AreEqual(2, session.EventCount);
    }

    [TestMethod]
    public void Read_EnvironmentSectionInVersion1File_Throws()
    {
        var bytes = Convert.FromHexString(VersionOneFile);
        using var ms = new MemoryStream();
        ms.Write(bytes, 0, bytes.Length - 1);
        ms.WriteByte(FormatConstants.Section.Environment);
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(FormatConstants.Section.EndOfFile);
        ms.Position = 0;

        var ex = Assert.ThrowsExactly<InvalidDataException>(() => TraceFormat.Read(ms));
        StringAssert.Contains(ex.Message, "Unknown section id");
    }

    [TestMethod]
    public void Read_UnknownSectionInVersion2_Throws()
    {
        using var ms = new MemoryStream();
        TraceFormatWriter.WriteHeader(ms, new SessionHeader(2, false, 1_000_000, 0, 0, 0, 0, 0, 0));
        ms.WriteByte(99);
        ms.Position = 0;

        var ex = Assert.ThrowsExactly<InvalidDataException>(() => TraceFormat.Read(ms));
        StringAssert.Contains(ex.Message, "Unknown section id 99");
        StringAssert.Contains(ex.Message, "version 2");
    }

    [TestMethod]
    public void Read_EnvironmentWithHugeDeclaredLength_ThrowsWithoutAllocating()
    {
        using var ms = new MemoryStream();
        TraceFormatWriter.WriteHeader(ms, new SessionHeader(2, false, 1_000_000, 0, 0, 0, 0, 0, 0));
        ms.WriteByte(FormatConstants.Section.Environment);
        ms.WriteByte(1);
        VarInt.WriteUInt64(ms, 1);
        VarInt.WriteUInt64(ms, 1_500_000_000);
        ms.Write(new byte[16]);
        ms.Position = 0;

        var before = GC.GetAllocatedBytesForCurrentThread();

        Assert.ThrowsExactly<InvalidDataException>(() => TraceFormat.Read(ms));

        Assert.IsLessThan(1_000_000, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [TestMethod]
    public void Read_EnvironmentWithAbsurdEntryCount_Throws()
    {
        using var ms = new MemoryStream();
        TraceFormatWriter.WriteHeader(ms, new SessionHeader(2, false, 1_000_000, 0, 0, 0, 0, 0, 0));
        ms.WriteByte(FormatConstants.Section.Environment);
        ms.WriteByte(1);
        VarInt.WriteUInt64(ms, 1_000_000_000_000UL);
        ms.Position = 0;

        Assert.ThrowsExactly<InvalidDataException>(() => TraceFormat.Read(ms));
    }

    [TestMethod]
    public void Read_EnvironmentWithBadPresenceByte_Throws()
    {
        using var ms = new MemoryStream();
        TraceFormatWriter.WriteHeader(ms, new SessionHeader(2, false, 1_000_000, 0, 0, 0, 0, 0, 0));
        ms.WriteByte(FormatConstants.Section.Environment);
        ms.WriteByte(7);
        ms.Position = 0;

        Assert.ThrowsExactly<InvalidDataException>(() => TraceFormat.Read(ms));
    }

    private static TraceEnvironment Environment(params (string Key, string Value)[] values)
    {
        return new TraceEnvironment(values.ToDictionary(v => v.Key, v => v.Value));
    }

    private static TraceSession RoundTrip(TraceSession session)
    {
        using var ms = new MemoryStream();
        TraceFormat.Write(session, ms);
        ms.Position = 0;
        return TraceFormat.Read(ms);
    }
}