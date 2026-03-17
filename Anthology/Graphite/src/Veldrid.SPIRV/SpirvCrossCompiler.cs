using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.SPIRV;
using Silk.NET.SPIRV.Cross;
using SpvcBackend = Silk.NET.SPIRV.Cross.Backend;
using SpvcCompiler = Silk.NET.SPIRV.Cross.Compiler;
using SpvcContext = Silk.NET.SPIRV.Cross.Context;
using SpvcResources = Silk.NET.SPIRV.Cross.Resources;
using SpvcResult = Silk.NET.SPIRV.Cross.Result;

namespace Veldrid.SPIRV
{
    /// <summary>
    /// Internal engine that drives SPIR-V cross-compilation via the Silk.NET SPIRV-Cross C API.
    /// Handles resource collection, binding remapping, combined image-sampler synthesis,
    /// stage I/O renaming, vertex input reflection, and resource layout building.
    /// </summary>
    internal static unsafe class SpirvCrossCompiler
    {
        private static readonly Cross s_cross = Cross.GetApi();

        internal static VertexFragmentCompilationResult CompileVertexFragment(
            byte[] vsSpirv, byte[] fsSpirv, CrossCompileTarget target, CrossCompileOptions options)
        {
            var cross = s_cross;
            SpvcContext* ctx = null;
            try
            {
                Check(cross, null, cross.ContextCreate(&ctx));

                ParsedIr* vsIr = null;
                ParsedIr* fsIr = null;
                fixed (byte* vsPtr = vsSpirv)
                fixed (byte* fsPtr = fsSpirv)
                {
                    Check(cross, ctx, cross.ContextParseSpirv(ctx, (uint*)vsPtr, (nuint)(vsSpirv.Length / 4), &vsIr));
                    Check(cross, ctx, cross.ContextParseSpirv(ctx, (uint*)fsPtr, (nuint)(fsSpirv.Length / 4), &fsIr));
                }

                SpvcBackend backend = GetSpvcBackend(target);
                SpvcCompiler* vsCompiler = null;
                SpvcCompiler* fsCompiler = null;
                Check(cross, ctx, cross.ContextCreateCompiler(ctx, backend, vsIr, CaptureMode.TakeOwnership, &vsCompiler));
                Check(cross, ctx, cross.ContextCreateCompiler(ctx, backend, fsIr, CaptureMode.TakeOwnership, &fsCompiler));

                // Collect resources from both shaders
                var allResources = new SortedDictionary<BindingKey, ResourceInfo>();
                bool hasVsStorage = CollectResources(cross, vsCompiler, allResources, 0, options.NormalizeResourceNames);
                bool hasFsStorage = CollectResources(cross, fsCompiler, allResources, 1, options.NormalizeResourceNames);

                // Set compiler options (GLSL version depends on whether storage resources are present)
                bool hasStorageResources = hasVsStorage || hasFsStorage;
                SetCompilerOptions(cross, vsCompiler, target, options, isCompute: false, hasStorageResources);
                SetCompilerOptions(cross, fsCompiler, target, options, isCompute: false, hasStorageResources);

                SetSpecializations(cross, vsCompiler, options);
                SetSpecializations(cross, fsCompiler, options);

                if (target == CrossCompileTarget.HLSL || target == CrossCompileTarget.MSL)
                {
                    RemapBindingsHlslMsl(cross, allResources, vsCompiler, fsCompiler, target);
                }

                if (target == CrossCompileTarget.GLSL || target == CrossCompileTarget.ESSL)
                {
                    BuildCombinedImageSamplers(cross, vsCompiler);
                    BuildCombinedImageSamplers(cross, fsCompiler);
                    RenameStageIO(cross, vsCompiler, fsCompiler);
                }

                if (target == CrossCompileTarget.ESSL)
                {
                    RemapBindingsEssl(cross, vsCompiler, fsCompiler, allResources);
                }

                byte* vsSource = null;
                byte* fsSource = null;
                Check(cross, ctx, cross.CompilerCompile(vsCompiler, &vsSource));
                Check(cross, ctx, cross.CompilerCompile(fsCompiler, &fsSource));
                string vsText = Marshal.PtrToStringUTF8((nint)vsSource);
                string fsText = Marshal.PtrToStringUTF8((nint)fsSource);

                VertexElementDescription[] vertexElements = ReflectVertexInputs(cross, vsCompiler);
                ResourceLayoutDescription[] layouts = BuildResourceLayouts(allResources, isCompute: false);

                var reflection = new SpirvReflection(vertexElements, layouts);
                return new VertexFragmentCompilationResult(vsText, fsText, reflection);
            }
            catch (Exception ex) when (ex is not SpirvCompilationException)
            {
                throw new SpirvCompilationException("Cross-compilation failed: " + ex.Message, ex);
            }
            finally
            {
                if (ctx != null) cross.ContextDestroy(ctx);
            }
        }

