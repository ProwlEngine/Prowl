#version 450
in Blk {
  flat vec4 a;
  noperspective vec4 b;
  centroid vec4 c;
  sample vec4 d;
  vec4 e;
} blk;
layout(location=0) out vec4 o;
void main() { o = blk.a + blk.b + blk.c + blk.d + blk.e; }
