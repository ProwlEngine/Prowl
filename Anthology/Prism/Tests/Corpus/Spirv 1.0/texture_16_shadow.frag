#version 460

layout(set=0, binding=0) uniform sampler2DShadow texShadow;

layout(location=0) out vec4 fragColor;

void main() {
    vec2 uv = gl_FragCoord.xy * 0.001;
    float depth = gl_FragCoord.z;
    float a = texture(texShadow, vec3(uv, depth));
    float b = textureProj(texShadow, vec4(uv, depth, 2.0));
    fragColor = vec4(a, b, 0.0, 1.0);
}
