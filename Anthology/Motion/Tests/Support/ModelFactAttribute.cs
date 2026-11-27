using System.Runtime.CompilerServices;

namespace Prowl.Motion.Tests;

/// <summary>A fact that needs the local humanoid test models.</summary>
public sealed class ModelFactAttribute : FactAttribute
{
    public ModelFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
      : base(sourceFilePath, sourceLineNumber)
    {
        if (!TestAssets.ModelsPresent)
            Skip = "Test models missing, see Tests/Models";
    }
}

/// <summary>A theory that needs the local humanoid test models.</summary>
public sealed class ModelTheoryAttribute : TheoryAttribute
{
    public ModelTheoryAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
      : base(sourceFilePath, sourceLineNumber)
    {
        if (!TestAssets.ModelsPresent)
            Skip = "Test models missing, see Tests/Models";
    }
}
