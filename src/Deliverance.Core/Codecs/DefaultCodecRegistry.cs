namespace Deliverance.Core.Codecs;

public sealed class DefaultCodecRegistry : ICodecRegistry
{
    private readonly Dictionary<byte, ICompressionCodec> _codecs = new();

    public DefaultCodecRegistry()
    {
        // Always register "none" (0)
        Register(new NoCompressionCodec());
    }

    public bool TryGet(byte id, out ICompressionCodec codec)
        => _codecs.TryGetValue(id, out codec!);

    public void Register(ICompressionCodec codec)
    {
        if (codec is null) throw new ArgumentNullException(nameof(codec));
        _codecs[codec.Id] = codec;
    }
}
