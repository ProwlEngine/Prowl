#version 460
layout(location=0) out vec4 fragColor;

void main() {
    bool p = 3 < 5;       // true
    bool q = 10 == 10;    // true
    bool r = 7 >= 8;      // false
    bool t = 2.5 != 2.5;  // false
    fragColor = vec4(p ? 1.0 : 0.0, q ? 1.0 : 0.0, r ? 0.0 : 1.0, t ? 0.0 : 1.0);
}
