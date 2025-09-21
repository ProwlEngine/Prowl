using Prowl.Drift;
using System.Numerics;

namespace DriftDemo
{
    public class DemoSeesaw : IDemo
    {
        public string Name => "Seesaw";

        private Space? _space;

        public void Init(Space space)
        {
            _space = space;

            // Create static body for boundaries
            var staticBody = new Body(Body.BodyType.Static, Vector2.Zero);
            staticBody.AddShape(ShapePoly.CreateBox(0, 0.2f, 20.48f, 0.4f));  // Ground
            staticBody.AddShape(ShapePoly.CreateBox(-10.04f, 7.68f, 0.4f, 14.56f));  // Left wall
            staticBody.AddShape(ShapePoly.CreateBox(10.04f, 7.68f, 0.4f, 14.56f));   // Right wall
            space.AddBody(staticBody);

            // Create support block for seesaw
            var supportBody = new Body(Body.BodyType.Dynamic, new Vector2(-3, 1.6f));
            var supportShape = ShapePoly.CreateBox(0, 0, 2.8f, 1.6f);
            supportShape.Elasticity = 0.1f;
            supportShape.Friction = 1.0f;
            supportShape.Density = 2;
            supportBody.AddShape(supportShape);
            space.AddBody(supportBody);

            // Create seesaw plank
            var seesawBody = new Body(Body.BodyType.Dynamic, new Vector2(0, 2.8f));
            var seesawShape = ShapePoly.CreateBox(0, 0, 12, 0.2f);
            seesawShape.Elasticity = 0.4f;
            seesawShape.Friction = 0.7f;
            seesawShape.Density = 1.2f;
            seesawBody.AddShape(seesawShape);
            space.AddBody(seesawBody);

            // Create stack of boxes on left side
            for (int i = 0; i < 5; i++)
            {
                for (int j = 0; j <= i; j++)
                {
                    var body = new Body(Body.BodyType.Dynamic, new Vector2((j - i * 0.5f) * 0.88f - 3, 7 - i * 0.88f));
                    var shape = ShapePoly.CreateBox(0, 0, 0.8f, 0.8f);
                    shape.Elasticity = 0.3f;
                    shape.Friction = 0.8f;
                    shape.Density = 1;
                    body.AddShape(shape);
                    space.AddBody(body);
                }
            }

            // Create large pentagon shape that will fall from above
            var fallBody = new Body(Body.BodyType.Dynamic, new Vector2(5, 30));
            var fallVerts = new Vector2[]
            {
                new Vector2(-0.96f, 0.7f),
                new Vector2(-0.76f, 0),
                new Vector2(0.76f, 0),
                new Vector2(0.96f, 0.7f),
                new Vector2(0, 1.48f)
            };
            var fallShape = new ShapePoly(fallVerts);
            fallShape.Elasticity = 0.4f;
            fallShape.Friction = 1.0f;
            fallShape.Density = 2;
            fallBody.AddShape(fallShape);
            space.AddBody(fallBody);
        }

        public void RunFrame()
        {
            // Nothing special needed per frame for this demo
        }

        public void KeyDown(char key)
        {
            // No special key handling for this demo
        }
    }
}