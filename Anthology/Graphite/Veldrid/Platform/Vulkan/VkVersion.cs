namespace Prowl.Veldrid.Vk;

internal readonly struct VkVersion
{
    private readonly uint value;

    public VkVersion(uint major, uint minor, uint patch)
    {
        value = major << 22 | minor << 12 | patch;
    }

    public readonly uint Major => value >> 22;

    public readonly uint Minor => (value >> 12) & 0x3ff;

    public readonly uint Patch => (value >> 22) & 0xfff;

    public static implicit operator uint(VkVersion version)
    {
        return version.value;
    }
}
