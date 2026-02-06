using System.IO.Compression;

namespace Deliverance.Core.Codecs;

public sealed class GzipCodec : ICompressionCodec
{
    public byte Id => 1;
    public string Name => "gzip";

    public byte[] Compress(ReadOnlyMemory<byte> input)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            gz.Write(input.Span);
        return ms.ToArray();
    }

    public byte[] Decompress(ReadOnlyMemory<byte> input)
    {
        using var src = new MemoryStream(input.ToArray());
        using var gz = new GZipStream(src, CompressionMode.Decompress);
        using var dst = new MemoryStream();
        gz.CopyTo(dst);
        return dst.ToArray();
    }
}
