namespace Deliverance.Core.Codecs;

public sealed class NoCompressionCodec : ICompressionCodec
{
    public byte Id => 0;
    public string Name => "none";

    public byte[] Compress(ReadOnlyMemory<byte> input) => input.ToArray();
    public byte[] Decompress(ReadOnlyMemory<byte> input) => input.ToArray();
}
