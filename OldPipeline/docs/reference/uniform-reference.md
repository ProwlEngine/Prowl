# Uniform Reference

Every pipeline-level uniform, who writes it, and who reads it. Material-level properties are listed on each shader's
page. UBO fields are in [global uniforms](../architecture/global-uniforms.md).

## Camera / frame (UBO `GlobalUniforms`)

`prowl_MatV`, `prowl_MatIV`, `prowl_MatP`, `prowl_MatVP`, `prowl_PrevViewProj`, `prowl_MatIP`, `prowl_MatIVP`,
`prowl_MatVP_NonJittered`, `_WorldSpaceCameraPos`, `_ProjectionParams`, `_ScreenParams`, `_CameraJitter`,
`_CameraPreviousJitter`, `_Time`, `_SinTime`, `_CosTime`, `prowl_DeltaTime` - written by
`RenderPipeline.SetupGlobalUniforms` / `AssignCameraMatrices` and `TAAEffect`; read everywhere via `ShaderVariables`.

## Per object (plain uniforms)

| Uniform | Writer | Reader |
|---|---|---|
| `prowl_ObjectToWorld`, `prowl_WorldToObject`, `prowl_PrevObjectToWorld` | `DrawRenderables` | `ShaderVariables` macros |
| `_ObjectID` | renderer components | `DrawRenderables` motion history |
| `_GIMode`, `_Lightmap`, `_LightmapScaleOffset`, `_LightmapUV` | `LightmapBinding.Fill` | `StandardCore.CalculateGI` |
| `prowl_SHAr/g/b`, `prowl_SHBr/g/b`, `prowl_SHC` | `LightmapBinding.Fill` | `Lighting.ShadeSH9` |
| `boneMatrixTexture`, `boneCount` | `SkinnedMeshRenderer` | `VertexAttributes` |
| `morphPositionTexture`, `morphNormalTexture`, `morphTangentTexture`, `morphWeightTexture`, `morphActiveCount`, `morphTexWidth`, `morphVertexCount`, `morphHasNormals`, `morphHasTangents` | `SkinnedMeshRenderer` (`MeshRenderer` pins `morphActiveCount = 0`) | `VertexAttributes` |

## Prepass outputs (globals)

| Uniform | Content | Readers |
|---|---|---|
| `_CameraDepthTexture` | prepass depth | GTAO, SSR, TAA, motion blur, DoF, fog, cinematic god rays, grid, gizmo icons |
| `_CameraNormalsTexture` | view normals | GTAO, SSR |
| `_CameraMotionVectorsTexture` | motion.rg, roughness, metallic | SSR, GTAO temporal (effects often rebind as `_MotionVectorsTex`) |

## Lighting (globals, `SceneLightSystem.UploadGlobalUniforms`)

| Uniform | Meaning |
|---|---|
| `_StaticLightData`, `_StaticLightNodes`, `_DynamicLightData`, `_DynamicLightNodes` | BVH textures |
| `_StaticLightTexSize/Shift`, `_StaticNodeTexSize/Shift`, `_DynamicLightTexSize/Shift`, `_DynamicNodeTexSize/Shift` | texture size and log2 |
| `_StaticLightRoot`, `_DynamicLightRoot` | root index or -1 |
| `_DirectionalLightEnabled`, `_DirectionalLightDirection`, `_DirectionalLightColor`, `_DirectionalLightIntensity` | directional |
| `_DirectionalLightShadowEnabled`, `_DirectionalLightShadowBias`, `_DirectionalLightShadowNormalBias`, `_DirectionalLightShadowStrength`, `_DirectionalLightShadowQuality` | directional shadow |
| `_CascadeCount`, `_CascadeShadowMatrix0..3`, `_CascadeAtlasParams0..3` (xy atlas pos, z size, w split distance) | cascades |
| `_ShadowFocusPos` | cascade center |
| `_PointShadowMatrices[24]`, `_PointShadowFaceParams[24]` (xy pos, z size, w far) | point slots x 6 faces |
| `_SpotShadowMatrices[4]`, `_SpotShadowAtlasParams[4]` (xy pos, z size) | spot slots |
| `_ShadowAtlas` (sampler2DShadow), `_ShadowAtlasSize` | atlas |

## Environment (globals, `DefaultRenderPipeline`)

| Uniform | Source |
|---|---|
| `_FogColor`, `_FogParams`, `_FogStates` | `Scene.Fog` |
| `_AmbientMode`, `_AmbientColor`, `_AmbientSkyColor`, `_AmbientGroundColor`, `_AmbientStrength` | `Scene.Ambient` |
| `_BRDFLut` | `BRDFLutGenerator.UploadGlobal` (unused by shaders) |

## Temporary / scoped

| Uniform | Scope |
|---|---|
| `_GrabTexture` (name from the pass) and optional grab depth | one batch of a `GrabTexture` pass |
| `_MainTex` | set by every `cmd.Blit(src, ...)` |

## Effect-private (set on the effect material)

See each effect page. Naming conventions: `_Resolution`, `_Noise`, `_JitterOffset`/`_JitterSizeAndOffset`,
`_PreviousBuffer`/`_HistoryTex`, `_TResponse`, `_MotionVectorsTex`.
