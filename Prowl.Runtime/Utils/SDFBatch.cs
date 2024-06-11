using System.Collections.Generic;

namespace Prowl.Runtime
{
    public class SDFBatch
    {
        #warning Veldrid change
        public class GraphicsVertexArray { }
        public class GraphicsBuffer { }
        public class GraphicsProgram { }

        private struct Vertex
        {
            public float x, y;
            public float u, v;
        }

        private RenderTexture target;

        private GraphicsVertexArray? vao;
        private GraphicsBuffer vbo;
        private List<Vertex> vertices = new List<Vertex>(1000);
        private Mesh mesh;
        private GraphicsProgram shader;

        private int w, h;

        public bool IsUploaded { get; private set; }

        public SDFBatch(int width, int height)
        {
            #warning Veldrid change
            /*
            target = new RenderTexture(width, height, 1, false, [ TextureImageFormat.Color4b ]);
            w = width;
            h = height;

            vbo = Graphics.Device.CreateBuffer(BufferType.VertexBuffer, new byte[0], true);

            var format = new VertexFormat([
                new VertexFormat.Element((uint)0, VertexFormat.VertexType.Float, 3),
                new VertexFormat.Element((uint)1, VertexFormat.VertexType.Float, 4)
            ]);

            vao = Graphics.Device.CreateVertexArray(format, vbo, null);

            shader = Graphics.Device.CompileProgram(FragmentShader, VertexShader, "");

            IsUploaded = false;
            */
        }

        public void Resize(int width, int height)
        {
            #warning Veldrid change
            /*
            target?.Dispose();
            target = new RenderTexture(width, height, 1, false, [ TextureImageFormat.Color4b ]);
            w = width;
            h = height;
            */
        }

        public void Reset()
        {
            vertices.Clear();
            IsUploaded = false;
        }

        /// <summary>
        /// Ensure all arguments are Normalized!
        /// </summary>
        public void RectUnsafe(float x, float y, float width, float height)
        {
            // arguments are normalized
            // Convert to Normalized Device Coordinates
            x = x * 2 - 1;
            y = y * 2 - 1;
            width *= 2;
            height *= 2;


            vertices.Add(new Vertex { x = x, y = y, u = 0, v = 0 });
            vertices.Add(new Vertex { x = x + width, y = y, u = 1, v = 0 });
            vertices.Add(new Vertex { x = x + width, y = y + height, u = 1, v = 1 });
            vertices.Add(new Vertex { x = x, y = y + height, u = 0, v = 1 });
        }

        public void Upload()
        {
            if (vertices.Count == 0) return;

            #warning Veldrid change
            //Graphics.Device.SetBuffer(vbo, vertices.ToArray(), true);

            IsUploaded = true;
        }

        public void Draw()
        {
            if (vertices.Count == 0 || vao == null) return;

            #warning Veldrid change
            /*
            target.Begin();
            Graphics.Clear();
            Graphics.Device.BindProgram(shader);
            Graphics.Device.BindVertexArray(vao);
            Graphics.Device.Draw(Topology.Quads, 0, (uint)vertices.Count);
            target.End();
            */
        }


        private const string VertexShader = @"
            #version 410

            layout(location = 0) in vec2 position;
            layout(location = 1) in vec2 texCoord;

            out vec2 TexCoord;

            void main()
            {
                gl_Position = vec4(position, 0.0, 1.0);
                TexCoord = texCoord;
            }
        ";

        private const string FragmentShader = @"
            #version 410

            in vec2 TexCoord;

            out vec4 color;

            void main()
            {
                color = vec4(TexCoord.x, TexCoord.y, 0.0, 1.0);
            }
        ";

    }

}
