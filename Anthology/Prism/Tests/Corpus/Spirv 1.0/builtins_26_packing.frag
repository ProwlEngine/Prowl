#version 450
layout(location = 0) in vec2 vUV;
layout(location = 0) out vec4 oColor;
void main() {
    uint packed = packHalf2x16(vUV);
    vec2 unpacked = unpackHalf2x16(packed);
    uint un = packUnorm4x8(vec4(vUV, 0.5, 1.0));
    vec4 back = unpackUnorm4x8(un);
    oColor = vec4(unpacked, back.z + back.w, float(packed & 0xFFu) / 255.0);
}
