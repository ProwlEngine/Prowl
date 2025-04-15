// Attributes
attribute vec2 vertex;
attribute vec2 tcoord;

// Uniforms
uniform mat4 transformMat;

// Varyings
varying vec2 fpos;
varying vec2 ftcoord;

void main()
{
	ftcoord = tcoord;
	fpos = vertex;
	gl_Position = transformMat * vec4(vertex, 0, 1);
}