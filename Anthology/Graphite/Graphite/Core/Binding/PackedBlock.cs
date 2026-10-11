namespace Prowl.Graphite;

internal sealed class PackedBlock
{
    public readonly UniformBlockLayout Layout;
    public readonly byte[] Bytes;
    public uint Version;
    public bool Full;
    public uint UploadedVersion;
    public ulong Stamp;
    public DeviceBufferRange Range;

    public PackedBlock(UniformBlockLayout layout)
    {
        Layout = layout;
        Bytes = new byte[layout.Size];
    }
}
