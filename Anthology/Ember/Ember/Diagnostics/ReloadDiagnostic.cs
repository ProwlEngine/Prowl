using System;

namespace Prowl.Ember;

/// <summary>
/// One thing that happened during a reload. <see cref="Subject"/> is a stable identifier for what the entry
/// is about (a type full name, a member name, an assembly simple name) so a host can group and suppress
/// without parsing messages.
/// </summary>
public readonly record struct ReloadDiagnostic(
    ReloadCode Code,
    ReloadSeverity Severity,
    string Message,
    string? Subject)
{
    public string Id => $"EMB{(int)Code:D4}";

    public override string ToString()
        => Subject == null ? $"{Id} {Message}" : $"{Id} {Subject}: {Message}";
}

/// <summary>An additional live stream of diagnostics. Everything is collected into the report regardless.</summary>
public interface IDiagnosticSink
{
    void Report(in ReloadDiagnostic diagnostic);
}

public sealed class DelegateDiagnosticSink : IDiagnosticSink
{
    private readonly Action<ReloadDiagnostic> _callback;

    public DelegateDiagnosticSink(Action<ReloadDiagnostic> callback)
        => _callback = callback ?? throw new ArgumentNullException(nameof(callback));

    public void Report(in ReloadDiagnostic diagnostic) => _callback(diagnostic);
}

public sealed class FilteringDiagnosticSink : IDiagnosticSink
{
    private readonly IDiagnosticSink _inner;
    private readonly ReloadSeverity _minimum;

    public FilteringDiagnosticSink(IDiagnosticSink inner, ReloadSeverity minimum)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _minimum = minimum;
    }

    public void Report(in ReloadDiagnostic diagnostic)
    {
        if (diagnostic.Severity >= _minimum)
            _inner.Report(diagnostic);
    }
}
