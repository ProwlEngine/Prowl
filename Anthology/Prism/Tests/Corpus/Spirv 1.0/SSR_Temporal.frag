#version 460 core

// SSR - Temporal pass
// Reproject + neighbourhood-clamp the reflection against a history buffer.

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

#define PROWL_MATRIX_I_VP prowl_MatIVP
#define PROWL_MATRIX_VP_PREVIOUS prowl_PrevViewProj

layout(std140, set = 2, binding = 0) uniform PassUniforms
{
    int _ReflectionVelocity;  // 1 = derive velocity from reflection hit depth
    float _TScale;
    float _TResponse;
};

layout(set = 2, binding = 1) uniform sampler2D _MainTex;        // current resolved reflection
layout(set = 2, binding = 2) uniform sampler2D _PreviousBuffer; // reflection history
layout(set = 2, binding = 3) uniform sampler2D _RayCast;
layout(set = 2, binding = 4) uniform sampler2D _CameraDepthTexture;
layout(set = 2, binding = 5) uniform sampler2D _CameraMotionVectorsTexture; // .rg motion

layout(location = 1) in vec2 TexCoords;
layout(location = 0) out vec4 fragColor;

// Reproject this pixel through the previous view-projection to get screen-space motion.
vec2 reflectionVelocity(vec2 uv)
{
    // Use the reflection hit point's depth so the history follows the reflected geometry.
    float hitDepthW = texture(_RayCast, uv).w > 0.0 ? texture(_CameraDepthTexture, texture(_RayCast, uv).xy).r
                                                    : texture(_CameraDepthTexture, uv).r;
    vec4 clip = vec4(uv * 2.0 - 1.0, hitDepthW * 2.0 - 1.0, 1.0);
    vec4 world = PROWL_MATRIX_I_VP * clip;
    world /= world.w;
    vec4 prevClip = PROWL_MATRIX_VP_PREVIOUS * world;
    vec2 prevUV = (prevClip.xy / prevClip.w) * 0.5 + 0.5;
    return uv - prevUV;
}

void main()
{
    vec2 velocity = _ReflectionVelocity == 1 ? reflectionVelocity(TexCoords)
                                             : texture(_CameraMotionVectorsTexture, TexCoords).rg;
    vec4 current = texture(_MainTex, TexCoords);
    vec4 previous = texture(_PreviousBuffer, TexCoords - velocity);

    vec2 du = vec2(1.0 / _ScreenParams.x, 0.0);
    vec2 dv = vec2(0.0, 1.0 / _ScreenParams.y);
    vec4 mn = current, mx = current;
    for (int x = -1; x <= 1; x++)
    for (int y = -1; y <= 1; y++)
    {
        vec4 c = texture(_MainTex, TexCoords + du * float(x) + dv * float(y));
        mn = min(mn, c); mx = max(mx, c);
    }
    vec4 center = (mn + mx) * 0.5;
    mn = (mn - center) * _TScale + center;
    mx = (mx - center) * _TScale + center;
    previous = clamp(previous, mn, mx);

    fragColor = mix(current, previous, clamp(_TResponse * (1.0 - length(velocity) * 8.0), 0.0, 1.0));
}
