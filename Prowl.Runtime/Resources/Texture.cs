using Veldrid;
using System;

namespace Prowl.Runtime
{
    /// <summary>
    /// This is the base class for all texture types and manages some of their internal workings.
    /// </summary>
    /// <remarks>
    /// Much of this class is comprised of validations and utilities to make working with a <see cref="Veldrid.Texture"/> safer.
    /// </remarks>
    public abstract class Texture : EngineObject
    {
        /// <summary>The handle for the GL Texture Object.</summary>
        public Veldrid.Texture InternalTexture { get; protected set; }

        /// <summary>The type of this <see cref="Texture"/>, such as 1D, 2D, Multisampled 2D, Array 2D, CubeMap, etc.</summary>
        public TextureType Type => InternalTexture.Type;
        public PixelFormat Format => InternalTexture.Format;
        public TextureUsage Usage => InternalTexture.Usage;
        public uint MipLevels => InternalTexture.MipLevels;


        /// <summary>Gets whether this <see cref="Texture"/> has generated mipmaps.</summary>
        public bool IsMipmapped { get; protected set; }

        /// <summary>Gets whether this <see cref="Texture"/> can be mipmapped (depends on texture type).</summary>
        public bool IsMipmappable => InternalTexture.Usage.HasFlag(TextureUsage.GenerateMipmaps);

        internal Texture(TextureDescription description) : base("New Texture") 
        { 
            RecreateInternalTexture(description);
        }

        protected void RecreateInternalTexture(TextureDescription description)
        {
            if (InternalTexture != null)
                InternalTexture.Dispose();

            ValidateDescription(description);
            InternalTexture = Graphics.Device.ResourceFactory.CreateTexture(ref description);
        }

        public void Dispose()
        {
            InternalTexture.Dispose();
        }

        /// <summary>
        /// Gets the size in bytes of a <see cref="PixelFormat"/>.
        /// </summary>
        public static uint PixelFormatBytes(PixelFormat format)
        {
            return format switch 
            {
                // BGRA 8-bit 4-channel
                PixelFormat.B8_G8_R8_A8_UNorm => 4,
                PixelFormat.B8_G8_R8_A8_UNorm_SRgb => 4,

                // Depth + stencil 2-channel
                PixelFormat.D24_UNorm_S8_UInt => 4, 	
                PixelFormat.D32_Float_S8_UInt => 5, 	

                // RGBA Packed 4-channel
                PixelFormat.R10_G10_B10_A2_UInt => 4, 	
                PixelFormat.R10_G10_B10_A2_UNorm => 4,

                // RGB Packed 3-channel
                PixelFormat.R11_G11_B10_Float => 4, 
                
                // RGBA 16-bit 4-channel
                PixelFormat.R16_G16_B16_A16_Float => 8,
                PixelFormat.R16_G16_B16_A16_SInt => 8, 	
                PixelFormat.R16_G16_B16_A16_SNorm => 8,
                PixelFormat.R16_G16_B16_A16_UInt => 8,
                PixelFormat.R16_G16_B16_A16_UNorm => 8,

                // RGBA 32-bit 4-channel
                PixelFormat.R32_G32_B32_A32_Float => 16, 	
                PixelFormat.R32_G32_B32_A32_SInt => 16, 	
                PixelFormat.R32_G32_B32_A32_UInt => 16, 	

                // RG 16-bit 2-channel
                PixelFormat.R16_G16_Float => 4,
                PixelFormat.R16_G16_SInt => 4,
                PixelFormat.R16_G16_SNorm => 4,
                PixelFormat.R16_G16_UInt => 4,
                PixelFormat.R16_G16_UNorm => 4,

                // R 16-bit 1-channel
                PixelFormat.R16_Float => 2,
                PixelFormat.R16_SInt => 2, 	
                PixelFormat.R16_SNorm => 2, 	
                PixelFormat.R16_UInt => 2, 	
                PixelFormat.R16_UNorm => 2, 	
                PixelFormat.R32_Float => 4, 	

                // RG 32-bit 2-channel
                PixelFormat.R32_G32_Float => 8, 	
                PixelFormat.R32_G32_SInt => 8,
                PixelFormat.R32_G32_UInt => 8,

                // R 32-bit 1-channel
                PixelFormat.R32_SInt => 4,
                PixelFormat.R32_UInt => 4, 	

                // RGBA 8-bit 4-channel
                PixelFormat.R8_G8_B8_A8_SInt => 4, 	
                PixelFormat.R8_G8_B8_A8_SNorm => 4, 	
                PixelFormat.R8_G8_B8_A8_UInt => 4, 	
                PixelFormat.R8_G8_B8_A8_UNorm => 4, 	
                PixelFormat.R8_G8_B8_A8_UNorm_SRgb => 4, 	

                // RG 8-bit 2-channel
                PixelFormat.R8_G8_SInt => 2, 	
                PixelFormat.R8_G8_SNorm => 2, 	
                PixelFormat.R8_G8_UInt => 2, 	
                PixelFormat.R8_G8_UNorm => 2, 	

                // R 8-bit 1-channel
                PixelFormat.R8_SInt => 1, 	
                PixelFormat.R8_SNorm => 1, 	
                PixelFormat.R8_UInt => 1, 	
                PixelFormat.R8_UNorm => 1,

                // Unknwons such as compressed BC(1-4) formats:
                // Should be replaced with a more appripriate exception
                _ => throw new Exception($"Cannot determine format byte size for {format}"),
            };
        }

