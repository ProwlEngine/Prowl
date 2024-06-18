using Prowl.Runtime;
using Prowl.Runtime.SceneManagement;
using Prowl.Runtime.Utils;
using Veldrid;
using System.Text;

namespace Prowl.Test;

internal static class Program
{
    static DirectoryInfo Data => new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);

    static double secondCounter;
    static int temp;
    static Transform camTrs = new Transform();


    public static int Main(string[] args)
    {

        Application.isPlaying = true;
        Application.DataPath = Data.FullName;

        Application.Initialize += () =>
        {
            Graphics.VSync = false;
            Input.SetCursorVisible(false);
            Input.LockCursor(true);

            camTrs.position = new Vector3(0, 1, -10);
            camTrs.LookAt(Vector3.zero, Vector3.up);

            Graphics.SetCamera(camTrs, 1.0f);
        };

        Application.Update += () =>
        {
            if (secondCounter <= 1) 
            {
                secondCounter += Time.deltaTime;
                temp++;
            }
            else 
            {
                Console.WriteLine($"FPS: {temp}");
                secondCounter = 0;
                temp = 0;
            }

            double lr = (Input.GetKey(Veldrid.Key.A) ? -1.0 : 0.0) + (Input.GetKey(Veldrid.Key.D) ? 1.0 : 0.0);
            double fw = (Input.GetKey(Veldrid.Key.S) ? 1.0 : 0.0) + (Input.GetKey(Veldrid.Key.W) ? -1.0 : 0.0);

            camTrs.eulerAngles = new Vector3(camTrs.eulerAngles.x - Input.MouseDelta.y / 50.0f, camTrs.eulerAngles.y - Input.MouseDelta.x / 50.0f, 0.0f);

            Vector3 pos = camTrs.position;

            pos += camTrs.right * lr * Time.deltaTime;
            pos += camTrs.forward * fw * Time.deltaTime;

            camTrs.position = pos;
        };

        Application.Render += () =>
        {
            Graphics.StartFrame();

            Gizmos.Color = Color.white;
            Gizmos.DrawLine(Vector3.zero, new Vector3(MathD.Sin(Time.time), MathD.Cos(Time.time), 0.0));

            Gizmos.Matrix = Matrix4x4.CreateRotationX(90 * MathD.Deg2Rad);
            Gizmos.DrawArc(Vector3.zero, 1.0f, 0.0f, 360.0f);

            Gizmos.Color = Color.red;
            Gizmos.DrawCapsule(new Vector3(-2.0, 0.0, 0.0), 0.5f, 1f);

            Gizmos.Color = Color.green;
            Gizmos.DrawCylinder(new Vector3(2.0, 0.0, 0.0), 0.5f, 2.0f);

            Gizmos.Color = Color.blue;
            Gizmos.DrawSphere(new Vector3(0.0, -2.0, 0.0), 0.5f);

            Gizmos.Color = Color.magenta;
            Gizmos.DrawCube(new Vector3(0.0, 2.0, 0.0), Vector3.one);

            Gizmos.Render();

            Graphics.EndFrame();
        };

        Application.Quitting += () =>
        {

        };

        Application.Run("Prowl Test", 1920, 1080, null, false);

        return 0;
    }

}
