#version 460 core

// exercises gl_VertexIndex, gl_InstanceIndex, gl_PointSize, gl_Position

void main()
{
    float x = float(gl_VertexIndex) * 0.1;
    float y = float(gl_InstanceIndex) * 0.1;

    gl_PointSize = 1.0;
    gl_Position = vec4(x, y, 0.0, 1.0);
}
