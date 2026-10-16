// glslang rejects this shader (vertexindex in frag); our compiler must reject it too.
#version 460
void main(){ gl_VertexIndex; }
