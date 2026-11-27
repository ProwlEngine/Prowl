#version 460 core
#extension GL_EXT_debug_printf : require

// debugPrintfEXT emits a non-semantic printf instruction.

layout(location = 0) flat in int v_id;
layout(location = 0) out vec4 fragColor;

void main()
{
    debugPrintfEXT("v=%d", v_id);
    fragColor = vec4(float(v_id));
}
