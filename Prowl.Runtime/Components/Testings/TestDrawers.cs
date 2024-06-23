using Prowl.Icons;
using System.Collections.Generic;

namespace Prowl.Runtime.Components.Testings;

[AddComponentMenu($"{FontAwesome6.Dna}  Testing/{FontAwesome6.Shapes}  TestDrawers")]
public class TestDrawers : MonoBehaviour
{
    // Test a bunch of differant Fields to ensure they all draw properly in editor
    public AssetRef<Material> aMat;
    public Color aColor;
    public float aFloat;
    public int aInt;
    public Vector2 aVec2;
    public Vector3 aVec3;
    public Vector4 aVec4;
    public bool aBool;
    public string aString;
    public MeshRenderer aMeshRenderer;
    public Camera aCamera;
    public Material aMaterial;
    public GameObject aGameObject;
    public Transform aTransform;

    public float[] aFloats = new float[2];

    // Struct
    public struct TestStruct
    {
        public int a;
        public float b;
        public string c;
    }
    public TestStruct aStruct;

    // Enum
    public enum TestEnum
    {
        A,
        B,
        C
    }
    public TestEnum aEnum;

    // List
    public List<int> aList = new();

    // Array
    public TestStruct[] aArray = new TestStruct[5];

    // Dictionary
    public Dictionary<string, int> aDictionary = new();

    // Nested
    public class Nested
    {
        public int a;
        public float b;
        public string c;
    }
    public Nested aNested = new();

    public LayerMask aLayerMask;
}
