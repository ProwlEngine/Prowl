#version 460
#extension GL_EXT_shader_explicit_arithmetic_types : require
#extension GL_EXT_demote_to_helper_invocation : require
#extension GL_EXT_terminate_invocation : require
#extension GL_EXT_control_flow_attributes : require
layout(location=0) out vec4 o;
layout(location=0) flat in int ii;
layout(location=1) in float ff;
layout(location=2) in vec4 vv;
layout(set=0,binding=1) buffer B { float data[64]; int64_t l[]; } b;
layout(constant_id=0) const int SC = 3;
layout(constant_id=1) const bool SB = true;
layout(constant_id=2) const float SF = 1.5;

void main(){ int64_t a = -9l; int64_t c = 4l; o=vec4(float(a/c), float(a%c), float(-a/-c), float(a*c)); }
