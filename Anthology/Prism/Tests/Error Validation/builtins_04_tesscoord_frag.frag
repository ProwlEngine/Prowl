// glslang rejects this shader (tesscoord frag); our compiler must reject it too.
#version 460
layout(location=0) out vec4 c; void main(){ c=vec4(0); gl_TessCoord; }
