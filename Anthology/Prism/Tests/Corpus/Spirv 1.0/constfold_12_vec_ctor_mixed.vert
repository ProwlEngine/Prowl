#version 460

void main() {
    vec3 v = vec3(1, 2.0, 3);   // mixed int/float scalars
    gl_Position = vec4(v / 3.0, 1.0);
}