        internal static ComputeCompilationResult CompileCompute(
            byte[] csSpirv, CrossCompileTarget target, CrossCompileOptions options)
        {
            var cross = s_cross;
            SpvcContext* ctx = null;
            try
            {
                Check(cross, null, cross.ContextCreate(&ctx));

                ParsedIr* csIr = null;
                fixed (byte* csPtr = csSpirv)
                {
                    Check(cross, ctx, cross.ContextParseSpirv(ctx, (uint*)csPtr, (nuint)(csSpirv.Length / 4), &csIr));
                }

                SpvcBackend backend = GetSpvcBackend(target);
                SpvcCompiler* csCompiler = null;
                Check(cross, ctx, cross.ContextCreateCompiler(ctx, backend, csIr, CaptureMode.TakeOwnership, &csCompiler));

                var allResources = new SortedDictionary<BindingKey, ResourceInfo>();
                bool hasStorage = CollectResources(cross, csCompiler, allResources, 0, options.NormalizeResourceNames);

                SetCompilerOptions(cross, csCompiler, target, options, isCompute: true, hasStorage);
                SetSpecializations(cross, csCompiler, options);

                if (target == CrossCompileTarget.HLSL || target == CrossCompileTarget.MSL)
                {
                    RemapBindingsHlslMsl(cross, allResources, csCompiler, null, target);
                }

                if (target == CrossCompileTarget.GLSL || target == CrossCompileTarget.ESSL)
                {
                    BuildCombinedImageSamplers(cross, csCompiler);
                }

                if (target == CrossCompileTarget.ESSL)
                {
                    RemapBindingsEsslSingleStage(cross, csCompiler, allResources);
                }

                byte* csSource = null;
                Check(cross, ctx, cross.CompilerCompile(csCompiler, &csSource));
                string csText = Marshal.PtrToStringUTF8((nint)csSource);

                ResourceLayoutDescription[] layouts = BuildResourceLayouts(allResources, isCompute: true);
                var reflection = new SpirvReflection(Array.Empty<VertexElementDescription>(), layouts);
                return new ComputeCompilationResult(csText, reflection);
            }
            catch (Exception ex) when (ex is not SpirvCompilationException)
            {
                throw new SpirvCompilationException("Cross-compilation failed: " + ex.Message, ex);
            }
            finally
            {
                if (ctx != null) cross.ContextDestroy(ctx);
            }
        }

        #region Types

        private readonly struct BindingKey(uint set, uint binding) : IComparable<BindingKey>
        {
            public readonly uint Set = set;
            public readonly uint Binding = binding;

            public int CompareTo(BindingKey other)
            {
                int c = Set.CompareTo(other.Set);
                return c != 0 ? c : Binding.CompareTo(other.Binding);
            }
        }

        private class ResourceInfo
        {
            public string Name;
            public ResourceKind Kind;
            public uint[] IDs = new uint[2]; // 0 = VS/CS, 1 = FS
        }

        #endregion

        #region Compiler Setup

