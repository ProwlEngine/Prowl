using System.Numerics;

namespace AssimpSharp
{
    public class AiCamera
    {
        public string Name { get; set; } = "";
        public Vector3 Position { get; set; } = Vector3.Zero;
        public Vector3 Up { get; set; } = new Vector3(0, 1, 0);
        public Vector3 LookAt { get; set; } = new Vector3(0, 0, 1);
        public float HorizontalFOV { get; set; } = 0.25f * MathF.PI;
        public float ClipPlaneNear { get; set; } = 0.1f;
        public float ClipPlaneFar { get; set; } = 1000f;
        public float Aspect { get; set; } = 0f;

        public void GetCameraMatrix(out Matrix4x4 matrix)
        {
            // Implementation omitted for brevity
            matrix = Matrix4x4.Identity;
        }
    }
}