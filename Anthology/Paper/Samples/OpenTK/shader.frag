#ifdef GL_ES
	#define LOWP lowp
	precision mediump float;
#else
	#define LOWP
#endif

// Uniforms
uniform sampler2D tex;
uniform mat4 scissorMat;
uniform mat4 paintMat;
uniform vec4 innerCol;
uniform vec4 outerCol;
uniform vec2 scissorExt;
uniform vec2 scissorScale;
uniform vec2 extent;
uniform float radius;
uniform float feather;
uniform float strokeMult;
uniform float strokeThr;
uniform int type;

// Varyings
varying vec2 fpos;
varying vec2 ftcoord;

float sdroundrect(vec2 pt, vec2 ext, float rad) {
	vec2 ext2 = ext - vec2(rad,rad);
	vec2 d = abs(pt) - ext2;
	return min(max(d.x,d.y),0.0) + length(max(d,0.0)) - rad;
}

// Scissoring
float scissorMask(vec2 p) {
	vec2 sc = (abs((scissorMat * vec4(p,0,1)).xy) - scissorExt);
	sc = vec2(0.5,0.5) - sc * scissorScale;
	return clamp(sc.x,0.0,1.0) * clamp(sc.y,0.0,1.0);
}
#ifdef EDGE_AA
// Stroke - from [0..1] to clipped pyramid, where the slope is 1px.
float strokeMask() {
	return min(1.0, (1.0-abs(ftcoord.x*2.0-1.0))*strokeMult) * min(1.0, ftcoord.y);
}
#endif

void main(void) {
	vec4 result;
	float scissor = scissorMask(fpos);
#ifdef EDGE_AA
	float strokeAlpha = strokeMask();
	if (strokeAlpha < strokeThr) discard;
#else
	float strokeAlpha = 1.0;
#endif
	if (type == 0) {
		// Gradient
		// Calculate gradient color using box gradient
		vec2 pt = (paintMat * vec4(fpos,0,1)).xy;
		float d = clamp((sdroundrect(pt, extent, radius) + feather*0.5) / feather, 0.0, 1.0);
		vec4 color = mix(innerCol,outerCol,d);

		// Combine alpha
		color *= strokeAlpha * scissor;
		result = color;
	} else if (type == 1) {
		// Image
		// Calculate color fron texture
		vec2 pt = (paintMat * vec4(fpos,0,1)).xy / extent;
		vec4 color = texture2D(tex, pt);
		color = vec4(color.xyz*color.w,color.w);
		
		// Apply color tint and alpha.
		color *= innerCol;
		// Combine alpha
		color *= strokeAlpha * scissor;
		result = color;
	} else if (type == 2) {
		// Stencil fill
		result = vec4(1,1,1,1);
	} else if (type == 3) {
		// Textured tris
		vec4 color = texture2D(tex, ftcoord);
		color *= scissor;
		result = color * innerCol;
	}

	gl_FragColor = result;
}