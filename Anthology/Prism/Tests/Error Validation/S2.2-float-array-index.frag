// S2.2 non-integer array index accepted; emitted OpAccessChain gets a float index ->
// spirv-val: "Indexes passed to OpAccessChain must be of type integer".
// glslang: array index must be an integer expression.
// Expected today: compiles + INVALID module (BUG).
#version 450
layout(location=0) out vec4 o;
void main(){ float a[4]; o = vec4(a[1.5]); }