        private static void Check(Cross cross, SpvcContext* ctx, SpvcResult result)
        {
            if (result != SpvcResult.Success)
            {
                string msg = "SPIRV-Cross error";
                if (ctx != null)
                {
                    byte* errorPtr = (byte*)cross.ContextGetLastErrorString(ctx);
                    if (errorPtr != null)
                    {
                        msg = Marshal.PtrToStringUTF8((nint)errorPtr) ?? msg;
                    }
                }
                throw new SpirvCompilationException(msg);
            }
        }

        private static SpvcBackend GetSpvcBackend(CrossCompileTarget target)
        {
            return target switch
            {
                CrossCompileTarget.HLSL => SpvcBackend.Hlsl,
                CrossCompileTarget.GLSL => SpvcBackend.Glsl,
                CrossCompileTarget.ESSL => SpvcBackend.Glsl,
                CrossCompileTarget.MSL => SpvcBackend.Msl,
                _ => throw new SpirvCompilationException($"Invalid CrossCompileTarget: {target}")
            };
        }

        private static void SetCompilerOptions(
            Cross cross, SpvcCompiler* compiler, CrossCompileTarget target, CrossCompileOptions options,
            bool isCompute, bool hasStorageResources)
        {
            CompilerOptions* opts = null;
            Check(cross, null, cross.CompilerCreateCompilerOptions(compiler, &opts));

            if (options.FixClipSpaceZ)
                cross.CompilerOptionsSetBool(opts, Silk.NET.SPIRV.Cross.CompilerOption.FixupDepthConvention, 1);
            if (options.InvertVertexOutputY)
                cross.CompilerOptionsSetBool(opts, Silk.NET.SPIRV.Cross.CompilerOption.FlipVertexY, 1);

            switch (target)
            {
                case CrossCompileTarget.HLSL:
                    cross.CompilerOptionsSetUint(opts, Silk.NET.SPIRV.Cross.CompilerOption.HlslShaderModel, 50);
                    cross.CompilerOptionsSetBool(opts, Silk.NET.SPIRV.Cross.CompilerOption.HlslPointSizeCompat, 1);
                    break;

                case CrossCompileTarget.GLSL:
                {
                    uint version = (isCompute || hasStorageResources) ? 430u : 330u;
                    cross.CompilerOptionsSetUint(opts, Silk.NET.SPIRV.Cross.CompilerOption.GlslVersion, version);
                    cross.CompilerOptionsSetBool(opts, Silk.NET.SPIRV.Cross.CompilerOption.GlslES, 0);
                    cross.CompilerOptionsSetBool(opts, Silk.NET.SPIRV.Cross.CompilerOption.GlslEnable420PackExtension, 0);
                    break;
                }

                case CrossCompileTarget.ESSL:
                {
                    uint version = (isCompute || hasStorageResources) ? 310u : 300u;
                    cross.CompilerOptionsSetUint(opts, Silk.NET.SPIRV.Cross.CompilerOption.GlslVersion, version);
                    cross.CompilerOptionsSetBool(opts, Silk.NET.SPIRV.Cross.CompilerOption.GlslES, 1);
                    cross.CompilerOptionsSetBool(opts, Silk.NET.SPIRV.Cross.CompilerOption.GlslEnable420PackExtension, 0);
                    break;
                }

                case CrossCompileTarget.MSL:
                    break;
            }

            Check(cross, null, cross.CompilerInstallCompilerOptions(compiler, opts));
        }

        private static void SetSpecializations(Cross cross, SpvcCompiler* compiler, CrossCompileOptions options)
        {
            if (options.Specializations.Length == 0) return;

            Silk.NET.SPIRV.Cross.SpecializationConstant* constants = null;
            nuint count = 0;
            cross.CompilerGetSpecializationConstants(compiler, &constants, &count);

            for (int i = 0; i < options.Specializations.Length; i++)
            {
                uint constID = options.Specializations[i].ID;

                uint varID = 0;
                for (nuint j = 0; j < count; j++)
                {
                    if (constants[j].ConstantId == constID)
                    {
                        varID = constants[j].Id;
                        break;
                    }
                }

                if (varID != 0)
                {
                    Constant* constant = cross.CompilerGetConstantHandle(compiler, varID);
                    // Write the raw u64 value, matching upstream's direct `constVar.m.c[0].r[0].u64 = value`
                    cross.ConstantSetScalarU64(constant, 0, 0, options.Specializations[i].Data);
                }
            }
        }

