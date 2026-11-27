#version 460

layout(quads, equal_spacing, ccw) in;

void main() {
    float scale = gl_TessLevelInner[0] + gl_TessLevelOuter[2];

    vec4 a = mix(gl_in[0].gl_Position, gl_in[1].gl_Position, gl_TessCoord.x);
    vec4 b = mix(gl_in[2].gl_Position, gl_in[3].gl_Position, gl_TessCoord.x);
    vec4 p = mix(a, b, gl_TessCoord.y);

    p.z += scale * 0.01;
    gl_Position = p;
}
