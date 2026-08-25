using EmberTrace.Format.Internal;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Format;

[TestClass]
public class TraceFormatErrorTests
{
    [TestMethod]
    public void Header_RoundTrips()
    {
        var header = new SessionHeader(
            FormatConstants.Version, true, 10_000_000, 111, 222, 3, 4, 5, 6);

        using var ms = new MemoryStream();
        TraceFormatWriter.WriteHeader(ms, header);
        Assert.AreEqual(FormatConstants.HeaderSize, ms.Length);

        ms.Position = 0;
        Assert.AreEqual(header, TraceFormatReader.ReadHeader(ms));
    }

    [TestMethod]
    public void ReadHeader_WithWrongMagic_Throws()
    {
        var bytes = new byte[FormatConstants.HeaderSize];
        bytes[0] = (byte)'N';
        bytes[1] = (byte)'O';

        using var ms = new MemoryStream(bytes);

        var ex = Assert.ThrowsExactly<InvalidDataException>(() => TraceFormatReader.ReadHeader(ms));
        StringAssert.Contains(ex.Message, "not an EmberTrace");
    }

    [TestMethod]
    public void ReadHeader_WithNewerVersion_Throws()
    {
        var header = new SessionHeader(
            (ushort)(FormatConstants.Version + 1), false, 1, 0, 0, 0, 0, 0, 0);

        using var ms = new MemoryStream();
        TraceFormatWriter.WriteHeader(ms, header);
        ms.Position = 0;

        var ex = Assert.ThrowsExactly<InvalidDataException>(() => TraceFormatReader.ReadHeader(ms));
        StringAssert.Contains(ex.Message, "version");
    }

    [TestMethod]
    public void ReadHeader_OnTruncatedStream_Throws()
    {
        using var ms = new MemoryStream(new byte[10]);

        Assert.ThrowsExactly<EndOfStreamException>(() => TraceFormatReader.ReadHeader(ms));
    }

    [TestMethod]
    public void Read_OnTruncatedEventSection_Throws()
    {
        var session = TraceSession.FromEvents(
            new[]
            {
                new TraceEventRecord(1, 1, 10, TraceEventKind.Begin, 0, 0, 1),
                new TraceEventRecord(1, 1, 20, TraceEventKind.End, 0, 0, 2)
            },
            10, 20, 1_000_000);

        using var full = new MemoryStream();
        TraceFormat.Write(session, full);

        using var ms = new MemoryStream(full.ToArray()[..^3]);

        Assert.ThrowsExactly<EndOfStreamException>(() => TraceFormat.Read(ms));
    }

    [TestMethod]
    public void Read_OnUnknownSectionId_Throws()
    {
        using var ms = new MemoryStream();
        TraceFormatWriter.WriteHeader(ms, EmptyHeader);
        ms.WriteByte(200);
        ms.Position = 0;

        var ex = Assert.ThrowsExactly<InvalidDataException>(() => TraceFormat.Read(ms));
        StringAssert.Contains(ex.Message, "Unknown section id 200");
    }

    [TestMethod]
    public void Read_OnMissingEndOfFile_Throws()
    {
        using var ms = new MemoryStream();
        TraceFormatWriter.WriteHeader(ms, EmptyHeader);
        ms.Position = 0;

        Assert.ThrowsExactly<EndOfStreamException>(() => TraceFormat.Read(ms));
    }

    [TestMethod]
    public void Read_OnAbsurdEventCount_ThrowsWithoutHugeAllocation()
    {
        using var ms = new MemoryStream();
        TraceFormatWriter.WriteHeader(ms, EmptyHeader);
        ms.WriteByte(FormatConstants.Section.Events);
        VarInt.WriteUInt64(ms, 1_000_000_000_000UL);
        ms.Position = 0;

        Assert.ThrowsExactly<InvalidDataException>(() => TraceFormat.Read(ms));
    }

    [TestMethod]
    public void Read_OnEventCountBeyondRemainingBytes_Throws()
    {
        using var ms = new MemoryStream();
        TraceFormatWriter.WriteHeader(ms, EmptyHeader);
        ms.WriteByte(FormatConstants.Section.Events);
        VarInt.WriteUInt64(ms, 100_000);
        ms.Position = 0;

        var ex = Assert.ThrowsExactly<InvalidDataException>(() => TraceFormat.Read(ms));
        StringAssert.Contains(ex.Message, "entries but only");
    }

    private static SessionHeader EmptyHeader =>
        new(FormatConstants.Version, false, 1_000_000, 0, 0, 0, 0, 0, 0);
}