        #endregion

        #region Resource Collection

        /// <summary>
        /// Collects all resources from a shader into the shared resource map.
        /// Returns true if the shader uses storage buffers or storage images.
        /// </summary>
        private static bool CollectResources(
            Cross cross, SpvcCompiler* compiler,
            SortedDictionary<BindingKey, ResourceInfo> allResources,
            uint idIndex, bool normalizeResourceNames)
        {
            SpvcResources* resources = null;
            Check(cross, null, cross.CompilerCreateShaderResources(compiler, &resources));

            bool hasStorage = false;

            AddResourcesOfType(cross, compiler, resources, ResourceType.UniformBuffer,
                allResources, idIndex, normalizeResourceNames, ResourceKind.UniformBuffer);

            hasStorage |= AddStorageBuffers(cross, compiler, resources,
                allResources, idIndex, normalizeResourceNames);

            AddResourcesOfType(cross, compiler, resources, ResourceType.SeparateImage,
                allResources, idIndex, normalizeResourceNames, ResourceKind.TextureReadOnly);

            hasStorage |= AddResourcesOfType(cross, compiler, resources, ResourceType.StorageImage,
                allResources, idIndex, normalizeResourceNames, ResourceKind.TextureReadWrite);

            AddResourcesOfType(cross, compiler, resources, ResourceType.SeparateSamplers,
                allResources, idIndex, normalizeResourceNames, ResourceKind.Sampler);

            return hasStorage;
        }

        private static bool AddResourcesOfType(
            Cross cross, SpvcCompiler* compiler, SpvcResources* resources,
            ResourceType resourceType,
            SortedDictionary<BindingKey, ResourceInfo> allResources,
            uint idIndex, bool normalizeResourceNames, ResourceKind kind)
        {
            ReflectedResource* resourceList = null;
            nuint resourceCount = 0;
            cross.ResourcesGetResourceListForType(resources, resourceType, &resourceList, &resourceCount);

            bool any = false;
            for (nuint i = 0; i < resourceCount; i++)
            {
                any = true;
                ref ReflectedResource resource = ref resourceList[i];
                uint set = cross.CompilerGetDecoration(compiler, resource.Id, Decoration.DescriptorSet);
                uint binding = cross.CompilerGetDecoration(compiler, resource.Id, Decoration.Binding);

                string name = GetOrSetResourceName(cross, compiler, ref resource, kind,
                    set, binding, normalizeResourceNames);

                InsertResource(allResources, set, binding, resource.Id, idIndex, name, kind);
            }

            return any;
        }

        private static bool AddStorageBuffers(
            Cross cross, SpvcCompiler* compiler, SpvcResources* resources,
            SortedDictionary<BindingKey, ResourceInfo> allResources,
            uint idIndex, bool normalizeResourceNames)
        {
            ReflectedResource* resourceList = null;
            nuint resourceCount = 0;
            cross.ResourcesGetResourceListForType(resources, ResourceType.StorageBuffer, &resourceList, &resourceCount);

            bool any = false;
            for (nuint i = 0; i < resourceCount; i++)
            {
                any = true;
                ref ReflectedResource resource = ref resourceList[i];

                // Uses get_buffer_block_decorations (matching upstream's get_buffer_block_flags) which checks
                // both variable-level and member-level decorations, not just the variable itself.
                bool isNonWritable = HasBufferBlockDecoration(cross, compiler, resource.Id, Decoration.NonWritable);
                ResourceKind kind = isNonWritable ? ResourceKind.StructuredBufferReadOnly : ResourceKind.StructuredBufferReadWrite;

                uint set = cross.CompilerGetDecoration(compiler, resource.Id, Decoration.DescriptorSet);
                uint binding = cross.CompilerGetDecoration(compiler, resource.Id, Decoration.Binding);

                string name;
                if (normalizeResourceNames)
                {
                    name = $"vdspv_{set}_{binding}";
                    SetNativeName(cross, compiler, resource.Id, name);
                }
                else
                {
                    name = GetNativeName(cross, compiler, resource.Id, resource.BaseTypeId);
                }

                InsertResource(allResources, set, binding, resource.Id, idIndex, name, kind);
            }

            return any;
        }

