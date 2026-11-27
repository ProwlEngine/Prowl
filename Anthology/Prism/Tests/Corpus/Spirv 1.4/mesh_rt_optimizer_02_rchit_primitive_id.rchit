//spv 1.4
#version 460
#extension GL_EXT_ray_tracing : require
layout(location=0) rayPayloadInEXT vec4 payload;
hitAttributeEXT vec2 attribs;
float shade(float t) { if (false) { return 0.0; } return t * 2.0; }
void main() {
  float t = gl_HitTEXT;
  for (int i = 0; i < 0; ++i) t += 1.0;
  payload = vec4(shade(t), attribs, float(gl_PrimitiveID));
}
