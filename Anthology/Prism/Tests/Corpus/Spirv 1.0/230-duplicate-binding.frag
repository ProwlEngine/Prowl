// 230-duplicate-binding: two uniform blocks on the same set/binding in one stage (reclassified from
// knownbug/): the ORACLE accepts this too (probe-verified for blocks and samplers) - overlapping
// resource registrations are a driver/pipeline-layout concern, not a compiler error. Prism matches
// glslang by emitting both. Must compile + validate.
#version 450
layout(binding=0) uniform A { float f; };
layout(binding=0) uniform B { float g; };
layout(location=0) out vec4 o;
void main(){ o = vec4(f + g); }