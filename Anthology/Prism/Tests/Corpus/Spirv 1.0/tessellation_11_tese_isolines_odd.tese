#version 460

layout(isolines, fractional_odd_spacing) in;

void main() {
    vec4 p = mix(gl_in[0].gl_Position, gl_in[1].gl_Position, gl_TessCoord.x);
    p.y += gl_TessCoord.y;
    gl_Position = p;
}
