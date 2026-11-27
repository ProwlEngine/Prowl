#version 450
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
layout(location=1) flat in int i;
layout(binding=0, rgba8) uniform image2DMS im; void main(){ o = vec4(imageSamples(im)); }
