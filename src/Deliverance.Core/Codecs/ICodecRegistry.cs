namespace Deliverance.Core.Codecs;

public interface ICodecRegistry
{
    bool TryGet(byte id, out ICompressionCodec codec);
    void Register(ICompressionCodec codec);
}