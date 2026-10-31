// Graphite/PBR with what the demo scenes add on top: a world grid darkening the base colour and the instance colour
// multiplying it, each on while its property is.
Shader "PBRRenderer/Demo"
{
    Properties
    {
        _Grid("Grid", Float) = 0
        _InstanceColor("Instance Color", Float) = 0
    }

    Surface "Lit"
    {
        GLSLPROGRAM
        uniform float _Grid;
        uniform float _InstanceColor;

        void Surface(inout PBR_Params p, Graphite_SurfaceData i)
        {
            if (_InstanceColor > 0.5)
                p.baseColor *= Graphite_Object().custom0.rgb;
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
