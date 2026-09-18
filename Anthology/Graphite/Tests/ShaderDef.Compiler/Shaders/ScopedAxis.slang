// Criterion: a module that declares variant axes and helpers reading them. Imported by some passes
// and not others, so axis discovery can be checked for leaking across unrelated passes in a session.
import VariantAttributes;

module ScopedAxis;

public enum ScopedMode { Off, Low, High }

[VariantAxis]
extern public static const bool SCOPED_FLAG;

[VariantAxis]
extern public static const ScopedMode SCOPED_MODE;

public float4 ReadsFlag(float4 c)
{
    return SCOPED_FLAG ? c * 2.0 : c;
}

public float4 ReadsMode(float4 c)
{
    switch (SCOPED_MODE)
    {
        case ScopedMode.High: return c * 4.0;
        case ScopedMode.Low: return c * 2.0;
        default: return c;
    }
}

public float4 ReadsNothing(float4 c)
{
    return c + 1.0;
}