        public static uint GetDimension(uint largestLevelDimension, uint mipLevel)
        {
            uint ret = largestLevelDimension;
            for (uint i = 0; i < mipLevel; i++)
            {
                ret /= 2;
            }

            return Math.Max(1, ret);
        }

        public void GenerateMipmaps()
        {
            if (!IsMipmappable)
                throw new InvalidOperationException($"Cannot generate mipmaps on a non-mipmappable texture. Ensure texture is created with the {TextureUsage.GenerateMipmaps} flag.");

            CommandList commandList = Graphics.Device.ResourceFactory.CreateCommandList();

            commandList.GenerateMipmaps(InternalTexture);

            Graphics.Device.SubmitCommands(commandList);

            IsMipmapped = true;
        }

        /// <summary>
        /// Gets the estimated memory usage in bytes of the <see cref="Texture"/>.
        /// </summary>
        public uint GetMemoryUsage() 
        {
            return InternalTexture.Width * InternalTexture.Height * InternalTexture.Depth * InternalTexture.ArrayLayers * PixelFormatBytes(Format);
        }

// -----------------------------------------------------
// ------------- CPU Texture Manipulation --------------
// -----------------------------------------------------

        private static void InternalFencedCopyTexture(Veldrid.Texture source, Veldrid.Texture target)
        {
            Fence fence = Graphics.ResourceFactory.CreateFence(false);
            CommandList commandList = Graphics.ResourceFactory.CreateCommandList();

            commandList.Begin();
            commandList.CopyTexture(source, target);
            commandList.End();

            Graphics.Device.SubmitCommands(commandList, fence);
            Graphics.Device.WaitForFence(fence);
        }


