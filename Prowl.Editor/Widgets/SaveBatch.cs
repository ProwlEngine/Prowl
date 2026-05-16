using System.Collections.Generic;
using System.Text;
using Prowl.OrigamiUI;

namespace Prowl.Editor.Widgets;

/// <summary>
/// Per-frame accumulator for things saved this frame. Anything that reacts to Ctrl+S
/// (scene save, prefab save, graph save, ...) should call <see cref="Record"/> so a
/// single combined toast shows at end of frame instead of one toast per subsystem.
/// </summary>
public static class SaveBatch
{
    private static readonly List<string> _items = new();

    /// <summary>Record a human-readable label for something just saved.</summary>
    public static void Record(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return;
        _items.Add(label);
    }

    /// <summary>Compile recorded items into a single toast and clear the batch.</summary>
    public static void Flush()
    {
        if (_items.Count == 0) return;

        var sb = new StringBuilder();
        for (int i = 0; i < _items.Count; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append("• ").Append(_items[i]);
        }

        Toasts.Success("Saved the following:", sb.ToString());
        _items.Clear();
    }
}
