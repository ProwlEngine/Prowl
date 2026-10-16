// S2.2 (builtin-array variant, found by fuzzing): float literal used as index into gl_in.
// Expected today: compiles + INVALID module (BUG).
#version 460
layout(triangles, equal_spacing, cw) in;
layout(location=0) out vec4 o;
void main(){
  vec4 p = gl_in[1e400].gl_Position;
  gl_Position = p;
}
