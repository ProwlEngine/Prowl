#version 450
layout(location = 0) flat in uint vBits;
layout(location = 0) out vec4 oColor;
void main() {
    uint a = vBits & 0xF0u;
    uint b = vBits | 0x0Fu;
    uint c = vBits ^ 0xAAu;
    uint d = (vBits << 2) >> 1;
    oColor = vec4(float(a), float(b), float(c), float(d));
}
