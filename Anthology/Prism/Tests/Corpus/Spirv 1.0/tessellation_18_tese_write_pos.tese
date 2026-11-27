#version 460

layout(triangles, fractional_even_spacing, cw) in;

void main() {
    vec3 bc = gl_TessCoord;
    vec4 interp = bc.x * gl_in[0].gl_Position
                + bc.y * gl_in[1].gl_Position
                + bc.z * gl_in[2].gl_Position;
    gl_Position = vec4(interp.xyz, 1.0);
}
