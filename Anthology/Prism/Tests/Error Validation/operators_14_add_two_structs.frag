#version 450
// Arithmetic addition is applied to two struct values which is not allowed
layout(location=0) out vec4 outColor;
struct Data {
    float a;
    float b;
};
void main() {
    Data x = Data(1.0, 2.0);
    Data y = Data(3.0, 4.0);
    Data z = x + y;
    outColor = vec4(z.a, z.b, 0.0, 1.0);
}
