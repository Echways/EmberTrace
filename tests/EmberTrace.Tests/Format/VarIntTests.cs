using EmberTrace.Format.Internal;

namespace EmberTrace.Tests.Format;

[TestClass]
public class VarIntTests
{
    [TestMethod]
    [DataRow(0UL, 1)]
    [DataRow(127UL, 1)]
    [DataRow(128UL, 2)]
    [DataRow(16_383UL, 2)]
    [DataRow(16_384UL, 3)]
    [DataRow((ulong)uint.MaxValue, 5)]
    [DataRow(ulong.MaxValue, 10)]
    public void UInt64_RoundTripsInTheMinimalNumberOfBytes(ulong value, int expectedBytes)
    {
        using var ms = new MemoryStream();
        VarInt.WriteUInt64(ms, value);

        Assert.AreEqual(expectedBytes, ms.Length);

        ms.Position = 0;
        Assert.AreEqual(value, VarInt.ReadUInt64(ms));
        Assert.AreEqual(ms.Length, ms.Position);
    }

    [TestMethod]
    [DataRow(0L, 1)]
    [DataRow(-1L, 1)]
    [DataRow(63L, 1)]
    [DataRow(-64L, 1)]
    [DataRow(64L, 2)]
    [DataRow(-65L, 2)]
    [DataRow(long.MaxValue, 10)]
    [DataRow(long.MinValue, 10)]
    public void Int64_RoundTripsThroughZigZagKeepingSmallMagnitudesShort(long value, int expectedBytes)
    {
        using var ms = new MemoryStream();
        VarInt.WriteInt64(ms, value);

        Assert.AreEqual(expectedBytes, ms.Length);

        ms.Position = 0;
        Assert.AreEqual(value, VarInt.ReadInt64(ms));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("поток-1")]
    [DataRow("emoji 🔥")]
    public void String_RoundTripsAsLengthPrefixedUtf8(string value)
    {
        using var ms = new MemoryStream();
        VarInt.WriteString(ms, value);
        ms.Position = 0;

        Assert.AreEqual(value, VarInt.ReadString(ms));
        Assert.AreEqual(ms.Length, ms.Position);
    }

    [TestMethod]
    public void String_AtTheFormatLimit_RoundTrips()
    {
        using var ms = new MemoryStream();
        var value = new string('x', FormatConstants.MaxStringBytes);

        VarInt.WriteString(ms, value);
        ms.Position = 0;

        Assert.AreEqual(value, VarInt.ReadString(ms));
    }

    [TestMethod]
    public void WriteString_AboveTheFormatLimitInUtf8Bytes_Throws()
    {
        using var ms = new MemoryStream();
        var value = new string('ф', FormatConstants.MaxStringBytes / 2 + 1);

        Assert.ThrowsExactly<InvalidOperationException>(() => VarInt.WriteString(ms, value));
        Assert.AreEqual(0L, ms.Length);
    }

    [TestMethod]
    public void ReadString_DeclaringMoreThanTheLimit_Throws()
    {
        using var ms = new MemoryStream();
        VarInt.WriteUInt64(ms, (ulong)FormatConstants.MaxStringBytes + 1);
        ms.Position = 0;

        Assert.ThrowsExactly<InvalidDataException>(() => VarInt.ReadString(ms));
    }

    [TestMethod]
    public void ReadUInt64_OnTruncatedStream_Throws()
    {
        using var ms = new MemoryStream([0x80]);

        Assert.ThrowsExactly<EndOfStreamException>(() => VarInt.ReadUInt64(ms));
    }

    [TestMethod]
    public void ReadUInt64_OnOverlongEncoding_Throws()
    {
        using var ms = new MemoryStream(Enumerable.Repeat((byte)0x80, 11).ToArray());

        Assert.ThrowsExactly<InvalidDataException>(() => VarInt.ReadUInt64(ms));
    }
}
