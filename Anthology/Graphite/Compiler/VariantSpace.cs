using System.Collections.Generic;

namespace Prowl.Graphite.Compiler;


public readonly record struct VariantSpace(string Name, string DeclType, IReadOnlyList<string> Values) { }
