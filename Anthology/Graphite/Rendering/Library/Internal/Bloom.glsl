#pragma variant BLOOM COMPOSITE PREFILTER DOWNSAMPLE UPSAMPLE
// Dual filter bloom: a soft knee threshold into half resolution, a chain of 4x4 downsamples, upsamples that mix
// each level with the blurred one below by scatter, then the composite adding it to the colour.

float Luminance(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

#if defined(BLOOM_PREFILTER)
uniform float _Threshold;
uniform float _SoftKnee;
uniform float _Clamp;
uniform float _AntiFlicker;

// Keeps what is above the threshold, easing in across a quadratic knee
vec3 Prefilter(vec3 color)
{
    color = min(max(color, vec3(0.0)), vec3(_Clamp));
    float brightness = max(color.r, max(color.g, color.b));
    float knee = _Threshold * _SoftKnee + 1e-5;
    float soft = clamp(brightness - _Threshold + knee, 0.0, 2.0 * knee);
    soft = soft * soft / (4.0 * knee);
    return color * (max(soft, brightness - _Threshold) / max(brightness, 1e-5));
}

void main()
{
    vec2 texel = 1.0 / vec2(Graphite_Size(Graphite_Source, 0));
    vec2 offsets[4] = vec2[](vec2(-1.0, -1.0), vec2(1.0, -1.0), vec2(-1.0, 1.0), vec2(1.0, 1.0));
    vec3 sum = vec3(0.0);
    float weightSum = 0.0;
    for (int i = 0; i < 4; i++)
    {
        vec3 s = Prefilter(Graphite_SampleSource(vUV + offsets[i] * texel).rgb);
        float w = _AntiFlicker > 0.5 ? 1.0 / (1.0 + Luminance(s)) : 1.0;
        sum += s * w;
        weightSum += w;
    }
    outColor = vec4(sum / weightSum, 1.0);
}
#elif defined(BLOOM_DOWNSAMPLE)
uniform GRAPHITE_SCREEN_TEXTURE _MainTex;

void main()
{
    vec2 texel = 1.0 / vec2(Graphite_Size(_MainTex, 0));
    vec4 sum = Graphite_Sample(_MainTex, vUV) * 4.0;
    sum += Graphite_Sample(_MainTex, vUV - texel);
    sum += Graphite_Sample(_MainTex, vUV + texel);
    sum += Graphite_Sample(_MainTex, vUV + vec2(texel.x, -texel.y));
    sum += Graphite_Sample(_MainTex, vUV - vec2(texel.x, -texel.y));
    outColor = sum / 8.0;
}
#elif defined(BLOOM_UPSAMPLE)
uniform GRAPHITE_SCREEN_TEXTURE _MainTex;
uniform GRAPHITE_SCREEN_TEXTURE _HighTex;
uniform float _Scatter;

void main()
{
    vec2 halfpixel = 0.5 / vec2(Graphite_Size(_MainTex, 0));
    vec4 sum = Graphite_Sample(_MainTex, vUV + vec2(-halfpixel.x * 2.0, 0.0));
    sum += Graphite_Sample(_MainTex, vUV + vec2(-halfpixel.x, halfpixel.y)) * 2.0;
    sum += Graphite_Sample(_MainTex, vUV + vec2(0.0, halfpixel.y * 2.0));
    sum += Graphite_Sample(_MainTex, vUV + vec2(halfpixel.x, halfpixel.y)) * 2.0;
    sum += Graphite_Sample(_MainTex, vUV + vec2(halfpixel.x * 2.0, 0.0));
    sum += Graphite_Sample(_MainTex, vUV + vec2(halfpixel.x, -halfpixel.y)) * 2.0;
    sum += Graphite_Sample(_MainTex, vUV + vec2(0.0, -halfpixel.y * 2.0));
    sum += Graphite_Sample(_MainTex, vUV + vec2(-halfpixel.x, -halfpixel.y)) * 2.0;
    outColor = mix(Graphite_Sample(_HighTex, vUV), sum / 12.0, _Scatter);
}
#else
uniform GRAPHITE_SCREEN_TEXTURE _BloomTex;
uniform float _Intensity;

void main()
{
    vec4 color = Graphite_SampleSource(vUV);
    outColor = vec4(color.rgb + Graphite_Sample(_BloomTex, vUV).rgb * _Intensity * 2.0, color.a);
}
#endif
