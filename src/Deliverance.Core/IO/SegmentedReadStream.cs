using System.Buffers;

namespace Deliverance.Core.IO;

/// <summary>
/// Read-only stream over a sequence of byte segments. No large concatenation allocation.
/// </summary>
internal sealed class SegmentedReadStream : Stream
{
    private readonly ReadOnlyMemory<byte>[] _segments;
    private int _segmentIndex;
    private int _segmentOffset;

    public SegmentedReadStream(params ReadOnlyMemory<byte>[] segments)
    {
        _segments = segments ?? throw new ArgumentNullException(nameof(segments));
        if (_segments.Length == 0) _segments = [ReadOnlyMemory<byte>.Empty];
    }

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
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException();

        if (count == 0) return 0;

        int totalRead = 0;

        while (count > 0 && _segmentIndex < _segments.Length)
        {
            var seg = _segments[_segmentIndex];
            var remaining = seg.Length - _segmentOffset;
            if (remaining <= 0)
            {
                _segmentIndex++;
                _segmentOffset = 0;
                continue;
            }

            int toCopy = Math.Min(count, remaining);
            seg.Span.Slice(_segmentOffset, toCopy).CopyTo(buffer.AsSpan(offset, toCopy));

            _segmentOffset += toCopy;
            offset += toCopy;
            count -= toCopy;
            totalRead += toCopy;
        }

        return totalRead;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        // Fast path: sync read is fine; stores typically call CopyToAsync which uses ReadAsync.
        var arr = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            int n = Read(arr, 0, buffer.Length);
            if (n > 0) arr.AsSpan(0, n).CopyTo(buffer.Span);
            return ValueTask.FromResult(n);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(arr);
        }
    }

    public override void Flush() => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
