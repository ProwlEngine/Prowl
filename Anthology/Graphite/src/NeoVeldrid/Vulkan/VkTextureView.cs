using Silk.NET.Vulkan;
using static NeoVeldrid.Vk.VulkanUtil;

namespace NeoVeldrid.Vk
{
    internal unsafe class VkTextureView : TextureView
    {
        private readonly VkGraphicsDevice _gd;
        private readonly ImageView _imageView;
        private bool _destroyed;
        private string _name;

        public ImageView ImageView => _imageView;

        public new VkTexture Target => (VkTexture)base.Target;

        public ResourceRefCount RefCount { get; }

        public override bool IsDisposed => _destroyed;

        public VkTextureView(VkGraphicsDevice gd, ref TextureViewDescription description)
            : base(ref description)
        {
            _gd = gd;
            ImageViewCreateInfo imageViewCI = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo
            };
            VkTexture tex = Util.AssertSubtype<Texture, VkTexture>(description.Target);
            imageViewCI.Image = tex.OptimalDeviceImage;
            imageViewCI.Format = VkFormats.VdToVkPixelFormat(Format, (Target.Usage & TextureUsage.DepthStencil) != 0);

            ImageAspectFlags aspectFlags;
            if ((description.Target.Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil)
            {
                aspectFlags = ImageAspectFlags.DepthBit;
            }
            else
            {
                aspectFlags = ImageAspectFlags.ColorBit;
            }

            imageViewCI.SubresourceRange = new ImageSubresourceRange(
                aspectFlags,
                description.BaseMipLevel,
                description.MipLevels,
                description.BaseArrayLayer,
                description.ArrayLayers);

            if ((tex.Usage & TextureUsage.Cubemap) == TextureUsage.Cubemap)
            {
                imageViewCI.ViewType = description.ArrayLayers == 1 ? ImageViewType.TypeCube : ImageViewType.TypeCubeArray;
                imageViewCI.SubresourceRange.LayerCount *= 6;
            }
            else
            {
                switch (tex.Type)
                {
                    case TextureType.Texture1D:
                        imageViewCI.ViewType = description.ArrayLayers == 1
                            ? ImageViewType.Type1D
                            : ImageViewType.Type1DArray;
                        break;
                    case TextureType.Texture2D:
                        imageViewCI.ViewType = description.ArrayLayers == 1
                            ? ImageViewType.Type2D
                            : ImageViewType.Type2DArray;
                        break;
                    case TextureType.Texture3D:
                        imageViewCI.ViewType = ImageViewType.Type3D;
                        break;
                }
            }

            _gd.Vk.CreateImageView(_gd.Device, in imageViewCI, null, out _imageView);
            RefCount = new ResourceRefCount(DisposeCore);
        }

        public override string Name
        {
            get => _name;
            set
            {
                _name = value;
                _gd.SetResourceName(this, value);
            }
        }

        public override void Dispose()
        {
            RefCount.Decrement();
        }

        private void DisposeCore()
        {
            if (!_destroyed)
            {
                _destroyed = true;
                _gd.Vk.DestroyImageView(_gd.Device, ImageView, null);
            }
        }
    }
}