        unsafe protected void InternalSetDataPtr(void* data, Vector3Int rectPos, Vector3Int rectSize, uint layer, uint mipLevel)
        {
            ValidateRectOperation(rectPos, rectSize, layer, mipLevel);

            uint mipWidth = GetDimension(InternalTexture.Width, mipLevel);
            uint mipHeight = GetDimension(InternalTexture.Height, mipLevel);
            uint mipDepth = GetDimension(InternalTexture.Depth, mipLevel);

            uint mipLevelSize = mipWidth * mipHeight * mipDepth * PixelFormatBytes(Format);

            if (!InternalTexture.Usage.HasFlag(TextureUsage.Staging))
            {
                TextureDescription description = new()
                {
                    Width = InternalTexture.Width,
                    Height = InternalTexture.Height,
                    Depth = InternalTexture.Depth,
                    ArrayLayers = InternalTexture.ArrayLayers,
                    Type = Type,
                    MipLevels = MipLevels,
                    Usage = TextureUsage.Staging,
                    Format = Format, 
                    SampleCount = TextureSampleCount.Count1,
                };

                Veldrid.Texture StagingTexture = Graphics.Device.ResourceFactory.CreateTexture(ref description);

                Graphics.Device.UpdateTexture(StagingTexture, (IntPtr)data, mipLevelSize, (uint)rectPos.x, (uint)rectPos.y, (uint)rectPos.z, (uint)rectSize.x, (uint)rectSize.y, (uint)rectSize.z, mipLevel, layer);

                InternalFencedCopyTexture(StagingTexture, InternalTexture);

                StagingTexture.Dispose();
            }
            else
            {
                Graphics.Device.UpdateTexture(InternalTexture, (IntPtr)data, mipLevelSize, (uint)rectPos.x, (uint)rectPos.y, (uint)rectPos.z, (uint)rectSize.x, (uint)rectSize.y, (uint)rectSize.z, mipLevel, layer);
            }
        }

        unsafe protected void InternalSetData<T>(Memory<T> data, Vector3Int rectPos, Vector3Int rectSize, uint layer, uint mipLevel) where T : unmanaged
        {
            if (data.Length * sizeof(T) < rectSize.x * rectSize.y * rectSize.z)
                throw new ArgumentException("Not enough pixel data", nameof(data));

            fixed (void* ptr = data.Span)
                InternalSetDataPtr(ptr, rectPos, rectSize, layer, mipLevel);
        }

        unsafe protected void InternalCopyDataPtr(void* dataPtr, out uint rowPitch, out uint depthPitch, uint subresource)
        {
            Veldrid.Texture mapTexture = InternalTexture;

            if (!InternalTexture.Usage.HasFlag(TextureUsage.Staging))
            {
                TextureDescription description = new()
                {
                    Width = InternalTexture.Width,
                    Height = InternalTexture.Height,
                    Depth = InternalTexture.Depth,
                    ArrayLayers = InternalTexture.ArrayLayers,
                    Type = Type,
                    MipLevels = MipLevels,
                    Usage = TextureUsage.Staging,
                    Format = Format, 
                    SampleCount = TextureSampleCount.Count1,
                };

                mapTexture = Graphics.Device.ResourceFactory.CreateTexture(ref description);
                InternalFencedCopyTexture(InternalTexture, mapTexture);
            }

            MappedResource resource = Graphics.Device.Map(mapTexture, MapMode.Read, subresource);

            rowPitch = resource.RowPitch;
            depthPitch = resource.DepthPitch;

            Buffer.MemoryCopy((void*)resource.Data, dataPtr, resource.SizeInBytes, resource.SizeInBytes);

            Graphics.Device.Unmap(mapTexture, subresource);

            if (mapTexture != InternalTexture)
                mapTexture.Dispose();
        }

