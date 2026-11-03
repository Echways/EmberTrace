using System.IO.Compression;

namespace EmberTrace.Format.Internal;

internal static class GzipSupport
{
    private const int BufferSize = 1 << 16;
    private const byte FirstMagicByte = 0x1F;
    private const byte SecondMagicByte = 0x8B;

    public const string TruncatedMessage = "The gzip-compressed EmberTrace file is truncated or corrupt.";

    public static Stream Unwrap(Stream source, List<IDisposable> owned, out bool compressed)
    {
        Span<byte> signature = stackalloc byte[2];
        var start = source.CanSeek ? source.Position : 0;
        var read = source.ReadAtLeast(signature, signature.Length, throwOnEndOfStream: false);

        Stream prefixed;
        if (source.CanSeek)
        {
            source.Position = start;
            prefixed = source;
        }
        else
        {
            prefixed = new PrefixedStream(signature[..read].ToArray(), source);
            owned.Add(prefixed);
        }

        compressed = read == 2 && signature[0] == FirstMagicByte && signature[1] == SecondMagicByte;
        if (!compressed)
            return prefixed;

        var buffered = new BufferedStream(new GZipStream(prefixed, CompressionMode.Decompress, leaveOpen: true), BufferSize);
        owned.Add(buffered);
        return buffered;
    }

    public static void Dispose(List<IDisposable> owned)
    {
        for (var i = owned.Count - 1; i >= 0; i--)
            owned[i].Dispose();
    }

    public static void Write(Action<Stream> write, Stream destination)
    {
        using var gzip = new GZipStream(destination, CompressionLevel.Fastest, leaveOpen: true);
        using var buffered = new BufferedStream(gzip, BufferSize);
        write(buffered);
        buffered.Flush();
    }

    private sealed class PrefixedStream(byte[] prefix, Stream inner) : Stream
    {
        private int _consumed;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            if (_consumed < prefix.Length)
            {
                var take = Math.Min(buffer.Length, prefix.Length - _consumed);
                prefix.AsSpan(_consumed, take).CopyTo(buffer);
                _consumed += take;
                return take;
            }

            return inner.Read(buffer);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
