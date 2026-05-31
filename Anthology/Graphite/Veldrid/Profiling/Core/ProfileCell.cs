#if PROFILE_USAGE
namespace Prowl.Veldrid;

/// <summary>
/// Mutable, interlocked-friendly backing storage for a single profiling bin. Distinct from
/// the public read-only <see cref="ProfileCounter"/> so its fields can be passed by reference
/// to <see cref="System.Threading.Interlocked"/>.
/// </summary>
internal struct ProfileCell
{
    public long Count;
    public long Bytes;
}
#endif
