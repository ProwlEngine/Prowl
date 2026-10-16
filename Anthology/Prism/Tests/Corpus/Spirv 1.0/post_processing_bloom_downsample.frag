#version 460 core

layout(location = 0) in vec2 uv;

layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform sampler2D sourceColor;

layout(set = 0, binding = 1) uniform DownsampleParams
{
    vec2 texelSize;
} params;

void main()
{
    vec2 ts = params.texelSize;

    // center tap
    vec3 center = texture(sourceColor, uv).rgb;

    // 4 inner corner taps, one texel out on the diagonal
    vec3 innerA = texture(sourceColor, uv + vec2(-ts.x, -ts.y)).rgb;
    vec3 innerB = texture(sourceColor, uv + vec2( ts.x, -ts.y)).rgb;
    vec3 innerC = texture(sourceColor, uv + vec2(-ts.x,  ts.y)).rgb;
    vec3 innerD = texture(sourceColor, uv + vec2( ts.x,  ts.y)).rgb;

    // 8 outer ring taps, two texels out, forming the wide 3x3 minus corners/center pattern
    vec3 outerA = texture(sourceColor, uv + vec2(-2.0 * ts.x, -2.0 * ts.y)).rgb;
    vec3 outerB = texture(sourceColor, uv + vec2( 0.0,        -2.0 * ts.y)).rgb;
    vec3 outerC = texture(sourceColor, uv + vec2( 2.0 * ts.x, -2.0 * ts.y)).rgb;
    vec3 outerD = texture(sourceColor, uv + vec2(-2.0 * ts.x,  0.0)).rgb;
    vec3 outerE = texture(sourceColor, uv + vec2( 2.0 * ts.x,  0.0)).rgb;
    vec3 outerF = texture(sourceColor, uv + vec2(-2.0 * ts.x,  2.0 * ts.y)).rgb;
    vec3 outerG = texture(sourceColor, uv + vec2( 0.0,         2.0 * ts.y)).rgb;
    vec3 outerH = texture(sourceColor, uv + vec2( 2.0 * ts.x,  2.0 * ts.y)).rgb;

    const float centerWeight = 0.125;
    const float innerWeight = 0.125;
    const float outerWeight = 0.0625;

    vec3 sum = center * centerWeight
             + (innerA + innerB + innerC + innerD) * innerWeight
             + (outerA + outerB + outerC + outerD + outerE + outerF + outerG + outerH) * outerWeight;

    float totalWeight = centerWeight + 4.0 * innerWeight + 8.0 * outerWeight;

    outColor = vec4(sum / totalWeight, 1.0);
}
