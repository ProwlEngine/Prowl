namespace Prowl.Clay;

/// <summary>Severity of an <see cref="ImportLogEntry"/>.</summary>
public enum ImportLogSeverity
{
    /// <summary>Informational note. Always safe to ignore.</summary>
    Info,
    /// <summary>Recoverable issue. Import continued, but the asset may not be 100% faithful.</summary>
    Warning,
    /// <summary>Hard error. Surfaced as a thrown <see cref="ImportException"/> in most cases.</summary>
    Error,
}

/// <summary>
/// One entry in the import log: severity, a short message, optional source/format identifiers.
/// </summary>
public sealed class ImportLogEntry
{
    /// <summary>Severity bucket.</summary>
    public required ImportLogSeverity Severity { get; init; }

    /// <summary>Human-readable message.</summary>
    public required string Message { get; init; }

    /// <summary>Step or component that produced the entry, e.g. <c>"GltfFormat"</c>, <c>"Triangulate"</c>.</summary>
    public string? Source { get; init; }

    /// <inheritdoc />
    public override string ToString() =>
        Source is null ? $"[{Severity}] {Message}" : $"[{Severity}] [{Source}] {Message}";
}

/// <summary>
/// Collected log entries from one import call.
/// </summary>
public sealed class ImportLog
{
    private readonly List<ImportLogEntry> _entries = new();

    /// <summary>Logged entries, in the order they were emitted.</summary>
    public IReadOnlyList<ImportLogEntry> Entries => _entries;

    /// <summary>Optional sink that receives every log entry as it is added.</summary>
    public Action<ImportLogEntry>? Sink { get; init; }

    /// <summary>Adds an informational entry.</summary>
    public void Info(string message, string? source = null) => Add(ImportLogSeverity.Info, message, source);

    /// <summary>Adds a warning entry.</summary>
    public void Warning(string message, string? source = null) => Add(ImportLogSeverity.Warning, message, source);

    /// <summary>Adds an error entry (does not throw; throwing is the caller's job).</summary>
    public void Error(string message, string? source = null) => Add(ImportLogSeverity.Error, message, source);

    private void Add(ImportLogSeverity severity, string message, string? source)
    {
        var entry = new ImportLogEntry { Severity = severity, Message = message, Source = source };
        _entries.Add(entry);
        Sink?.Invoke(entry);
    }

    /// <summary>True when any <see cref="ImportLogSeverity.Warning"/> or <see cref="ImportLogSeverity.Error"/> was logged.</summary>
    public bool HasAnyWarnings
    {
        get
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var s = _entries[i].Severity;
                if (s == ImportLogSeverity.Warning || s == ImportLogSeverity.Error)
                    return true;
            }
            return false;
        }
    }
}
