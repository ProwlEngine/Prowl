using System.Numerics;

namespace AssimpSharp
{
    public class FlipUVsProcess : BaseProcess
    {
        public override bool IsActive(int flags)
        {
            return ((AiPostProcessSteps)flags).HasFlag(AiPostProcessSteps.FlipUVs);
        }

        public override void Execute(AiScene scene)
        {
            Console.WriteLine("FlipUVsProcess begin");
            for (int i = 0; i < scene.NumMeshes; i++)
                ProcessMesh(scene.Meshes[i]);

            for (int i = 0; i < scene.NumMaterials; i++)
                ProcessMaterial(scene.Materials[i]);
            Console.WriteLine("FlipUVsProcess finished");
        }

        private void ProcessMesh(AiMesh mesh)
        {
            for (int a = 0; a < Constants.AI_MAX_NUMBER_OF_TEXTURECOORDS; a++)
            {
                if (!mesh.HasTextureCoords(a))
                    break;

                for (int b = 0; b < mesh.NumVertices; b++)
                {
                    mesh.TextureCoordinateChannels[a][b][1] = 1f - mesh.TextureCoordinateChannels[a][b][1];
                }
            }
        }

        private void ProcessMaterial(AiMaterial material)
        {
            foreach (var texture in material.Textures)
            {
                if (texture.UVTrafo.HasValue)
                {
                    var transform = texture.UVTrafo.Value;
                    transform.Translation = new Vector2(transform.Translation.X, -transform.Translation.Y);
                    transform.Rotation = -transform.Rotation;
                    texture.UVTrafo = transform;
                }
            }
        }
    }
}
