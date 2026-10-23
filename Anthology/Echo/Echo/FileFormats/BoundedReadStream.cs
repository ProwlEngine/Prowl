// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo;

/// <summary>Reads at most a fixed number of bytes from a stream, so a decompressor reading ahead stops at the payload's end.</summary>
internal sealed class BoundedReadStream(Stream inner, long length) : Stream
{
    private long _remaining = length;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_remaining <= 0)
            return 0;
        int read = inner.Read(buffer[..(int)Math.Min(buffer.Length, _remaining)]);
        if (read == 0)
            throw new EndOfStreamException("The compressed Echo payload ends early.");
        _remaining -= read;
        return read;
    }

    /// <summary>Moves the inner stream to the end of the payload.</summary>
    public void SkipRest()
    {
        if (_remaining <= 0)
            return;
        if (inner.CanSeek)
            inner.Seek(_remaining, SeekOrigin.Current);
        else
            CopyTo(Null);
        _remaining = 0;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
