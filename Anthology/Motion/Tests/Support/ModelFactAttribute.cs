namespace Prowl.Motion.Tests;

/// <summary>A fact that needs the local humanoid test models.</summary>
public sealed class ModelFactAttribute : FactAttribute
{
    public ModelFactAttribute()
    {
        if (!TestAssets.ModelsPresent)
            Skip = "Test models missing, see Tests/Models";
    }
}

/// <summary>A theory that needs the local humanoid test models.</summary>
public sealed class ModelTheoryAttribute : TheoryAttribute
{
    public ModelTheoryAttribute()
    {
        if (!TestAssets.ModelsPresent)
            Skip = "Test models missing, see Tests/Models";
    }
}
