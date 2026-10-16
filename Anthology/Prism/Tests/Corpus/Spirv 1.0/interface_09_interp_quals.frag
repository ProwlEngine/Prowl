#version 460

layout(location = 0) noperspective in float a;
layout(location = 1) centroid in vec2 b;
layout(location = 2) sample in vec3 c;
layout(location = 3) flat in int d;

layout(location = 0) out vec4 color;

void main() {
    color = vec4(a + b.x + c.x + float(d));
}