        unsafe protected void InternalCopyData<T>(Memory<T> data, uint subresource) where T : unmanaged
        {
            Veldrid.Texture mapTexture = InternalTexture;

            if (!InternalTexture.Usage.HasFlag(TextureUsage.Staging))
            {
                TextureDescription description = new()
                {
                    Width = InternalTexture.Width,
                    Height = InternalTexture.Height,
                    Depth = InternalTexture.Depth,
                    ArrayLayers = InternalTexture.ArrayLayers,
                    Type = Type,
                    MipLevels = MipLevels,
                    Usage = TextureUsage.Staging,
                    Format = Format, 
                    SampleCount = TextureSampleCount.Count1,
                };

                mapTexture = Graphics.Device.ResourceFactory.CreateTexture(ref description);
                InternalFencedCopyTexture(InternalTexture, mapTexture);
            }

            MappedResource resource = Graphics.Device.Map(mapTexture, MapMode.Read, subresource);
        
            if (data.Length * sizeof(T) < resource.SizeInBytes)
                throw new ArgumentException("Insufficient space to store the requested pixel data", nameof(data));

            fixed (void* ptr = data.Span)
                Buffer.MemoryCopy((void*)resource.Data, ptr, data.Length * sizeof(T), resource.SizeInBytes);

            Graphics.Device.Unmap(mapTexture, subresource);

            if (mapTexture != InternalTexture)
                mapTexture.Dispose();
        }

// -----------------------------------------------------
// -------------------  Validation  --------------------
// -----------------------------------------------------

        private void ValidateRectOperation(Vector3Int rect, Vector3Int size, uint layer, uint mipLevel)
        {
            if (rect.x < 0 || rect.x >= InternalTexture.Width)
                throw new ArgumentOutOfRangeException("Rect X", rect.x, "Rect X must be in the range [0, " + InternalTexture.Width + "]");

            if (rect.y < 0 || rect.y >= InternalTexture.Height)
                throw new ArgumentOutOfRangeException("Rect Y", rect.y, "Rect Y must be in the range [0, " + InternalTexture.Height + "]");
            
            if (rect.z < 0 || rect.z >= InternalTexture.Depth)
                throw new ArgumentOutOfRangeException("Rect Z", rect.z, "Rect Z must be in the range [0, " + InternalTexture.Depth + "]");

            if (size.x <= 0)
                throw new ArgumentOutOfRangeException("Rect Width", size.x, "Rect width must be greater than 0");

            if (size.y <= 0)
                throw new ArgumentOutOfRangeException("Rect Height", size.y, "Rect height must be greater than 0");
            
            if (size.z <= 0)
                throw new ArgumentOutOfRangeException("Rect Depth", size.z, "Rect depth must be greater than 0");

            if (size.x > InternalTexture.Width - rect.x || size.y > InternalTexture.Height - rect.y || size.z > InternalTexture.Depth - rect.z)
                throw new ArgumentOutOfRangeException("Specified area is outside of the texture's storage");

            if (layer >= InternalTexture.ArrayLayers)
                throw new ArgumentOutOfRangeException("Layer", layer, "Array layer must be in the range [0, " + InternalTexture.ArrayLayers + "]");

            if (mipLevel >= InternalTexture.MipLevels)
                throw new ArgumentOutOfRangeException("Specified mip level is outside of mip size");
        }

        private void ValidateDescription(TextureDescription description)
        {
            uint width = description.Width;
            uint height = description.Height;
            uint depth = description.Depth;
            uint layers = description.ArrayLayers;

            if (width <= 0 || width > Graphics.MaxTextureSize)
                throw new ArgumentOutOfRangeException(nameof(width), width, nameof(width) + " must be in the range (0, " + nameof(Graphics.MaxTextureSize) + ")");

            if (height <= 0 || height > Graphics.MaxTextureSize)
                throw new ArgumentOutOfRangeException(nameof(height), height, nameof(height) + " must be in the range (0, " + nameof(Graphics.MaxTextureSize) + ")");

            if (depth <= 0 || depth > Graphics.MaxTextureSize)
                throw new ArgumentOutOfRangeException(nameof(depth), depth, nameof(depth) + " must be in the range (0, " + nameof(Graphics.MaxTextureSize) + ")");

            if (layers <= 0 || layers > Graphics.MaxArrayTextureLayers)
                throw new ArgumentOutOfRangeException(nameof(layers), layers, nameof(layers) + " must be in the range (0, " + nameof(Graphics.MaxArrayTextureLayers) + ")");
        }
    }
}
