using Prowl.Runtime;
using Prowl.Runtime.SceneManagement;
using Prowl.Runtime.Utils;
using Veldrid;
using System.Text;

namespace Prowl.Test;

internal static class Program
{
    static DirectoryInfo Data => new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);


    static Texture2D sampleImage1;
    static Texture2D sampleImage2;

    static double secondCounter;
    static int temp;
    static Transform camTrs = new Transform();


    public static int Main(string[] args)
    {

        Application.isPlaying = true;
        Application.DataPath = Data.FullName;

        Application.Initialize += () =>
        {
            Graphics.VSync = true;
            Input.SetCursorVisible(false);
            Input.LockCursor(true);

            camTrs.position = new Vector3(0, 1, -10);
            camTrs.LookAt(Vector3.zero, Vector3.up);

            Graphics.SetCamera(camTrs, 1.0f);

            sampleImage1 = Texture2DLoader.FromFile("Sample1.png");
            sampleImage2 = Texture2DLoader.FromFile("Sample2.jpg");

            Graphics.onGUI += OnGUI;
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

            Gizmos.Render();

            Graphics.EndFrame();
        };

        Application.Quitting += () =>
        {

        };

        Application.Run("Prowl Test", 1920, 1080, null, false);

        return 0;
    }


    private static void OnGUI(Prowl.Runtime.GUI.Gui gui)
    {
        Rect pos = new Rect(Screen.Size.x / 2 - 250, Screen.Size.y / 2 - 300, 500, 600);

        gui.Draw2D.DrawRectFilled(pos, Color.green, 5, 5);

        pos.height = 100;

        gui.Draw2D.DrawText("Pollo 👍", 150, pos, Color.white, doclip:false);

        pos.x += 25;
        pos.y += 125;
        pos.height = 450;
        pos.width = 450;

        gui.Draw2D.DrawImage(sampleImage2, pos, Color.white);
    }

}
