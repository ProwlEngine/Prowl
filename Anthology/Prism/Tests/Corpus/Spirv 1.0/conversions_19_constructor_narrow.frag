#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    vec4 v;
    ivec3 iv;
};

void main() {
    ivec2 t = ivec2(v);
    vec2 c = vec2(iv);
    fragColor = vec4(float(t.x), float(t.y), c.x, c.y);
}
