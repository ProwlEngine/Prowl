#version 460

layout(triangles, equal_spacing, cw) in;

layout(location = 0) in vec3 teColor[];

layout(location = 0) out vec3 fragColor;

void main() {
    fragColor = gl_TessCoord.x * teColor[0]
              + gl_TessCoord.y * teColor[1]
              + gl_TessCoord.z * teColor[2];

    gl_Position = gl_TessCoord.x * gl_in[0].gl_Position
                + gl_TessCoord.y * gl_in[1].gl_Position
                + gl_TessCoord.z * gl_in[2].gl_Position;
}
