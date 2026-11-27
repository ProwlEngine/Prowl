// Gives a view its exposure on the GPU while an AutoExposure drives it: the one it measured last frame, else the host's,
// with last frame's beside it for history, so no frame waits on a read back. Every layer of the view shares it.
Compute
{
    #include "Graphite/ViewData.glsl"

    layout(local_size_x = 1) in;

    uniform sampler2D Graphite_Measured;
    layout(std430) buffer Graphite_ExposureState { vec4 state; } exposure;
    layout(std430) buffer Graphite_ExposureViews { Graphite_ViewData views[]; };

    // flags: 1 take the measured exposure, 2 last frame's comes from the state, 4 there is no last frame
    layout(push_constant) uniform Push { float host; uint flags; uint layers; uint pad; } push;

    void main()
    {
        float measured = texelFetch(Graphite_Measured, ivec2(0), 0).r;
        bool valid = (push.flags & 1u) != 0u && measured > 0.0 && !isinf(measured) && !isnan(measured);
        float current = valid ? measured : push.host;
        for (uint layer = 0u; layer < push.layers; layer++)
        {
            float previous = (push.flags & 4u) != 0u ? current : (push.flags & 2u) != 0u ? exposure.state.x : views[layer].exposure.y;
            views[layer].exposure.xy = vec2(current, previous);
        }
        exposure.state = vec4(current, 0.0, 0.0, 0.0);
    }
}
