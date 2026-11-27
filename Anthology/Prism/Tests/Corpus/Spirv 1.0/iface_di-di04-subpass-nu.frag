#version 450
#extension GL_EXT_nonuniform_qualifier : require
layout(location=0) flat in int i;
layout(location=0) out vec4 o;
layout(input_attachment_index=0, binding=8) uniform subpassInput sp[];
void main(){ o = subpassLoad(sp[nonuniformEXT(i)]); }
