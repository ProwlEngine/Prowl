namespace AssimpSharp
{
    public class FlipWindingOrderProcess : BaseProcess
    {
        public override bool IsActive(int flags)
        {
            return ((AiPostProcessSteps)flags).HasFlag(AiPostProcessSteps.FlipWindingOrder);
        }

        public override void Execute(AiScene scene)
        {
            Console.WriteLine("FlipWindingOrderProcess begin");
            for (int i = 0; i < scene.NumMeshes; i++)
                ProcessMesh(scene.Meshes[i]);
            Console.WriteLine("FlipWindingOrderProcess finished");
        }

        private void ProcessMesh(AiMesh mesh)
        {
            for (int a = 0; a < mesh.NumFaces; a++)
            {
                var face = mesh.Faces[a];
                for (int b = 0; b < face.Count / 2; b++)
                {
                    var tmp = face[b];
                    face[b] = face[face.Count - 1 - b];
                    face[face.Count - 1 - b] = tmp;
                }
            }
        }
    }
}