        private static string GetOrSetResourceName(
            Cross cross, SpvcCompiler* compiler, ref ReflectedResource resource,
            ResourceKind kind, uint set, uint binding, bool normalizeResourceNames)
        {
            if (normalizeResourceNames)
            {
                string name = $"vdspv_{set}_{binding}";
                uint nameTarget = kind == ResourceKind.UniformBuffer ? resource.BaseTypeId : resource.Id;
                SetNativeName(cross, compiler, nameTarget, name);
                return name;
            }
            else
            {
                return GetNativeName(cross, compiler, resource.Id, resource.BaseTypeId);
            }
        }

        private static void InsertResource(
            SortedDictionary<BindingKey, ResourceInfo> allResources,
            uint set, uint binding, uint resourceId, uint idIndex, string name, ResourceKind kind)
        {
            var key = new BindingKey(set, binding);
            if (allResources.TryGetValue(key, out var existing))
            {
                if (existing.IDs[idIndex] != 0)
                {
                    throw new SpirvCompilationException(
                        $"The same binding slot ({set}, {binding}) was used by multiple distinct resources. " +
                        $"First resource: {existing.Name}. Second resource: {name}");
                }
                if (existing.Kind != kind)
                {
                    throw new SpirvCompilationException(
                        $"The same binding slot ({set}, {binding}) was used by multiple resources with " +
                        $"incompatible types: \"{existing.Kind}\" and \"{kind}\".");
                }
                existing.IDs[idIndex] = resourceId;
            }
            else
            {
                var info = new ResourceInfo { Name = name, Kind = kind };
                info.IDs[idIndex] = resourceId;
                allResources[key] = info;
            }
        }

        #endregion

        #region Binding Remapping

        private static uint GetResourceIndex(
            CrossCompileTarget target, ResourceKind kind,
            ref uint bufferIndex, ref uint textureIndex, ref uint uavIndex, ref uint samplerIndex)
        {
            switch (kind)
            {
                case ResourceKind.UniformBuffer:
                    return bufferIndex++;
                case ResourceKind.StructuredBufferReadWrite:
                    return target == CrossCompileTarget.MSL ? bufferIndex++ : uavIndex++;
                case ResourceKind.TextureReadWrite:
                    return target == CrossCompileTarget.MSL ? textureIndex++ : uavIndex++;
                case ResourceKind.TextureReadOnly:
                    return textureIndex++;
                case ResourceKind.StructuredBufferReadOnly:
                    return target == CrossCompileTarget.MSL ? bufferIndex++ : textureIndex++;
                case ResourceKind.Sampler:
                    return samplerIndex++;
                default:
                    throw new SpirvCompilationException($"Invalid ResourceKind: {kind}");
            }
        }

        private static void RemapBindingsHlslMsl(
            Cross cross,
            SortedDictionary<BindingKey, ResourceInfo> allResources,
            SpvcCompiler* compiler0, SpvcCompiler* compiler1,
            CrossCompileTarget target)
        {
            uint bufferIndex = 0, textureIndex = 0, uavIndex = 0, samplerIndex = 0;

            foreach (var kvp in allResources)
            {
                uint index = GetResourceIndex(target, kvp.Value.Kind,
                    ref bufferIndex, ref textureIndex, ref uavIndex, ref samplerIndex);

                uint id0 = kvp.Value.IDs[0];
                if (id0 != 0)
                {
                    cross.CompilerSetDecoration(compiler0, id0, Decoration.Binding, index);
                }

                if (compiler1 != null)
                {
                    uint id1 = kvp.Value.IDs[1];
                    if (id1 != 0)
                    {
                        cross.CompilerSetDecoration(compiler1, id1, Decoration.Binding, index);
                    }
                }
            }
        }

