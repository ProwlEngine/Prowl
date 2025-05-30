
namespace Prowl.Slang;


/// <summary>
/// Slang diagnostic information.
/// </summary>
public struct DiagnosticInfo(string? message)
{
    /// <summary>
    /// The diagnostic message.
    /// </summary>
    public string? Message = message;
}