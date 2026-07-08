# OpenGL Removal Research

Research notes on how OpenGL/OpenGL ES support is wired into Prowl.Graphite, for the purpose of
fully removing it. Paths are relative to `Graphite/` (the repo root containing this file's parent).

## 1. Core implementation files — `Graphite/Platform/OpenGL/` (53 files)

Entire directory to be deleted:

```
Graphite/Platform/OpenGL/BackendInfoOpenGL.cs
Graphite/Platform/OpenGL/OpenGLBuffer.cs
Graphite/Platform/OpenGL/OpenGLCachedPipeline.cs
Graphite/Platform/OpenGL/OpenGLCommandBuffer.cs
Graphite/Platform/OpenGL/OpenGLCommandEntryList.cs
Graphite/Platform/OpenGL/OpenGLCommandExecutor.cs
Graphite/Platform/OpenGL/OpenGLComputeProgram.cs
Graphite/Platform/OpenGL/OpenGLDeferredResource.cs
Graphite/Platform/OpenGL/OpenGLExtensions.cs
Graphite/Platform/OpenGL/OpenGLFence.cs
Graphite/Platform/OpenGL/OpenGLFormats.cs
Graphite/Platform/OpenGL/OpenGLFrame.cs
Graphite/Platform/OpenGL/OpenGLFramebuffer.cs
Graphite/Platform/OpenGL/OpenGLGraphicsDevice.cs
Graphite/Platform/OpenGL/OpenGLGraphicsProgram.cs
Graphite/Platform/OpenGL/OpenGLPlaceholderTexture.cs
Graphite/Platform/OpenGL/OpenGLPlatformInfo.cs
Graphite/Platform/OpenGL/OpenGLResourceFactory.cs
Graphite/Platform/OpenGL/OpenGLSampler.cs
Graphite/Platform/OpenGL/OpenGLSwapchain.cs
Graphite/Platform/OpenGL/OpenGLSwapchainFramebuffer.cs
Graphite/Platform/OpenGL/OpenGLTexture.cs
Graphite/Platform/OpenGL/OpenGLTextureSamplerManager.cs
Graphite/Platform/OpenGL/OpenGLTextureView.cs
Graphite/Platform/OpenGL/OpenGLUtil.cs
Graphite/Platform/OpenGL/StagingMemoryPool.cs
Graphite/Platform/OpenGL/NoAllocEntryList/*.cs   (25 files: NoAllocBeginEntry, NoAllocClearColorTargetEntry,
    NoAllocClearDepthTargetEntry, NoAllocClearPropertiesEntry, NoAllocCopyBufferEntry, NoAllocCopyTextureEntry,
    NoAllocDispatchEntry, NoAllocDispatchIndirectEntry, NoAllocDrawEntry, NoAllocDrawIndexedEntry,
    NoAllocDrawIndexedIndirectEntry, NoAllocDrawIndirectEntry, NoAllocEndEntry, NoAllocGenerateMipmapsEntry,
    NoAllocInsertDebugMarkerEntry, NoAllocPopDebugGroupEntry, NoAllocPushDebugGroupEntry,
    NoAllocResolveTextureEntry, NoAllocSetComputeShaderEntry, NoAllocSetFramebufferEntry,
    NoAllocSetPropertiesEntry, NoAllocSetScissorRectEntry, NoAllocSetShaderEntry, NoAllocSetVertexSourceEntry,
    NoAllocSetViewportEntry, NoAllocUpdateBufferEntry, OpenGLNoAllocCommandEntryList)
```

## 2. Other OpenGL files outside `Platform/OpenGL/`

Delete these too:

- `Graphite/ValidationLayers/Platform/OpenGL/OpenGLGraphicsDevice.Validation.cs` — `partial class OpenGLGraphicsDevice` adding `ExecutorActiveFrame_CheckActive()`.
- `Graphite/Profiling/Platform/OpenGL/OpenGLCommandBuffer.Profiling.cs`
- `Graphite/Profiling/Platform/OpenGL/OpenGLComputeProgram.Profiling.cs`
- `Graphite/Profiling/Platform/OpenGL/OpenGLGraphicsProgram.Profiling.cs`
- `Graphite/Profiling/Platform/OpenGL/OpenGLTexture.Profiling.cs`
- `Compiler/Platform/GLCompiler.cs` — GLSL shader-compiler module (216 lines). Delete.
- `Tests/Compiler/OpenGLCompilationTests.cs` — delete.
- `Tests/Compiler/KnownGood/*.glsl` (14 files) — delete: `Variants.vertex_false.glsl`, `ConstantBuffers.vertex.glsl`,
  `UVOriginUsage.fragment.glsl`, `ParameterBlocks.fragment.glsl`, `ConstantBuffers.fragment.glsl`,
  `Variants.fragment_false.glsl`, `Graphics.fragment.glsl`, `UVOriginUsage.vertex.glsl`, `Variants.vertex_true.glsl`,
  `Graphics.vertex.glsl`, `Modules.fragment.glsl`, `Variants.fragment_true.glsl`, `Modules.vertex.glsl`,
  `ParameterBlocks.vertex.glsl`.

## 3. `GraphicsBackend` enum + dispatch/branch sites

**Enum:** `Graphite/Core/GraphicsDevice/GraphicsBackend.cs`
```csharp
public enum GraphicsBackend : byte
{
    Direct3D11,
    Vulkan,
    OpenGL,     // line 19 — remove
    OpenGLES,   // line 23 — remove
}
```
⚠️ Removing/reordering enum members is a binary-breaking change for consumers who persisted the byte value — check if that matters for this library's versioning policy.

**`Graphite/Core/GraphicsDevice/GraphicsDevice.cs`** (central factory/dispatch):
- Lines 1141–1168: `#if !EXCLUDE_OPENGL_BACKEND` block wrapping `GetOpenGLInfo(...)` — remove entirely (including the `#else` stub if present).
- Lines 1176–1207: `IsBackendSupported(GraphicsBackend)` switch — remove `case GraphicsBackend.OpenGL:` (1192) and `case GraphicsBackend.OpenGLES:` (1198).
- Lines 1324–1342: `#if !EXCLUDE_OPENGL_BACKEND` block wrapping `CreateOpenGL(...)` factory method — remove.

**Other call sites branching on `GraphicsBackend.OpenGL`/`OpenGLES`:**
- `Samples/Shared/DeviceCreateUtilities.cs`
  - Lines 19-20: `Backend` tuple / `API` computed ternary referencing `GraphicsBackend.OpenGL` — simplify (Backend is hardcoded to Vulkan already, so this is dead code today).
  - Lines 86-99: `case GraphicsBackend.OpenGLES: case GraphicsBackend.OpenGL:` in `CreateDevice(...)` — remove.
- `Tests/Graphite/TestUtils.cs`
  - Lines 89-102: duplicate of the above switch (comment explicitly notes duplication) — remove.
  - Lines 68-79: `GetApi(GraphicsBackend)` switch expression arms for OpenGL/OpenGLES — remove.
  - Lines 325-339: `OpenGLDeviceCreator` / `OpenGLESDeviceCreator` classes — remove; also remove from wherever the backend test matrix enumerates creators.
- `Samples/Shared/ShaderLoader.cs:17-18` and `Tests/Graphite/TestShaderLoader.cs:22-23` — dictionary entries mapping `GraphicsBackend.OpenGL`/`OpenGLES` → `new GLCompiler(...)` — remove.
- `Tools/SlangQuickCompile/Program.cs:104` — `GraphicsBackend.OpenGL => "glsl"` switch arm — remove.
- `Compiler/CompilationSession.cs:324-325` — `IsBackendTopLeft` lists `Direct3D11`/`Vulkan` only; no change needed (OpenGL was already implicit "other"), but re-verify after GLCompiler removal.

## 4. `.csproj` / build-flag references

**`Graphite/Prowl.Graphite.csproj`**
- Line 18: `<DefineConstants Condition="'$(ExcludeOpenGL)' == 'true'">$(DefineConstants);EXCLUDE_OPENGL_BACKEND</DefineConstants>` — remove.
- Lines 33-35: `PackageReference` for `Silk.NET.OpenGL`, `Silk.NET.OpenGLES`, `Silk.NET.OpenGL.Extensions.EXT` — remove all three.
- Lines 11-12: `Description`/`PackageTags` mention OpenGL — edit text.

**`Compiler/Prowl.Graphite.Compiler.csproj:12`** — `PackageTags` mentions OpenGL — edit text.

**`ShaderDef/Prowl.Graphite.ShaderDef.csproj:12`** — `PackageTags` mentions OpenGL — edit text.

**`Directory.Build.props`** (lines 16-21) — has `<ExcludeVulkan>`, `<ExcludeD3D11>`, `<ExcludeOpenGL>` build-flag defaults. Remove `<ExcludeOpenGL>` once the code path is gone (keep the other two).

**`Tests/Graphite/Prowl.Graphite.Tests.csproj`** (lines 8-11) — remove `TEST_OPENGL`/`TEST_OPENGLES` `DefineConstants` additions.

⚠️ **Important gotcha found during research:** `EXCLUDE_OPENGL_BACKEND` currently only guards `BackendInfoOpenGL.cs` and the handful of call sites in `GraphicsDevice.cs` — **not** the other 52 files in `Platform/OpenGL/` nor the 5 files in `ValidationLayers/Platform/OpenGL/` + `Profiling/Platform/OpenGL/`. Those always compile today. This means:
- Simply setting `ExcludeOpenGL=true` would **not** cleanly exclude OpenGL — it would break the build (Silk.NET.OpenGL types would go unresolved while 57 files still reference them).
- Full removal must **physically delete** the files and de-reference every call site listed above, not just flip a flag.
- (Aside, not in scope: the same incomplete-guard gap exists for D3D11 and Vulkan's `ExcludeD3D11`/`ExcludeVulkan` flags — worth flagging separately, not part of this OpenGL task.)

## 5. Preprocessor defines to remove

- `EXCLUDE_OPENGL_BACKEND` — defined `Graphite/Prowl.Graphite.csproj:18`; consumed in `GraphicsDevice.cs:1141,1193,1199,1324` and `BackendInfoOpenGL.cs:1,96` (file itself is deleted anyway).
- `TEST_OPENGL` / `TEST_OPENGLES` — defined `Tests/Graphite/Prowl.Graphite.Tests.csproj:8-11`; consumed via `#if TEST_OPENGL`/`#if TEST_OPENGLES` in 15 files under `Tests/Graphite/GPU/` + `GPU/Baseline/`:
  `PropertySetBindingTests.cs`, `ComputeTests.cs`, `FramebufferTests.cs`, `DisposalTests.cs`,
  `MultiParameterBlockBindingTests.cs`, `GraphicsDeviceTests.cs`, `RenderCoreTests.cs`, `TextureTests.cs`,
  `ComputeCoreTests.cs`, `BufferTests.cs`, `SwapchainTests.cs`, `FrameCoreTests.cs`, `RenderTests.cs`,
  `ProfilingCountingTests.cs`. Each `#if TEST_OPENGL`/`#if TEST_OPENGLES` block needs to be stripped (keeping the
  code inside if it's meaningful for other backends, deleting if OpenGL-only).
- Runtime string constants like `GL_ARB_texture_storage`, `GL_INVALID_INDEX` etc. are just extension-name strings inside the deleted files — not build-system relevant, no action needed beyond deleting those files.

## 6. Shader compiler (GLSL)

- `Compiler/Platform/GLCompiler.cs` is the whole GLSL backend: targets `CompileTarget.Glsl` via Slang, rewrites
  Vulkan-flavored GLSL into portable desktop/ES GLSL (`MakePortableGlsl`), does GL-specific resource reflection.
  **Delete the file.**
- Whatever registers `CompilerModule`s for a session (used by `Samples/Shared/ShaderLoader.cs` and
  `Tests/Graphite/TestShaderLoader.cs`, see §3) needs its GLCompiler dictionary entries removed.
- `ShaderDef/` has no OpenGL-specific compiler code — only a prose aside in `ShaderDef/ShaderSpec.md:203`
  referencing `GL_DEPTH_CLAMP` as a cross-API explainer. Optional wording cleanup, not required.

## 7. Samples

- `Samples/Shared/DeviceCreateUtilities.cs` and `Samples/Shared/ShaderLoader.cs` are the only sample-layer files
  touching OpenGL (see §3) — both shared by `Cube`, `CubeGrid`, `HelloTriangle`, `TexturedQuad`. No sample has its
  own OpenGL code.

## 8. Tests

- `Tests/Graphite/TestUtils.cs` — remove `OpenGLDeviceCreator`/`OpenGLESDeviceCreator` and switch arms (§3).
- 15 files under `Tests/Graphite/GPU/` + `GPU/Baseline/` have `#if TEST_OPENGL`/`#if TEST_OPENGLES` blocks to strip (§5). Notable: `GraphicsDeviceTests.cs:115` skips a test when `GD.BackendType is GraphicsBackend.OpenGL or GraphicsBackend.OpenGLES` — remove alongside the enum values.
- `Tests/Compiler/OpenGLCompilationTests.cs` — delete whole file.
- `Tests/Compiler/KnownGood/*.glsl` — delete (§2).
- Cross-backend compiler tests that list OpenGL and need editing (remove OpenGL/GLSL expectation from arrays):
  - `Tests/Compiler/CompileForTargetTests.cs:17` — `[GraphicsBackend.OpenGL, GraphicsBackend.Vulkan, GraphicsBackend.Direct3D11]`
  - `Tests/Compiler/EnumVariantCompilationTests.cs:57`
  - `Tests/Compiler/VariantCompilationTests.cs:64`
- `Tests/Graphite/README.md` — backend-support table (lines 50-55) and shader-compilation note (line 34) mention OpenGL — edit.

## 9. Documentation to update

- `Graphite/README.md` — most doc surface area:
  - Line 3: feature summary ("...Vulkan, Direct3D 11, and OpenGL/OpenGL ES")
  - Line 9: features bullet list
  - Line 12: `PropertySet` binding rule ("OpenGL uniforms")
  - Line 37: `CreateOpenGL` mention in Quick Start
  - Lines 65-70: backend support matrix (`OpenGL` / `OpenGL ES` rows) — remove rows
  - Lines 72-74: `GraphicsBackend` enum / factory-method mention
  - Line 95: `ExcludeOpenGL` row in build-flags table — remove row
  - Lines 97-99: `EXCLUDE_*_BACKEND` symbol description — edit
  - Line 184: OpenGL uniforms vs D3D registers vs Vulkan sets/bindings comparison
  - Lines 192, 196: `PropertySet.SetTexture`/`SetSampler` OpenGL-specific notes
  - Line 279: GPU test shader-compilation note ("GLSL for OpenGL/ES")
- `Tests/Graphite/README.md:34,50-55` — see §8.
- `Anthology/README.md:27` — root repo README, one line in sub-project table mentions "GL" for Graphite — edit to drop GL mention.
- csproj `PackageTags`/`Description` — see §4.

## Suggested removal order

1. Delete `Graphite/Platform/OpenGL/`, `Graphite/ValidationLayers/Platform/OpenGL/`,
   `Graphite/Profiling/Platform/OpenGL/`, `Compiler/Platform/GLCompiler.cs`,
   `Tests/Compiler/OpenGLCompilationTests.cs`, `Tests/Compiler/KnownGood/*.glsl`.
2. Remove `OpenGL`/`OpenGLES` members from `GraphicsBackend` enum (`Graphite/Core/GraphicsDevice/GraphicsBackend.cs`).
3. Fix all resulting compile errors — this will surface every remaining call site (should match §3 list):
   `GraphicsDevice.cs`, `DeviceCreateUtilities.cs`, `ShaderLoader.cs`, `TestUtils.cs`, `TestShaderLoader.cs`,
   `Tools/SlangQuickCompile/Program.cs`, the three cross-backend compiler test files, the 15 `TEST_OPENGL`-gated
   test files.
4. Remove `Silk.NET.OpenGL`/`Silk.NET.OpenGLES`/`Silk.NET.OpenGL.Extensions.EXT` package refs and
   `EXCLUDE_OPENGL_BACKEND`/`TEST_OPENGL`/`TEST_OPENGLES`/`ExcludeOpenGL` build-flag plumbing from all `.csproj`
   files and `Directory.Build.props`.
5. Update docs (`Graphite/README.md`, `Tests/Graphite/README.md`, `Anthology/README.md`, csproj metadata).
6. Full solution build + test run to confirm nothing OpenGL-related remains reachable.
