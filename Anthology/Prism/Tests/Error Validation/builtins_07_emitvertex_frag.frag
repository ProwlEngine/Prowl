// glslang rejects this shader (emitvertex frag); our compiler must reject it too.
#version 460
layout(location=0) out vec4 c; void main(){ c = vec4(dFdx(1.0)); EmitVertex(); }
