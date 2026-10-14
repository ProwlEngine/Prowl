#version 450
layout(location=0) in flat vec4 a;
layout(location=1) in noperspective vec4 b;
layout(location=2) in smooth vec4 c;
layout(location=3) in centroid vec4 d;
layout(location=4) in sample vec4 e;
layout(location=5) in flat centroid vec4 f;
layout(location=6) in noperspective sample vec4 g;
layout(location=0) out vec4 o;
void main() { o = a + b + c + d + e + f + g; }
