#version 460

layout(vertices = 3) out;

void main() {
    float id = float(gl_InvocationID) + float(gl_PrimitiveID);

    gl_out[gl_InvocationID].gl_Position =
        gl_in[gl_InvocationID].gl_Position + vec4(id, 0.0, 0.0, 0.0);

    gl_TessLevelInner[0] = 1.0;
    gl_TessLevelOuter[0] = 1.0;
    gl_TessLevelOuter[1] = 1.0;
    gl_TessLevelOuter[2] = 1.0;
}
