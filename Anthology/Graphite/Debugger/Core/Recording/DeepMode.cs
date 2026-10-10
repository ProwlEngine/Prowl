namespace Prowl.Graphite.Debugger;

/// <summary>What a deep recording copies.</summary>
public enum DeepMode : byte
{
    /// <summary>Unreproducible first references before each pass, every pass output after it.</summary>
    Full,
    /// <summary>Unreproducible first references only.</summary>
    ReplayOnly,
}