        private static void RemapBindingsEssl(
            Cross cross, SpvcCompiler* vsCompiler, SpvcCompiler* fsCompiler,
            SortedDictionary<BindingKey, ResourceInfo> allResources)
        {
            // Unset binding on VS uniform buffers
            SpvcResources* vsResources = null;
            Check(cross, null, cross.CompilerCreateShaderResources(vsCompiler, &vsResources));
            ReflectedResource* vsUBOs = null;
            nuint vsUBOCount = 0;
            cross.ResourcesGetResourceListForType(vsResources, ResourceType.UniformBuffer, &vsUBOs, &vsUBOCount);
            for (nuint i = 0; i < vsUBOCount; i++)
            {
                cross.CompilerUnsetDecoration(vsCompiler, vsUBOs[i].Id, Decoration.Binding);
            }

            // Reassign storage buffer and storage image bindings
            uint bufferIndex = 0, imageIndex = 0;
            foreach (var kvp in allResources)
            {
                if (kvp.Value.Kind == ResourceKind.StructuredBufferReadOnly || kvp.Value.Kind == ResourceKind.StructuredBufferReadWrite)
                {
                    uint id = bufferIndex++;
                    if (kvp.Value.IDs[0] != 0)
                        cross.CompilerSetDecoration(vsCompiler, kvp.Value.IDs[0], Decoration.Binding, id);
                    if (kvp.Value.IDs[1] != 0)
                        cross.CompilerSetDecoration(fsCompiler, kvp.Value.IDs[1], Decoration.Binding, id);
                }
                else if (kvp.Value.Kind == ResourceKind.TextureReadWrite)
                {
                    uint id = imageIndex++;
                    if (kvp.Value.IDs[0] != 0)
                        cross.CompilerSetDecoration(vsCompiler, kvp.Value.IDs[0], Decoration.Binding, id);
                    if (kvp.Value.IDs[1] != 0)
                        cross.CompilerSetDecoration(fsCompiler, kvp.Value.IDs[1], Decoration.Binding, id);
                }
            }
        }

        private static void RemapBindingsEsslSingleStage(
            Cross cross, SpvcCompiler* compiler,
            SortedDictionary<BindingKey, ResourceInfo> allResources)
        {
            SpvcResources* resources = null;
            Check(cross, null, cross.CompilerCreateShaderResources(compiler, &resources));
            ReflectedResource* ubos = null;
            nuint uboCount = 0;
            cross.ResourcesGetResourceListForType(resources, ResourceType.UniformBuffer, &ubos, &uboCount);
            for (nuint i = 0; i < uboCount; i++)
            {
                cross.CompilerUnsetDecoration(compiler, ubos[i].Id, Decoration.Binding);
            }

            uint bufferIndex = 0, imageIndex = 0;
            foreach (var kvp in allResources)
            {
                if (kvp.Value.Kind == ResourceKind.StructuredBufferReadOnly || kvp.Value.Kind == ResourceKind.StructuredBufferReadWrite)
                {
                    cross.CompilerSetDecoration(compiler, kvp.Value.IDs[0], Decoration.Binding, bufferIndex++);
                }
                else if (kvp.Value.Kind == ResourceKind.TextureReadWrite)
                {
                    cross.CompilerSetDecoration(compiler, kvp.Value.IDs[0], Decoration.Binding, imageIndex++);
                }
            }
        }

        #endregion

        #region GLSL/ESSL Specific

