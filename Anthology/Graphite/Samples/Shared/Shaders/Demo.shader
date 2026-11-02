// Graphite/Lit with what the demo scenes add on top: a world grid darkening the base colour while _Grid is set.
Shader "PBRRenderer/Demo"
{
    Properties
    {
        _Grid("Grid", Float) = 0
    }

    Surface "Lit"
    {
        GLSLPROGRAM
        uniform float _Grid;

        void Surface(inout PBR_Params p, Graphite_SurfaceData i)
        {
            if (_Grid > 0.0)
            {
                vec2 g2 = Graphite_RelativeToWorld(i.relativePos).xz / _Grid;
                vec2 g = abs(fract(g2 - 0.5) - 0.5) / max(fwidth(g2), vec2(1e-4));
                p.baseColor *= mix(1.0, 0.5, 1.0 - min(min(g.x, g.y), 1.0));
            }
        }
        ENDGLSL
    }
}
