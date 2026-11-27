#version 460
#extension GL_ARB_enhanced_layouts : enable

out B {
    layout(location = 2) vec3 a;
    layout(location = 4) vec2 b;
};

void main() {
    a = vec3(1.0);
    b = vec2(2.0);
    gl_Position = vec4(0.0, 0.0, 0.0, 1.0);
}
