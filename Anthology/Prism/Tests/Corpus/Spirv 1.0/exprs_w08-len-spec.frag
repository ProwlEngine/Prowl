#version 450
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
layout(location=1) flat in int i;
layout(location=2) flat in uint u;
layout(constant_id=0) const int N = 4; float a[N]; void main(){ a[0] = v.x; o = vec4(a.length()); }
