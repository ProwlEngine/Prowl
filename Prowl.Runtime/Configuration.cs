using System.Drawing;

namespace Prowl.Runtime; 

//TODO: create and deserialize configuration file when loading
public static class Configuration {
    
    public static float FixedTimeStep = 0.02f;
    
    public static string WindowTitle = "Window Title";
    
    public static Color DefaultBackgroundColor = Color.gray;
    
    public static bool DoDebugLogs = true;
    public static bool DoDebugWarnings = true;
    public static bool DoDebugErrors = true;
    public static bool DoDebugSuccess = true;
    
}