        private static void BuildCombinedImageSamplers(Cross cross, SpvcCompiler* compiler)
        {
            uint dummySamplerId = 0;
            Check(cross, null, cross.CompilerBuildDummySamplerForCombinedImages(compiler, &dummySamplerId));
            Check(cross, null, cross.CompilerBuildCombinedImageSamplers(compiler));

            CombinedImageSampler* combinedSamplers = null;
            nuint count = 0;
            cross.CompilerGetCombinedImageSamplers(compiler, &combinedSamplers, &count);

            for (nuint i = 0; i < count; i++)
            {
                byte* imageName = (byte*)cross.CompilerGetName(compiler, combinedSamplers[i].ImageId);
                if (imageName != null)
                {
                    cross.CompilerSetName(compiler, combinedSamplers[i].CombinedId, imageName);
                }
            }
        }

        private static void RenameStageIO(Cross cross, SpvcCompiler* vsCompiler, SpvcCompiler* fsCompiler)
        {
            // Rename vertex outputs to vdspv_fsinN
            SpvcResources* vsResources = null;
            Check(cross, null, cross.CompilerCreateShaderResources(vsCompiler, &vsResources));
            ReflectedResource* vsOutputs = null;
            nuint vsOutputCount = 0;
            cross.ResourcesGetResourceListForType(vsResources, ResourceType.StageOutput, &vsOutputs, &vsOutputCount);

            for (nuint i = 0; i < vsOutputCount; i++)
            {
                uint location = cross.CompilerGetDecoration(vsCompiler, vsOutputs[i].Id, Decoration.Location);
                SetNativeName(cross, vsCompiler, vsOutputs[i].Id, $"vdspv_fsin{location}");
            }

            // Rename fragment inputs to vdspv_fsinN
            SpvcResources* fsResources = null;
            Check(cross, null, cross.CompilerCreateShaderResources(fsCompiler, &fsResources));
            ReflectedResource* fsInputs = null;
            nuint fsInputCount = 0;
            cross.ResourcesGetResourceListForType(fsResources, ResourceType.StageInput, &fsInputs, &fsInputCount);

            for (nuint i = 0; i < fsInputCount; i++)
            {
                uint location = cross.CompilerGetDecoration(fsCompiler, fsInputs[i].Id, Decoration.Location);
                SetNativeName(cross, fsCompiler, fsInputs[i].Id, $"vdspv_fsin{location}");
            }
        }

        #endregion

        #region Reflection

        private static VertexElementDescription[] ReflectVertexInputs(Cross cross, SpvcCompiler* compiler)
        {
            SpvcResources* resources = null;
            Check(cross, null, cross.CompilerCreateShaderResources(compiler, &resources));
            ReflectedResource* inputs = null;
            nuint inputCount = 0;
            cross.ResourcesGetResourceListForType(resources, ResourceType.StageInput, &inputs, &inputCount);

            uint elementCount = 0;
            for (nuint i = 0; i < inputCount; i++)
            {
                uint location = cross.CompilerGetDecoration(compiler, inputs[i].Id, Decoration.Location);
                elementCount = Math.Max(elementCount, location + 1);
            }

            var elements = new VertexElementDescription[elementCount];
            for (nuint i = 0; i < inputCount; i++)
            {
                uint location = cross.CompilerGetDecoration(compiler, inputs[i].Id, Decoration.Location);

                byte* namePtr = (byte*)cross.CompilerGetName(compiler, inputs[i].Id);
                string name = namePtr != null ? Marshal.PtrToStringUTF8((nint)namePtr) ?? "" : "";
                if (string.IsNullOrEmpty(name))
                {
                    name = $"_input{location}";
                }

                CrossType* type = cross.CompilerGetTypeHandle(compiler, inputs[i].BaseTypeId);
                Basetype baseType = cross.TypeGetBasetype(type);
                uint vecSize = cross.TypeGetVectorSize(type);

                VertexElementFormat format = baseType switch
                {
                    Basetype.FP32 => vecSize switch
                    {
                        1 => VertexElementFormat.Float1,
                        2 => VertexElementFormat.Float2,
                        3 => VertexElementFormat.Float3,
                        4 => VertexElementFormat.Float4,
                        _ => VertexElementFormat.Float1
                    },
                    Basetype.Int32 => vecSize switch
                    {
                        1 => VertexElementFormat.Int1,
                        2 => VertexElementFormat.Int2,
                        3 => VertexElementFormat.Int3,
                        4 => VertexElementFormat.Int4,
                        _ => VertexElementFormat.Int1
                    },
                    Basetype.Uint32 => vecSize switch
                    {
                        1 => VertexElementFormat.UInt1,
                        2 => VertexElementFormat.UInt2,
                        3 => VertexElementFormat.UInt3,
                        4 => VertexElementFormat.UInt4,
                        _ => VertexElementFormat.UInt1
                    },
                    _ => throw new SpirvCompilationException($"Unhandled SPIR-V vertex input data type: {baseType}")
                };

                elements[location] = new VertexElementDescription(
                    name,
                    VertexElementSemantic.TextureCoordinate,
                    format);
            }

            return elements;
        }

