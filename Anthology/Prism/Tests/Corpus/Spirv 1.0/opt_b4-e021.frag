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

void main(){ int16_t a = int16_t(-32768); o=vec4(float(a/int16_t(-1)), float(a%int16_t(-1)), float(-a), float(abs(a))); }
