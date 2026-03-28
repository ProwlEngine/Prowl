---
uid: getting-started-part2
---

# Part 2

## Creating Graphics Resources

It's time to create some Veldrid objects which we will need to render our multi-colored quad. Let's set up some fields in our Program class.

```C#
private static CommandList _commandList;
private static DeviceBuffer _vertexBuffer;
private static DeviceBuffer _indexBuffer;
private static Shader[] _shaders;
private static Pipeline _pipeline;
```

Before our application's main `while` loop, let's call a new method, called `CreateResources`.

```C#
CreateResources();

while (window.Exists)
{
    window.PumpEvents();
}
```

We will create our resources one-by-one, using the ResourceFactory property of our GraphicsDevice. Inside of our new CreateResources method, let's add the following:

```C#
ResourceFactory factory = _graphicsDevice.ResourceFactory;
```

### Vertex and Index Buffers

Next, let's create an array to store our vertex data. For this demo, we just need simple vertices containing a normalized position, and a color. Let's define a structure which will represent each vertex. Place this structure definition after the `Program` class:

```C#
struct VertexPositionColor
{
    public Vector2 Position; // This is the position, in normalized device coordinates.
    public RgbaFloat Color; // This is the color of the vertex.
    public VertexPositionColor(Vector2 position, RgbaFloat color)
    {
        Position = position;
        Color = color;
    }
    public const uint SizeInBytes = 24;
}
```

Let's create an array of these, representing the four corners of our multi-colored quad. Put this at the end of the CreateResources method:

```C#
VertexPositionColor[] quadVertices =
{
    new VertexPositionColor(new Vector2(-0.75f, 0.75f), RgbaFloat.Red),
    new VertexPositionColor(new Vector2(0.75f, 0.75f), RgbaFloat.Green),
    new VertexPositionColor(new Vector2(-0.75f, -0.75f), RgbaFloat.Blue),
    new VertexPositionColor(new Vector2(0.75f, -0.75f), RgbaFloat.Yellow)
};
```

We will render these vertices as a Triangle Strip, so we need four indices as well. We will use 16-bit indices:

```C#
ushort[] quadIndices = { 0, 1, 2, 3 };
```

We need somewhere to store this vertex and index data that the GraphicsDevice can use for rendering. This is accomplished with two [DeviceBuffer](xref:Veldrid.DeviceBuffer) objects, which can be used to store many different types of data.

A DeviceBuffer is created with a [BufferDescription](xref:Veldrid.BufferDescription) object. For each DeviceBuffer, we need to provide the total size that it will contain, as well as how the DeviceBuffer object will be used. In our case, we want to use one as a vertex buffer, and one as an index buffer.

```C#
_vertexBuffer = factory.CreateBuffer(new BufferDescription(4 * VertexPositionColor.SizeInBytes, BufferUsage.VertexBuffer));
_indexBuffer = factory.CreateBuffer(new BufferDescription(4 * sizeof(ushort), BufferUsage.IndexBuffer));
```

We've created our DeviceBuffers, but they are empty at the moment. We need to fill them with the data contained in our `quadVertices` and `quadIndices` arrays. DeviceBuffers can be filled with data using the [GraphicsDevice.UpdateBuffer](xref:Veldrid.GraphicsDevice#Veldrid_GraphicsDevice_UpdateBuffer__1_Veldrid_DeviceBuffer_System_UInt32___0___) method:

```C#
_graphicsDevice.UpdateBuffer(_vertexBuffer, 0, quadVertices);
_graphicsDevice.UpdateBuffer(_indexBuffer, 0, quadIndices);
```

`_vertexBuffer` and `_indexBuffer` now contain all of the data from our arrays, and are accessible to the GPU.

To create a Pipeline later on, we need to know the layout of the vertex buffer that will be used. Let's create the [VertexLayoutDescription](xref:Veldrid.VertexLayoutDescription) now.

```C#
VertexLayoutDescription vertexLayout = new VertexLayoutDescription(
    new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
    new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4));
```

Our vertex data has only two elements: a 2-float position, and a 4-float color.

To create a Pipeline, we also need a set of shaders. For simplicity, we will store our shaders in strings directly in the application. Declare these new strings with your other fields:

```C#
        private const string VertexCode = @"
#version 450

layout(location = 0) in vec2 Position;
layout(location = 1) in vec4 Color;

layout(location = 0) out vec4 fsin_Color;

void main()
{
    gl_Position = vec4(Position, 0, 1);
    fsin_Color = Color;
}";

        private const string FragmentCode = @"
#version 450

layout(location = 0) in vec4 fsin_Color;
layout(location = 0) out vec4 fsout_Color;

void main()
{
    fsout_Color = fsin_Color;
}";
```

