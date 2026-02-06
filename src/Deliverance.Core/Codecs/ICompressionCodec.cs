namespace Deliverance.Core.Codecs;

public interface ICompressionCodec
{
    byte Id { get; }            // 0..255
    string Name { get; }        // human-friendly

    byte[] Compress(ReadOnlyMemory<byte> input);
    byte[] Decompress(ReadOnlyMemory<byte> input);
}
