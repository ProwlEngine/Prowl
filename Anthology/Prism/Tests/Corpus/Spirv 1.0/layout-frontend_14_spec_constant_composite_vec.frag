#version 460 core

// Composite specialization constant: a vec4 assembled from scalar spec constants.

layout(constant_id = 0) const float R = 1.0;
layout(constant_id = 1) const float G = 0.5;
layout(constant_id = 2) const float B = 0.25;
layout(constant_id = 3) const int COUNT = 2;

layout(location = 0) in vec4 v_color;
layout(location = 0) out vec4 fragColor;

void main()
{
    vec4 tint = vec4(R, G, B, 1.0);
    ivec4 counts = ivec4(COUNT, COUNT + 1, COUNT * 2, COUNT - 1);
    fragColor = v_color * tint + vec4(counts) * 0.01;
}
