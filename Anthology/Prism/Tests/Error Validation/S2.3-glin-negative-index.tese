// S2.3 builtin interface arrays skip the bounds check user arrays have:
// gl_in[-2] -> OpAccessChain with negative constant index, spirv-val rejects
// ("Index at word 4 may not have a negative value"). User arrays reject cleanly.
// Expected today: compiles + INVALID module (BUG).
#version 460
layout(quads, fractional_even_spacing, ccw) in;
void main() {
  vec4 a = mix(gl_in[0].gl_Position, gl_in[-2].gl_Position, gl_TessCoord.x);
  vec4 b = mix(gl_in[-3].gl_Position, gl_in[1].gl_Position, gl_TessCoord.x);
  gl_Position = mix(a, b, gl_TessCoord.y);
}
