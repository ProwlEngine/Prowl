#version 460

layout(triangles, equal_spacing, cw) in;

layout(location = 0) patch in vec3 patchData;

layout(location = 0) out vec3 fragColor;

void main() {
    fragColor = patchData;

    gl_Position = gl_TessCoord.x * gl_in[0].gl_Position
                + gl_TessCoord.y * gl_in[1].gl_Position
                + gl_TessCoord.z * gl_in[2].gl_Position;
}