        private static ResourceLayoutDescription[] BuildResourceLayouts(
            SortedDictionary<BindingKey, ResourceInfo> allResources, bool isCompute)
        {
            uint setCount = 0;
            var setSizes = new Dictionary<uint, uint>();

            foreach (var kvp in allResources)
            {
                uint set = kvp.Key.Set;
                if (set + 1 > setCount) setCount = set + 1;
                uint needed = kvp.Key.Binding + 1;
                if (!setSizes.TryGetValue(set, out uint current) || needed > current)
                    setSizes[set] = needed;
            }

            if (setCount == 0 && allResources.Count == 0)
            {
                setCount = 1;
                setSizes[0] = 0;
            }

            var layouts = new ResourceLayoutDescription[setCount];
            for (uint i = 0; i < setCount; i++)
            {
                uint size = setSizes.TryGetValue(i, out uint s) ? s : 0;
                var elements = new ResourceLayoutElementDescription[size];
                for (uint j = 0; j < size; j++)
                {
                    elements[j] = new ResourceLayoutElementDescription(
                        null,
                        ResourceKind.UniformBuffer,
                        ShaderStages.None,
                        (ResourceLayoutElementOptions)2); // "Unused" marker
                }
                layouts[i].Elements = elements;
            }

            foreach (var kvp in allResources)
            {
                ShaderStages stages = ShaderStages.None;
                if (kvp.Value.IDs[0] != 0)
                {
                    stages |= isCompute ? ShaderStages.Compute : ShaderStages.Vertex;
                }
                if (kvp.Value.IDs[1] != 0)
                {
                    stages |= ShaderStages.Fragment;
                }

                layouts[kvp.Key.Set].Elements[kvp.Key.Binding] = new ResourceLayoutElementDescription(
                    kvp.Value.Name,
                    kvp.Value.Kind,
                    stages);
            }

            return layouts;
        }

        #endregion

        #region Native String Helpers

        private static string GetNativeName(Cross cross, SpvcCompiler* compiler, uint id, uint fallbackId)
        {
            byte* namePtr = (byte*)cross.CompilerGetName(compiler, id);
            string name = namePtr != null ? Marshal.PtrToStringUTF8((nint)namePtr) ?? "" : "";
            if (string.IsNullOrEmpty(name))
            {
                namePtr = (byte*)cross.CompilerGetName(compiler, fallbackId);
                name = namePtr != null ? Marshal.PtrToStringUTF8((nint)namePtr) ?? "" : "";
            }
            return name;
        }

        private static void SetNativeName(Cross cross, SpvcCompiler* compiler, uint id, string name)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(name + '\0');
            fixed (byte* namePtr = nameBytes)
            {
                cross.CompilerSetName(compiler, id, namePtr);
            }
        }

        private static bool HasBufferBlockDecoration(Cross cross, SpvcCompiler* compiler, uint id, Decoration decoration)
        {
            Decoration* decorations = null;
            nuint count = 0;
            cross.CompilerGetBufferBlockDecorations(compiler, id, &decorations, &count);
            for (nuint i = 0; i < count; i++)
            {
                if (decorations[i] == decoration)
                    return true;
            }
            return false;
        }

        #endregion
    }
}
