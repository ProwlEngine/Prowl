#version 450
// A value array inside a uniform block (not a struct array; plain vec4 elements).
layout(set = 0, binding = 0) uniform Palette {
    vec4 colors[8];
    int count;
} palette;
layout(location = 0) flat in int vIndex;
layout(location = 0) out vec4 oColor;
void main() {
    int i = clamp(vIndex, 0, palette.count - 1);
    oColor = palette.colors[i];
}
