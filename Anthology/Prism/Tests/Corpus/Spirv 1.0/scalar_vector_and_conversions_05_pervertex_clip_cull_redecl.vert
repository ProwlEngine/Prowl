#version 460
out gl_PerVertex {
    vec4 gl_Position;
    float gl_ClipDistance[2];
    float gl_CullDistance[1];
};
void main() {
    gl_Position = vec4(1.0);
    gl_ClipDistance[0] = 0.5;
    gl_ClipDistance[1] = -0.5;
    gl_CullDistance[0] = 1.0;
}
