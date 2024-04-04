using Prowl.Icons;
using Prowl.Runtime.CSG;

namespace Prowl.Runtime.Components.Testings
{
    [AddComponentMenu($"{FontAwesome6.Dna}  Testing/{FontAwesome6.HillRockslide}  CSGTest")]
    public class CSGTest : MonoBehaviour
    {
        CSGBrush cube;
        CSGBrush sphere;
        CSGBrush cylinder;
        CSGBrush cube_inter_sphere;
        CSGBrush finalres;

        // Start is called before the first frame update
        public override void Start()
        {
            // Create the brush to contain the result of the operation cube_inter_sphere
            // You can give a name if you want a specifique name for the GameObject created
            cube_inter_sphere = new CSGBrush(new GameObject("cube_inter_sphere"));
            // Create the brush to contain the result of another operation 
            finalres = new CSGBrush(GameObject.Find("Result"));

        }
        float timer = 0;
        public override void Update()
        {
            timer += Time.deltaTimeF;
            if (timer > 1f)
            {
                timer = 0f;
                CreateBrush();
                CreateObjet();
            }
        }

        public void CreateBrush()
        {
            // Create the Brush for the cube
            cube = new CSGBrush(GameObject.Find("Cube"));
            // Set-up the mesh in the Brush
            cube.BuildFromMesh(GameObject.Find("Cube").GetComponent<MeshRenderer>().Mesh.Res);

            // Create the Brush for the cube
            sphere = new CSGBrush(GameObject.Find("Sphere"));
            // Set-up the mesh in the Brush
            sphere.BuildFromMesh(GameObject.Find("Sphere").GetComponent<MeshRenderer>().Mesh.Res);

            // Create the Brush for the cylinder
            cylinder = new CSGBrush(GameObject.Find("Cylinder"));
            // Set-up the mesh in the Brush
            cylinder.BuildFromMesh(GameObject.Find("Cylinder").GetComponent<MeshRenderer>().Mesh.Res);
        }

        public void CreateObjet()
        {

            // Do the operation intersection between the cube and the sphere 
            CSGUtility.MergeBrushes(ref cube_inter_sphere, CSGOperation.Intersection, cube, sphere);

            // Do the operation subtraction between the previous operation and the cylinder 
            CSGUtility.MergeBrushes(ref finalres, CSGOperation.Subtraction, cube, cylinder);

            if (finalres.faces.Length > 0)
            {
                GameObject.Find("Result").GetComponent<MeshRenderer>().Mesh.Res?.DestroyImmediate();
                Mesh m = new Mesh();
                finalres.GetMesh(m);
                GameObject.Find("Result").GetComponent<MeshRenderer>().Mesh = m;
            }
        }
    }

}