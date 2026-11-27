namespace Prowl.Graphite;

/// <summary>
/// Format expected by indirect dispatch commands in an indirect buffer.
/// </summary>
public struct IndirectDispatchArguments
{
    /// <summary>
    /// X group count, same as Dispatch's.
    /// </summary>
    public uint GroupCountX;
    /// <summary>
    /// Y group count, same as Dispatch's.
    /// </summary>
    public uint GroupCountY;
    /// <summary>
    /// Z group count, same as Dispatch's.
    /// </summary>
    public uint GroupCountZ;
}
