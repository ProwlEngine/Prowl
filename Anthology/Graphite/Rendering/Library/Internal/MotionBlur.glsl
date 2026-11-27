// Motion blur: the colour sampled along each pixel's motion, clamped to a largest radius, jittered by
// interleaved gradient noise, and weighted by how near each sample's depth is to the pixel's so background does not
// bleed over foreground.

#include "Graphite/Internal/PostView.glsl"

uniform GRAPHITE_SCREEN_TEXTURE _MotionVectorsTex;
uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform vec2 _Resolution;
uniform float _Intensity;
uniform int _Samples;
uniform float _MaxBlurRadius;

float IGN(vec2 pixel)
{
    const vec3 magic = vec3(0.06711056, 0.00583715, 52.9829189);
    return fract(magic.z * fract(dot(pixel, magic.xy)));
}

float LinearDepth(Graphite_ViewData view, vec2 uv)
{
    return abs(Graphite_PostViewPosition(view, uv, max(Graphite_Sample(_CameraDepthTexture, uv).r, 1e-7)).z);
}

void main()
{
    Graphite_ViewData view = Graphite_PostView();
    vec2 texelSize = 1.0 / _Resolution;
    vec4 center = Graphite_SampleSource(vUV);
    vec3 centerColor = center.rgb;
    vec2 motion = Graphite_Sample(_MotionVectorsTex, vUV).rg * _Intensity;
    float maxBlurUV = _MaxBlurRadius * max(texelSize.x, texelSize.y);
    float motionLen = length(motion);
    if (motionLen > maxBlurUV)
        motion *= maxBlurUV / motionLen;
    if (length(motion * _Resolution) < 0.5)
    {
        outColor = center;
        return;
    }

    float dither = IGN(gl_FragCoord.xy) - 0.5;
    int samples = max(_Samples, 1);
    float centerDepth = LinearDepth(view, vUV);
    vec3 acc = vec3(0.0);
    float totalWeight = 0.0;
    for (int i = 0; i < samples; i++)
    {
        float t = (float(i) + dither) / float(samples) - 0.5;
        vec2 sampleUV = clamp(vUV + motion * t, vec2(0.0), vec2(1.0));
        float depthWeight = 1.0 / (1.0 + abs(LinearDepth(view, sampleUV) - centerDepth) * 10.0);
        acc += Graphite_SampleSource(sampleUV).rgb * depthWeight;
        totalWeight += depthWeight;
    }
    outColor = vec4(acc / max(totalWeight, 1e-5), center.a);
}
