#pragma variant AE EXPOSURE EXTRACT DOWNSAMPLE HISTOGRAM ADAPT
// Auto exposure with a histogram meter: the log luminance of the scene at half resolution, its pre-exposure
// undone, box downsampled to about 64 texels across, binned, averaged between a low and a high percentile so a dark sky
// or a few highlights do not pull it, adapted over time with separate speeds up and down, then metered into the
// exposure the view takes next frame. Measuring reads the first layer only, so a layered view's layers share one exposure.
#include "Graphite/Internal/PostView.glsl"

#define AE_BINS 64


#if defined(AE_EXTRACT)
uniform GRAPHITE_SCREEN_TEXTURE _MainTex;
vec3 SampleMain(vec2 uv) { return Graphite_Sample(_MainTex, uv).rgb; }
vec2 MainSize() { return vec2(Graphite_Size(_MainTex, 0)); }

// A texel's log luminance with the pre-exposure undone. A half float that overflowed reads as the largest one, a lower
// bound on its light, and NaN as dark, so neither can poison the mean.
float LogLuminance(vec2 uv, float scale)
{
    float l = Graphite_Luminance(SampleMain(uv));
    l = isnan(l) ? 0.0 : min(l, 65504.0);
    return log(max(l * scale, 0.0001));
}

void main()
{
    vec2 texel = 1.0 / MainSize();
    float scale = 1.0 / max(graphite_PostViews[0].exposure.x, 1e-30);
    float l0 = LogLuminance(vUV + vec2(-0.5, -0.5) * texel, scale);
    float l1 = LogLuminance(vUV + vec2(0.5, -0.5) * texel, scale);
    float l2 = LogLuminance(vUV + vec2(-0.5, 0.5) * texel, scale);
    float l3 = LogLuminance(vUV + vec2(0.5, 0.5) * texel, scale);
    outColor = vec4((l0 + l1 + l2 + l3) * 0.25, 0.0, 0.0, 1.0);
}
#elif defined(AE_DOWNSAMPLE)
uniform sampler2D _MainTex;

void main()
{
    vec2 texel = 1.0 / vec2(textureSize(_MainTex, 0));
    float s0 = texture(_MainTex, vUV + vec2(-0.5, -0.5) * texel).r;
    float s1 = texture(_MainTex, vUV + vec2(0.5, -0.5) * texel).r;
    float s2 = texture(_MainTex, vUV + vec2(-0.5, 0.5) * texel).r;
    float s3 = texture(_MainTex, vUV + vec2(0.5, 0.5) * texel).r;
    outColor = vec4((s0 + s1 + s2 + s3) * 0.25, 0.0, 0.0, 1.0);
}
#elif defined(AE_HISTOGRAM)
uniform sampler2D _MainTex;
uniform float _LogMin;
uniform float _LogMax;

// Bin x of the log luminances: the share of texels in it and their mean
void main()
{
    int bin = int(gl_FragCoord.x);
    ivec2 size = textureSize(_MainTex, 0);
    float scale = float(AE_BINS) / (_LogMax - _LogMin);
    float count = 0.0;
    float sum = 0.0;
    for (int y = 0; y < size.y; y++)
    {
        for (int x = 0; x < size.x; x++)
        {
            float l = texelFetch(_MainTex, ivec2(x, y), 0).r;
            if (clamp(int(floor((l - _LogMin) * scale)), 0, AE_BINS - 1) == bin)
            {
                count += 1.0;
                sum += l;
            }
        }
    }
    outColor = vec4(count / float(size.x * size.y), count > 0.0 ? sum / count : 0.0, 0.0, 1.0);
}
#elif defined(AE_ADAPT)
uniform sampler2D _MainTex;
uniform sampler2D _AdaptedTex;
uniform float _AdaptSpeedUp;
uniform float _AdaptSpeedDown;
uniform float _HistoryValid;
uniform float _DeltaTime;
uniform float _LowPercent;
uniform float _HighPercent;

// The mean log luminance of the texels between the two percentiles, from each bin's own mean
float MeteredLogLuminance()
{
    float below = 0.0;
    float weight = 0.0;
    float sum = 0.0;
    for (int b = 0; b < AE_BINS; b++)
    {
        vec2 bin = texelFetch(_MainTex, ivec2(b, 0), 0).rg;
        float share = max(min(below + bin.x, _HighPercent) - max(below, _LowPercent), 0.0);
        sum += share * bin.y;
        weight += share;
        below += bin.x;
    }
    return weight > 0.0 ? sum / weight : log(0.0001);
}

void main()
{
    float currentLum = exp(MeteredLogLuminance());
    float previousLum = texture(_AdaptedTex, vec2(0.5)).r;
    if (_HistoryValid < 0.5 || isnan(previousLum) || isinf(previousLum))
    {
        outColor = vec4(currentLum, 0.0, 0.0, 1.0);
        return;
    }
    float speed = currentLum > previousLum ? _AdaptSpeedUp : _AdaptSpeedDown;
    // In stops, so a change of many stops settles as fast as a small one
    float previousLog = log(max(previousLum, 0.0001));
    float adapted = exp(previousLog + (log(currentLum) - previousLog) * (1.0 - exp(-_DeltaTime * speed)));
    outColor = vec4(max(adapted, 0.0001), 0.0, 0.0, 1.0);
}
#else
uniform sampler2D _AdaptedTex;
uniform float _ExposureComp;
uniform float _MinEV100;
uniform float _MaxEV100;

// A reflected light meter of K 12.5 at ISO 100 over the adapted luminance, brightened by the compensation
void main()
{
    float luminance = max(texture(_AdaptedTex, vec2(0.5)).r, 0.0001);
    float ev100 = clamp(log2(luminance * 100.0 / 12.5) - _ExposureComp, _MinEV100, _MaxEV100);
    outColor = vec4(1.0 / (1.2 * exp2(ev100)), 0.0, 0.0, 1.0);
}
#endif
