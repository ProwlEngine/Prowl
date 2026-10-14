// glslang rejects this shader (mat add int); our compiler must reject it too.
#version 460
void main(){ mat2 m; float f = m + 1; bool b = m == m ? true : false; }
