using System.IO.Compression;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Format;

[TestClass]
public class TraceFormatCompressionTests
{
    [TestMethod]
    public void Gzip_RoundTrip_ByExtension_AndSniffedWithoutExtension()
    {
        using var directory = new TempDirectory();
        var session = Session();
        var compressed = Path.Combine(directory.Path, "run" + TraceFormat.CompressedFileExtension);
        var plain = Path.Combine(directory.Path, "run" + TraceFormat.FileExtension);
        var renamed = Path.Combine(directory.Path, "run.bin");

        TraceFormat.Write(session, compressed);
        TraceFormat.Write(session, plain);
        File.Copy(compressed, renamed);

        var header = File.ReadAllBytes(compressed)[..2];
        CollectionAssert.AreEqual(new byte[] { 0x1F, 0x8B }, header);
        Assert.IsLessThan(new FileInfo(plain).Length, new FileInfo(compressed).Length);

        var expected = session.SortedEvents();
        CollectionAssert.AreEqual(expected, TraceFormat.Read(compressed).SortedEvents());
        CollectionAssert.AreEqual(expected, TraceFormat.Read(renamed).SortedEvents());

        using var stream = TraceFormat.OpenRead(renamed);
        CollectionAssert.AreEqual(expected, stream.Events.ToList());
    }

    [TestMethod]
    public void Gzip_CompressedBytes_AreSniffedOnNonSeekableStreams()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "run.ember.gz");
        TraceFormat.Write(Session(), path);

        using var source = new NonSeekableStream(File.ReadAllBytes(path));
        var read = TraceFormat.Read(source);

        Assert.AreEqual(Session().EventCount, read.EventCount);
    }

    [TestMethod]
    public void Plain_NonSeekableStream_StillReads()
    {
        using var ms = new MemoryStream();
        TraceFormat.Write(Session(), ms);

        using var source = new NonSeekableStream(ms.ToArray());

        Assert.AreEqual(Session().EventCount, TraceFormat.Read(source).EventCount);
    }

    [TestMethod]
    public void Gzip_Truncated_ThrowsInvalidData()
    {
        var bytes = Compressed();

        var ex = Assert.ThrowsExactly<InvalidDataException>(() =>
            TraceFormat.Read(new MemoryStream(bytes[..(bytes.Length / 2)])));
        StringAssert.Contains(ex.Message, "gzip");

        Assert.ThrowsExactly<InvalidDataException>(() =>
        {
            using var stream = TraceFormat.OpenRead(new MemoryStream(bytes[..(bytes.Length / 2)]));
            stream.Events.ToList();
        });
    }

    [TestMethod]
    public void Gzip_Corrupt_ThrowsInvalidData()
    {
        var bytes = Compressed();
        for (var i = bytes.Length / 3; i < bytes.Length / 3 + 16; i++)
            bytes[i] ^= 0xFF;

        Assert.ThrowsExactly<InvalidDataException>(() => TraceFormat.Read(new MemoryStream(bytes)));
    }

    [TestMethod]
    public void Gzip_WrappingAFileWithBadMagic_ThrowsInvalidData()
    {
        using var ms = new MemoryStream();
        using (var gzip = new GZipStream(ms, CompressionLevel.Fastest, true))
        {
            gzip.Write(new byte[200]);
        }

        Assert.ThrowsExactly<InvalidDataException>(() => TraceFormat.Read(new MemoryStream(ms.ToArray())));
    }

    private static byte[] Compressed()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "run.ember.gz");
        TraceFormat.Write(Session(), path);
        return File.ReadAllBytes(path);
    }

    private static TraceSession Session()
    {
        var script = new TraceScript();
        for (var i = 0; i < 400; i++)
            script.Span(1, i * 100L, i * 100L + 50);

        return script.ToSession(metadata: Meta.Of((1, "Work", null)));
    }
}