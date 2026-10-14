# Graphite real-world shaders

The exact GLSL Graphite hands to shaderc for its own shader catalog: every pass of every `.shader` file in
`Graphite/Rendering/Library` plus every pass the renderer registers in code (`ShaderSources`: present/clear,
post effects, prefilter, compute), after Graphite's preprocess + program-layout rewrite (`GlslStageSource.Rewrite`).
Compiled for SPIR-V 1.5 / Vulkan 1.2, the settings Graphite uses.

Variant selection (the full keyword space is 70,525 variants, 67,584 of them Lit Forward):

- passes with at most 64 variants: every variant;
- larger passes (Lit/Unlit Forward, Prepass, ShadowCaster): the base variant, each keyword value on its own, and an
  everything-on variant (debug view off);
- byte-identical stage sources are kept once, and `GRAPHITE_NO_SOURCE` combinations that glslang itself rejects
  are dropped.

`variants.tsv` maps each file to its shader, pass and keyword combination.
