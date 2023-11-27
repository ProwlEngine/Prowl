using Prowl.Runtime.Utils;
using System;

namespace Prowl.Runtime.Resources
{
    [CreateAssetMenu("RenderPipeline")]
    public class RenderPipeline : ScriptableObject
    {
        [Text("This Script is a text for ScriptableObjects")]
        [Tooltip("This field is clamped [0-10] in OnValidate")]
        public float testingFloat = 9f;

        [Seperator()]
        [Tooltip("This field does nothing :D")]
        public bool ThisScriptIsATest = true;

        public override void OnValidate()
        {
            testingFloat = Math.Clamp(testingFloat, 0f, 10f);
        }

        [ImGUIButton("ToggleTest")]
        public void Toggle()
        {
            ThisScriptIsATest = !ThisScriptIsATest;
        }
    }
}
