#version 460 core

// SSR - Combine pass
// Fresnel-weighted blend over the scene (albedo approximated from the tonemapped scene).

layout(std140, set = 0, binding = 0) uniform GlobalUniforms
{
    mat4 prowl_MatV;
    mat4 prowl_MatIV;
    mat4 prowl_MatP;
    mat4 prowl_MatVP;
    mat4 prowl_PrevViewProj;
    mat4 prowl_MatIP;
    mat4 prowl_MatIVP;
    mat4 prowl_MatVP_NonJittered;
    vec3 _WorldSpaceCameraPos;
    float _padding0;
    vec4 _ProjectionParams;
    vec4 _ScreenParams;
    vec2 _CameraJitter;
    vec2 _CameraPreviousJitter;
    vec4 _Time;
    vec4 _SinTime;
    vec4 _CosTime;
    vec4 prowl_DeltaTime;
};

#define PROWL_MATRIX_I_P prowl_MatIP

layout(std140, set = 2, binding = 0) uniform PassUniforms
{
    int _UseFresnel;
};

layout(set = 2, binding = 1) uniform sampler2D _MainTex;          // scene colour
layout(set = 2, binding = 2) uniform sampler2D _ReflectionBuffer; // resolved (+temporal) reflection
layout(set = 2, binding = 3) uniform sampler2D _CameraDepthTexture;
layout(set = 2, binding = 4) uniform sampler2D _CameraNormalsTexture;
layout(set = 2, binding = 5) uniform sampler2D _CameraMotionVectorsTexture; // .b roughness, .a metallic

layout(location = 1) in vec2 TexCoords;
layout(location = 0) out vec4 fragColor;

// ---- ProwlCG: view-space reconstruction from screen-space depth ----
vec3 projectAndDivide(mat4 matrix, vec3 pos)
{
    vec4 p = matrix * vec4(pos, 1.0);
    return p.xyz / p.w;
}

vec3 getScreenPos(vec2 tc, float depth)
{
    return vec3(tc, depth);
}

vec3 getNDCFromScreenPos(vec3 screenPos)
{
    return screenPos * 2.0 - 1.0;
}

vec3 getViewFromScreenPos(vec3 screenPos)
{
    return projectAndDivide(PROWL_MATRIX_I_P, getNDCFromScreenPos(screenPos));
}

vec3 getViewPos(vec2 tc, float depth)
{
    return getViewFromScreenPos(getScreenPos(tc, depth));
}

// Environment BRDF approximation (Karis "mobile") for the specular reflection response.
vec3 envBRDFApprox(vec3 F0, float roughness, float NdotV)
{
    vec4 c0 = vec4(-1.0, -0.0275, -0.572, 0.022);
    vec4 c1 = vec4(1.0, 0.0425, 1.04, -0.04);
    vec4 r = roughness * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * NdotV)) * r.x + r.y;
    vec2 ab = vec2(-1.04, 1.04) * a004 + r.zw;
    return F0 * ab.x + ab.y;
}

void main()
{
    vec3 sceneColor = texture(_MainTex, TexCoords).rgb;
    vec4 reflection = texture(_ReflectionBuffer, TexCoords);

    float roughness = texture(_CameraMotionVectorsTexture, TexCoords).b;
    float metallic = texture(_CameraMotionVectorsTexture, TexCoords).a;
    vec3 viewNormal = normalize(texture(_CameraNormalsTexture, TexCoords).xyz * 2.0 - 1.0);
    float depth = texture(_CameraDepthTexture, TexCoords).r;
    vec3 V = normalize(-getViewPos(TexCoords, depth));
    float NdotV = clamp(dot(viewNormal, V), 0.0, 1.0);

    // Forward SSR has no G-buffer albedo: approximate it from the tonemapped scene colour.
    vec3 approxAlbedo = sceneColor / (1.0 + sceneColor);
    vec3 F0 = mix(vec3(0.04), approxAlbedo, metallic);

    float mask = reflection.a * reflection.a;
    vec3 refl = reflection.rgb;
    if (_UseFresnel == 1)
        refl *= envBRDFApprox(F0, roughness, NdotV);
    // Guard against runaway HDR and the one-bounce feedback loop diverging when Fresnel
    // (which normally attenuates each bounce) is disabled.
    refl = min(refl, vec3(8.0));

    fragColor = vec4(sceneColor + refl * mask, 1.0);
}
