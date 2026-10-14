#version 460 core

// sampler2DRectShadow: unnormalized-coordinate depth-compare sampling returns a scalar.
layout(set = 0, binding = 0) uniform sampler2DRectShadow tex;

layout(location = 0) in vec3 uvw;
layout(location = 0) out vec4 outColor;

void main() {
    float a = texture(tex, uvw);
    outColor = vec4(a, a, a, 1.0);
}
