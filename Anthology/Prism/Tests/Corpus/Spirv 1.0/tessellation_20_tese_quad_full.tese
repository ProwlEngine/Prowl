#version 460

layout(quads, fractional_odd_spacing, ccw) in;

layout(location = 0) in vec3 teColor[];
layout(location = 1) patch in vec3 patchCenter;

layout(location = 0) out vec3 fragColor;

void main() {
    vec3 ca = mix(teColor[0], teColor[1], gl_TessCoord.x);
    vec3 cb = mix(teColor[2], teColor[3], gl_TessCoord.x);
    fragColor = mix(ca, cb, gl_TessCoord.y) + patchCenter;

    vec4 a = mix(gl_in[0].gl_Position, gl_in[1].gl_Position, gl_TessCoord.x);
    vec4 b = mix(gl_in[2].gl_Position, gl_in[3].gl_Position, gl_TessCoord.x);
    gl_Position = mix(a, b, gl_TessCoord.y);
}
