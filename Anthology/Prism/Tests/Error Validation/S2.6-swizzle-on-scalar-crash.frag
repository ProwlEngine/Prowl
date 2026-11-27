// S2.6 swizzling a scalar with a 2-component swizzle (.y on float) — glslang rejects
// ("member of scalar"), Prism crashes: raw IndexOutOfRangeException from Lowerer.LowerSwizzle
// (~line 7337). Found by fuzzing. Contract: should be a clean GlslParseException.
// Expected today: raw IndexOutOfRangeException (BUG).
#version 450
layout(location=0) out vec4 o;
void main(){ o = vec4(vec2(1).x.y); }
