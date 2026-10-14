#version 460

layout(triangles, equal_spacing, cw) in;

void main() {
    vec4 acc = vec4(0.0);
    for (int i = 0; i < 3; ++i) {
        acc += gl_TessCoord[i] * gl_in[i].gl_Position;
    }
    gl_Position = acc;
}
