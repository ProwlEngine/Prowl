// glslang rejects this shader (texture nonsampler); our compiler must reject it too.
#version 460
layout(location=0)out vec4 o;void main(){ o = texture(vec4(0), vec2(0)); }
