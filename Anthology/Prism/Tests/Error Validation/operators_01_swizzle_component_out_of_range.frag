#version 450
// Swizzle uses .w component on a vec2 which only has x and y
layout(location=0) out vec4 outColor;
void main() {
    vec2 v = vec2(1.0, 2.0);
    float f = v.w;
    outColor = vec4(f);
}
