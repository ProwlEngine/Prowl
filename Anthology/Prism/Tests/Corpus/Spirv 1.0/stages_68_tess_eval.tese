#version 450
layout(triangles, equal_spacing, cw) in;
layout(location = 0) in vec3 tcPos[];
layout(location = 0) out vec3 tePos;
void main() {
    vec3 p = gl_TessCoord.x * tcPos[0]
           + gl_TessCoord.y * tcPos[1]
           + gl_TessCoord.z * tcPos[2];
    tePos = p;
    gl_Position = vec4(p, 1.0);
}
