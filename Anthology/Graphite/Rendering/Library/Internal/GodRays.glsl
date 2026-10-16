// Prowl's god rays, out of its cinematic pass into an HDR effect of its own: bright sky pixels sampled along the line to
// the sun's place on screen, decaying with each step, added as warm light shafts. The sun is the world's, a far point
// against the light's direction from the camera.

#include "Graphite/Internal/PostView.glsl"

uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform vec3 _SunDirection;
uniform float _GodRayIntensity;
uniform float _GodRayDecay;
uniform float _GodRayDensity;
uniform float _GodRayWeight;
uniform int _GodRaySamples;
uniform float _GodRayThreshold;

void main()
{
    Graphite_ViewData view = Graphite_PostView();
    vec4 clip = view.viewProjection * vec4(-_SunDirection * 10000.0, 1.0);
    vec2 sunUV = vec2(clip.x / clip.w * 0.5 + 0.5, 0.5 - clip.y / clip.w * 0.5);

    vec4 color = Graphite_SampleSource(vUV);
    vec2 deltaUV = (vUV - sunUV) * _GodRayDensity / float(_GodRaySamples);
    vec2 sampleUV = vUV;
    float illumination = 0.0;
    float decay = 1.0;
    int sampleCount = min(_GodRaySamples, 128);
    for (int i = 0; i < sampleCount; i++)
    {
        sampleUV -= deltaUV;
        vec2 clamped = clamp(sampleUV, 0.001, 0.999);
        float sky = Graphite_Sample(_CameraDepthTexture, clamped).r <= 0.0001 ? 1.0 : 0.0;
        float brightness = dot(Graphite_SampleSource(clamped).rgb, vec3(0.2126, 0.7152, 0.0722));
        illumination += sky * step(_GodRayThreshold, brightness) * decay * _GodRayWeight;
        decay *= _GodRayDecay;
    }
    outColor = vec4(color.rgb + illumination * _GodRayIntensity * vec3(1.0, 0.95, 0.85), color.a);
}
