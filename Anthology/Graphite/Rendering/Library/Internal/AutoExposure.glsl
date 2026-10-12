// Prowl's auto exposure: the log luminance of the colour at half resolution, box downsampled to a few texels, adapted
// over time toward their geometric mean with separate speeds up and down, then applied as a multiply. Measuring reads
// the first layer only, so a layered view's layers share one exposure.

float Luminance(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

#if defined(AE_EXTRACT)
#ifdef AE_LAYERED_SOURCE
uniform sampler2DArray _MainTex;
vec3 SampleMain(vec2 uv) { return texture(_MainTex, vec3(uv, 0.0)).rgb; }
vec2 MainSize() { return vec2(textureSize(_MainTex, 0).xy); }
#else
uniform sampler2D _MainTex;
vec3 SampleMain(vec2 uv) { return texture(_MainTex, uv).rgb; }
vec2 MainSize() { return vec2(textureSize(_MainTex, 0)); }
#endif

void main()
{
    vec2 texel = 1.0 / MainSize();
    float l0 = log(max(Luminance(SampleMain(vUV + vec2(-0.5, -0.5) * texel)), 0.0001));
    float l1 = log(max(Luminance(SampleMain(vUV + vec2(0.5, -0.5) * texel)), 0.0001));
    float l2 = log(max(Luminance(SampleMain(vUV + vec2(-0.5, 0.5) * texel)), 0.0001));
    float l3 = log(max(Luminance(SampleMain(vUV + vec2(0.5, 0.5) * texel)), 0.0001));
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
#elif defined(AE_ADAPT)
uniform sampler2D _MainTex;
uniform sampler2D _AdaptedTex;
uniform float _AdaptSpeedUp;
uniform float _AdaptSpeedDown;
uniform float _HistoryValid;
uniform float _DeltaTime;

void main()
{
    float currentLum = exp(texture(_MainTex, vec2(0.5)).r);
    float previousLum = texture(_AdaptedTex, vec2(0.5)).r;
    if (_HistoryValid < 0.5)
    {
        outColor = vec4(currentLum, 0.0, 0.0, 1.0);
        return;
    }
    float speed = currentLum > previousLum ? _AdaptSpeedUp : _AdaptSpeedDown;
    float adapted = previousLum + (currentLum - previousLum) * (1.0 - exp(-_DeltaTime * speed));
    outColor = vec4(clamp(adapted, 0.0001, 100.0), 0.0, 0.0, 1.0);
}
#else
uniform sampler2D _AdaptedTex;
uniform float _ExposureComp;
uniform float _MinExposure;
uniform float _MaxExposure;

void main()
{
    vec4 color = Graphite_SampleSource(vUV);
    float exposure = 0.18 / max(texture(_AdaptedTex, vec2(0.5)).r, 0.0001);
    exposure = clamp(exposure * exp2(_ExposureComp), _MinExposure, _MaxExposure);
    outColor = vec4(color.rgb * exposure, color.a);
}
#endif
