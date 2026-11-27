#version 460
#extension GL_ARB_fragment_shader_interlock : require
layout(sample_interlock_ordered) in;
layout(set = 0, binding = 0, std430) coherent buffer Counter { uint value; } counter;
layout(location = 0) out vec4 oColor;
void main()
{
    beginInvocationInterlockARB();
    counter.value = counter.value + 1u;
    endInvocationInterlockARB();
    oColor = vec4(float(counter.value), 0.0, 0.0, 1.0);
}
