#version 460 core

layout(location = 0) in vec2 uv;

layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform sampler2D lowerMip;
layout(set = 0, binding = 1) uniform sampler2D currentMip;

layout(push_constant) uniform UpsampleParams
{
    vec2 texelSize;
    float radius;
} params;

void main()
{
    vec2 t = params.texelSize * params.radius;

    vec3 a = texture(lowerMip, uv + vec2(-t.x, -t.y)).rgb;
    vec3 b = texture(lowerMip, uv + vec2( 0.0, -t.y)).rgb;
    vec3 c = texture(lowerMip, uv + vec2( t.x, -t.y)).rgb;

    vec3 d = texture(lowerMip, uv + vec2(-t.x, 0.0)).rgb;
    vec3 e = texture(lowerMip, uv).rgb;
    vec3 f = texture(lowerMip, uv + vec2( t.x, 0.0)).rgb;

    vec3 g = texture(lowerMip, uv + vec2(-t.x,  t.y)).rgb;
    vec3 h = texture(lowerMip, uv + vec2( 0.0,  t.y)).rgb;
    vec3 i = texture(lowerMip, uv + vec2( t.x,  t.y)).rgb;

    vec3 upsampled = (a + c + g + i) * (1.0 / 16.0)
                    + (b + d + f + h) * (2.0 / 16.0)
                    + e * (4.0 / 16.0);

    vec3 accumulated = texture(currentMip, uv).rgb;

    outColor = vec4(accumulated + upsampled, 1.0);
}
