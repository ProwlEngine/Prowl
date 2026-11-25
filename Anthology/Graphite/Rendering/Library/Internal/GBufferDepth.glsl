// The scene's depth as the prepass left it, into a deferred view's GBuffer before its pass draws, so that pass tests
// against what is already known to be in front. See Graphite/GBuffer.glsl.
uniform GRAPHITE_SCREEN_TEXTURE _SceneDepth;

void main()
{
    outColor = vec4(0.0);
    gl_FragDepth = Graphite_Fetch(_SceneDepth, ivec2(gl_FragCoord.xy), 0).r;
}
