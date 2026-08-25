using EmberTrace.Format.Internal;

namespace EmberTrace.Tests.Format;

[TestClass]
public class VarIntTests
{
    [TestMethod]
    [DataRow(0UL)]
    [DataRow(1UL)]
    [DataRow(127UL)]
    [DataRow(128UL)]
    [DataRow(300UL)]
    [DataRow(ulong.MaxValue)]
    public void UInt64_RoundTrips(ulong value)
    {
        using var ms = new MemoryStream();
        VarInt.WriteUInt64(ms, value);
        ms.Position = 0;

        Assert.AreEqual(value, VarInt.ReadUInt64(ms));
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(1L)]
    [DataRow(-1L)]
    [DataRow(long.MaxValue)]
    [DataRow(long.MinValue)]
    public void Int64_RoundTripsThroughZigZag(long value)
    {
        using var ms = new MemoryStream();
        VarInt.WriteInt64(ms, value);
        ms.Position = 0;

        Assert.AreEqual(value, VarInt.ReadInt64(ms));
    }

    [TestMethod]
    public void SmallValues_UseASingleByte()
    {
        using var ms = new MemoryStream();
        VarInt.WriteUInt64(ms, 127);

        Assert.AreEqual(1, ms.Length);
    }

    [TestMethod]
    public void SmallNegativeValues_StayCompactUnderZigZag()
    {
        using var ms = new MemoryStream();
        VarInt.WriteInt64(ms, -1);

        Assert.AreEqual(1, ms.Length, "zigzag must not expand small negatives to 10 bytes");
    }

    [TestMethod]
    public void String_RoundTripsIncludingNonAscii()
    {
        using var ms = new MemoryStream();
        VarInt.WriteString(ms, "поток-1");
        ms.Position = 0;

        Assert.AreEqual("поток-1", VarInt.ReadString(ms));
    }

    [TestMethod]
    public void ReadUInt64_OnTruncatedStream_Throws()
    {
        using var ms = new MemoryStream(new byte[] { 0x80 });

        Assert.ThrowsExactly<EndOfStreamException>(() => VarInt.ReadUInt64(ms));
    }

    [TestMethod]
    public void ReadUInt64_OnOverlongEncoding_Throws()
    {
        var overlong = new byte[11];
        for (var i = 0; i < overlong.Length; i++)
            overlong[i] = 0x80;

        using var ms = new MemoryStream(overlong);

        Assert.ThrowsExactly<InvalidDataException>(() => VarInt.ReadUInt64(ms));
    }
}
