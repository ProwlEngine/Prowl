#version 450
layout(points, invocations=4) in;
layout(points, max_vertices=4) out;
layout(stream=0, location=0) out vec4 a;
layout(stream=1, location=1) out vec4 b;
layout(location=0) in vec4 vin[];
void main(){ gl_Position = gl_in[0].gl_Position + vin[0]; a = vec4(gl_InvocationID); gl_PrimitiveID = gl_PrimitiveIDIn; gl_Layer = 1; EmitStreamVertex(0); EndStreamPrimitive(0); b = vec4(1); EmitStreamVertex(1); EndStreamPrimitive(1); }