Notice that the vertex shader has two "in" variables, a `vec2` and a `vec4`. These correspond to the two `VertexElementDescription` items we defined just before.

The shaders above are written in GLSL, but other shading languages can also be used with Veldrid. When you created your project, you added a reference to the Veldrid.SPIRV package. This is a helper library which allows you to load shaders from text, compiling and translating them into the form needed at runtime. To create the `Shader` instances that we need for our Pipeline, we do the following:

```C#
ShaderDescription vertexShaderDesc = new ShaderDescription(
    ShaderStages.Vertex,
    Encoding.UTF8.GetBytes(VertexCode),
    "main");
ShaderDescription fragmentShaderDesc = new ShaderDescription(
    ShaderStages.Fragment,
    Encoding.UTF8.GetBytes(FragmentCode),
    "main");

_shaders = factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);
```

`CreateFromSpirv` is an extension method defined in the `Veldrid.SPIRV` namespace, so make sure there is a `using` statement at the top of your file for that namespace.

### Pipeline

Another object we need is a [Pipeline](xref:Veldrid.Pipeline). There are two types of Pipelines in Veldrid: graphics and compute. In this tutorial, we are going to be creating a graphics Pipeline. This is an object which encapsulates all of the necessary graphics state for drawing primitives, like triangle strips. One piece of information is the set of shaders that will be used -- we have that already. There are several other pieces of information we need.

```C#
GraphicsPipelineDescription pipelineDescription = new GraphicsPipelineDescription();
pipelineDescription.BlendState = BlendStateDescription.SingleOverrideBlend;
```

The `BlendState` controls how the results from rendering are blended into the output textures. This is a simple example -- we only have a single output texture, and we want our new results to completely overwrite the existing data in our output.

```C#
pipelineDescription.DepthStencilState = new DepthStencilStateDescription(
    depthTestEnabled: true,
    depthWriteEnabled: true,
    comparisonKind: ComparisonKind.LessEqual);
```

In this example, the depth state is not very important -- we're only drawing a static 2D object. We've set up the depth-stencil state such that all depth testing and writing is enabled, anyways.

```C#
pipelineDescription.RasterizerState = new RasterizerStateDescription(
    cullMode: FaceCullMode.Back,
    fillMode: PolygonFillMode.Solid,
    frontFace: FrontFace.Clockwise,
    depthClipEnabled: true,
    scissorTestEnabled: false);
```

The rasterizer state lets you control various properties of the fixed-function rasterizer: which face gets culled, how that face is determined, how polygons are filled, and whether depth clipping and scissor testing are enabled. For this example, we've chosen the regular default values for everything.

```C#
pipelineDescription.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
```

We are rendering our quad as a triangle strip. If we wanted to use a different topology, we would need to modify our index data. Index data of `{ 0, 1, 2, 0, 2, 3 }` would work for a triangle list.

```C#
pipelineDescription.ResourceLayouts = System.Array.Empty<ResourceLayout>();
```

Our shaders do not read from any resources, so we use an empty array here.

```C#
pipelineDescription.ShaderSet = new ShaderSetDescription(
    vertexLayouts: new VertexLayoutDescription[] { vertexLayout },
    shaders: _shaders);
```

We're passing in our previously-created shader stages and vertex layout here. This controls which shaders are used for rendering when the Pipeline is active.

```C#
pipelineDescription.Outputs = _graphicsDevice.SwapchainFramebuffer.OutputDescription;
```

