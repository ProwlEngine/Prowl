using Prowl.Runtime;
using Prowl.Runtime.SceneManagement;
using Prowl.Runtime.Utils;
using Veldrid;
using System.Text;

namespace Prowl.Test;

internal static class Program
{
        private const string VertexCode = @"
#version 450

layout(set = 0, binding = 0) uniform WorldBuffer
{
    mat4 World;
};

layout(set = 0, binding = 1) uniform ViewBuffer
{
    mat4 View;
};

layout(set = 0, binding = 2) uniform ProjectionBuffer
{
    mat4 Projection;
};

layout(set = 1, binding = 0) uniform texture2D SurfaceTexture;
layout(set = 1, binding = 1) uniform sampler SurfaceSampler;

layout(set = 1, binding = 2) uniform ColorBuffer
{
    vec4 ColorValue;
};

layout(location = 0) in vec3 Position;
layout(location = 1) in vec2 TexCoords;

layout(location = 0) out vec2 fsin_texCoords;

void main()
{
    vec4 worldPosition = World * vec4(Position, 1);

    float lat = acos(worldPosition.y / length(worldPosition)); // theta
    float lon = atan(worldPosition.x / worldPosition.z); // phi

    worldPosition *= texture(sampler2D(SurfaceTexture, SurfaceSampler), vec2(lat, lon));

    vec4 viewPosition = View * worldPosition;
    vec4 clipPosition = Projection * viewPosition;

    clipPosition.y *= -1.0;

    gl_Position = clipPosition;
    fsin_texCoords = TexCoords;
}";

        private const string FragmentCode = @"
#version 450

layout(location = 0) in vec2 fsin_texCoords;
layout(location = 0) out vec4 fsout_color;

layout(set = 1, binding = 0) uniform texture2D SurfaceTexture;
layout(set = 1, binding = 1) uniform sampler SurfaceSampler;

layout(set = 1, binding = 2) uniform ColorBuffer
{
    vec4 ColorValue;
};

void main()
{
    fsout_color = texture(sampler2D(SurfaceTexture, SurfaceSampler), fsin_texCoords) * ColorValue;
}";

    static DirectoryInfo Data => new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);

    static Texture2D catTex;
    static Prowl.Runtime.Shader sphereShader;
    static Material sphereMat;
    static Mesh sphereMesh;

    public static int Main(string[] args)
    {

        Application.isPlaying = true;
        Application.DataPath = Data.FullName;

        Application.Initialize += () =>
        {
            catTex = Texture2DLoader.FromFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cat.png"));

            // Pass creation info (Name, tags)
            Pass pass = new Pass("DrawCube", []);

            pass.CreateProgram(Graphics.CreateFromSpirv(VertexCode, FragmentCode));

            // The input channels the vertex shader expects
            pass.AddVertexInput(MeshResource.Position);
            pass.AddVertexInput(MeshResource.UV0);

            // MVP matrix resources
            pass.AddResourceElement([ 
                new ShaderResource("WorldMatrix", ResourceType.Matrix4x4, ShaderStages.Vertex),
                new ShaderResource("ViewMatrix", ResourceType.Matrix4x4, ShaderStages.Vertex), 
                new ShaderResource("ProjectionMatrix", ResourceType.Matrix4x4, ShaderStages.Vertex) 
            ]);
            
            // Other shader resources
            pass.AddResourceElement([
                new ShaderResource("SurfaceTexture", ResourceType.Texture, ShaderStages.Vertex | ShaderStages.Fragment),
                new ShaderResource("SurfaceTexture", ResourceType.Sampler, ShaderStages.Vertex | ShaderStages.Fragment),
                new ShaderResource("ColorValue", ResourceType.Vector4, ShaderStages.Vertex | ShaderStages.Fragment) 
            ]);

            pass.cullMode = FaceCullMode.None;

            sphereShader = new Prowl.Runtime.Shader(pass);
            sphereMat = new Material(sphereShader);
            sphereMesh = Mesh.CreateSphere(1.0f, 40, 40);

            sphereMat.SetTexture("SurfaceTexture", catTex);
            sphereMat.SetColor("ColorValue", new Color(0.5f, 0.66f, 0.71f, 1.0f));
        };

        Application.Update += () =>
        {

        };

        Application.Render += () =>
        {
            Graphics.StartFrame();

            sphereMat.SetColor("ColorValue", new Color((float)Math.Sin(Time.time), (float)Math.Cos(Time.time), 0.75f, 1.0f));

            Matrix4x4 world = Matrix4x4.CreateWorld(Vector3.zero, Vector3.forward, Vector3.up);
            world *= Matrix4x4.CreateFromAxisAngle(Vector3.up, (float)Time.time * 0.25f);

            Graphics.DrawMesh(sphereMesh, sphereMat, world, 0, PolygonFillMode.Wireframe);

            Graphics.EndFrame();
        };

        Application.Quitting += () =>
        {

        };

        Application.Run("Prowl Test", 1920, 1080, null, false);

        return 0;
    }

}
