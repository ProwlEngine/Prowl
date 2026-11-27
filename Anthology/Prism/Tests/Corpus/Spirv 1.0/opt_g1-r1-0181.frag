#version 450
layout(location=0) out vec4 o;
layout(location=0) flat in int ii;
layout(location=1) flat in uint uu;
layout(location=2) in float ff;
layout(location=3) in vec4 vv;
layout(location=4) flat in ivec4 iv;
void main(){
  float fv = smoothstep(min(vv.x,2.0),1e30,(0.0/0.0));
  int iv2 = 33;
  o = vec4(((min(float((~2)),sign(dot(vec2((1.0/0.0),0.0),vec2(0.0,0.0))))+(-((1!=0x7fffffff) ? (((-1.0/0.0)+1e30)*vv.x) : float(iv2))))*intBitsToFloat((int(31u)/iv.y))), float(ii), float(((((2u|0u)-(1u&31u))%uint(findLSB(32)))|3u)), false ? 1.0 : 0.0);
}
