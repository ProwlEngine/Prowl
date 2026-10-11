namespace Prowl.Graphite;

internal sealed class PackedBlock
{
    public readonly UniformBlockLayout Layout;
    public byte[] Bytes = System.Array.Empty<byte>();
    public uint Version;
    public int Hits;
    public bool Full;
    public uint UploadedVersion;
    public ulong Stamp;
    public DeviceBufferRange Range;

    public PackedBlock(UniformBlockLayout layout)
    {
        Layout = layout;
    }
}
