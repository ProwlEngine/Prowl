#version 450
layout(location = 0) in vec2 vUV;
layout(location = 0) out vec4 oColor;
void main() {
    bool a = vUV.x > 0.5;
    bool b = vUV.y < 0.5;
    bool c = a && b;
    bool d = a || b;
    bool e = !c;
    oColor = vec4(c ? 1.0 : 0.0, d ? 1.0 : 0.0, e ? 1.0 : 0.0, 1.0);
}