Every `Pipeline` in Veldrid needs to know how many outputs it has, and what the format of each is. Since we are going to be rendering directly to the application's swapchain, we will use the swapchain's [OutputDescription](xref:Veldrid.Framebuffer#Veldrid_Framebuffer_OutputDescription).

Finally, we can create the Pipeline.

```C#
_pipeline = factory.CreateGraphicsPipeline(pipelineDescription);
```

### CommandList

A [CommandList](xref:Veldrid.CommandList) is a device resource that lets you record and execute graphics commands. You can't do anything interesting in Veldrid without one, and advanced techniques make use of many in parallel. In this program, we will use a single CommandList to issue our rendering commands. Creating a CommandList is simple:

```C#
_commandList = factory.CreateCommandList();
```

We have successfully created all of the device resources that we need. In the next section, we will draw our quad, and then do some cleanup.

## [Next: Part 3](xref:getting-started-part3)

Here is what our application should look like at the end of this section:

```C#
using System.Numerics;
using System.Text;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.SPIRV;
using Veldrid.StartupUtilities;

namespace GettingStarted
{
    class Program
    {
        private static GraphicsDevice _graphicsDevice;
        private static CommandList _commandList;
        private static DeviceBuffer _vertexBuffer;
        private static DeviceBuffer _indexBuffer;
        private static Shader[] _shaders;
        private static Pipeline _pipeline;

        private const string VertexCode = @"
#version 450

layout(location = 0) in vec2 Position;
layout(location = 1) in vec4 Color;

layout(location = 0) out vec4 fsin_Color;

void main()
{
    gl_Position = vec4(Position, 0, 1);
    fsin_Color = Color;
}";

        private const string FragmentCode = @"
#version 450

layout(location = 0) in vec4 fsin_Color;
layout(location = 0) out vec4 fsout_Color;

void main()
{
    fsout_Color = fsin_Color;
}";

        static void Main()
        {
            WindowCreateInfo windowCI = new WindowCreateInfo()
            {
                X = 100,
                Y = 100,
                WindowWidth = 960,
                WindowHeight = 540,
                WindowTitle = "Veldrid Tutorial"
            };
            Sdl2Window window = VeldridStartup.CreateWindow(ref windowCI);

            _graphicsDevice = VeldridStartup.CreateGraphicsDevice(window);

            CreateResources();

            while (window.Exists)
            {
                window.PumpEvents();
            }
        }

        private static void CreateResources()
        {
            ResourceFactory factory = _graphicsDevice.ResourceFactory;

            VertexPositionColor[] quadVertices =
            {
                new VertexPositionColor(new Vector2(-0.75f, 0.75f), RgbaFloat.Red),
                new VertexPositionColor(new Vector2(0.75f, 0.75f), RgbaFloat.Green),
                new VertexPositionColor(new Vector2(-0.75f, -0.75f), RgbaFloat.Blue),
                new VertexPositionColor(new Vector2(0.75f, -0.75f), RgbaFloat.Yellow)
            };

            ushort[] quadIndices = { 0, 1, 2, 3 };

            _vertexBuffer = factory.CreateBuffer(new BufferDescription(4 * VertexPositionColor.SizeInBytes, BufferUsage.VertexBuffer));
            _indexBuffer = factory.CreateBuffer(new BufferDescription(4 * sizeof(ushort), BufferUsage.IndexBuffer));

            _graphicsDevice.UpdateBuffer(_vertexBuffer, 0, quadVertices);
            _graphicsDevice.UpdateBuffer(_indexBuffer, 0, quadIndices);

            VertexLayoutDescription vertexLayout = new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4));

            ShaderDescription vertexShaderDesc = new ShaderDescription(
                ShaderStages.Vertex,
                Encoding.UTF8.GetBytes(VertexCode),
                "main");
            ShaderDescription fragmentShaderDesc = new ShaderDescription(
                ShaderStages.Fragment,
                Encoding.UTF8.GetBytes(FragmentCode),
                "main");

            _shaders = factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);

            GraphicsPipelineDescription pipelineDescription = new GraphicsPipelineDescription();
            pipelineDescription.BlendState = BlendStateDescription.SingleOverrideBlend;

            pipelineDescription.DepthStencilState = new DepthStencilStateDescription(
                depthTestEnabled: true,
                depthWriteEnabled: true,
                comparisonKind: ComparisonKind.LessEqual);

            pipelineDescription.RasterizerState = new RasterizerStateDescription(
                cullMode: FaceCullMode.Back,
                fillMode: PolygonFillMode.Solid,
                frontFace: FrontFace.Clockwise,
                depthClipEnabled: true,
                scissorTestEnabled: false);

            pipelineDescription.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
            pipelineDescription.ResourceLayouts = System.Array.Empty<ResourceLayout>();

            pipelineDescription.ShaderSet = new ShaderSetDescription(
                vertexLayouts: new VertexLayoutDescription[] { vertexLayout },
                shaders: _shaders);

            pipelineDescription.Outputs = _graphicsDevice.SwapchainFramebuffer.OutputDescription;
            _pipeline = factory.CreateGraphicsPipeline(pipelineDescription);

            _commandList = factory.CreateCommandList();
        }
    }

    struct VertexPositionColor
    {
        public Vector2 Position; // This is the position, in normalized device coordinates.
        public RgbaFloat Color; // This is the color of the vertex.
        public VertexPositionColor(Vector2 position, RgbaFloat color)
        {
            Position = position;
            Color = color;
        }
        public const uint SizeInBytes = 24;
    }
}

```