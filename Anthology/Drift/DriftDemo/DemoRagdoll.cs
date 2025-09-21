using Physics2D;
using Drift.Joints;

namespace DriftDemo
{
    public class DemoRagdoll : IDemo
    {
        public string Name => "Ragdoll";

        private Space? _space;

        public void Init(Space space)
        {
            _space = space;

            // Create boundaries
            var staticBody = new Body(Body.BodyType.Static, Vec2.Zero);
            staticBody.AddShape(ShapePoly.CreateBox(0, 0.2f, 20.48f, 0.4f));  // Floor
            staticBody.AddShape(ShapePoly.CreateBox(0, 15.16f, 20.48f, 0.4f)); // Ceiling
            staticBody.AddShape(ShapePoly.CreateBox(-10.04f, 7.68f, 0.4f, 14.56f)); // Left wall
            staticBody.AddShape(ShapePoly.CreateBox(10.04f, 7.68f, 0.4f, 14.56f));  // Right wall
            staticBody.ResetMassData();
            space.AddBody(staticBody);

            // Head
            var bodyHead = new Body(Body.BodyType.Dynamic, new Vec2(0, 7.4f));
            var headShape = new ShapeCircle(0, 0, 0.45f);
            headShape.Elasticity = 0.1f;
            headShape.Friction = 0.8f;
            headShape.Density = 5;
            bodyHead.AddShape(headShape);
            bodyHead.ResetMassData();
            space.AddBody(bodyHead);

            // Spine1 (upper torso)
            var bodySpine1 = new Body(Body.BodyType.Dynamic, new Vec2(0, 6.4f));
            var spine1Verts = new Vec2[]
            {
                new Vec2(-0.5f, 0.35f),
                new Vec2(-0.9f, 0.2f),
                new Vec2(-0.4f, -0.8f),
                new Vec2(0.4f, -0.8f),
                new Vec2(0.9f, 0.2f),
                new Vec2(0.5f, 0.35f)
            };
            var spine1Shape = new ShapePoly(spine1Verts);
            spine1Shape.Elasticity = 0.1f;
            spine1Shape.Friction = 0.8f;
            spine1Shape.Density = 4;
            bodySpine1.AddShape(spine1Shape);
            bodySpine1.ResetMassData();
            space.AddBody(bodySpine1);

            // Spine2 (lower torso)
            var bodySpine2 = new Body(Body.BodyType.Dynamic, new Vec2(0, 5.6f));
            var spine2Verts = new Vec2[]
            {
                new Vec2(-0.6f, 0.4f),
                new Vec2(-0.55f, -0.6f),
                new Vec2(0.55f, -0.6f),
                new Vec2(0.6f, 0.4f)
            };
            var spine2Shape = new ShapePoly(spine2Verts);
            spine2Shape.Elasticity = 0.1f;
            spine2Shape.Friction = 0.8f;
            spine2Shape.Density = 4;
            bodySpine2.AddShape(spine2Shape);
            bodySpine2.ResetMassData();
            space.AddBody(bodySpine2);

            // Pelvis
            var bodyPelvis = new Body(Body.BodyType.Dynamic, new Vec2(0, 4.85f));
            var pelvisVerts = new Vec2[]
            {
                new Vec2(-0.55f, 0.4f),
                new Vec2(-0.69f, -0.4f),
                new Vec2(-0.4f, -0.6f),
                new Vec2(0.4f, -0.6f),
                new Vec2(0.69f, -0.4f),
                new Vec2(0.55f, 0.4f)
            };
            var pelvisShape = new ShapePoly(pelvisVerts);
            pelvisShape.Elasticity = 0.1f;
            pelvisShape.Friction = 0.8f;
            pelvisShape.Density = 4;
            bodyPelvis.AddShape(pelvisShape);
            bodyPelvis.ResetMassData();
            space.AddBody(bodyPelvis);

            // Left Arm1 (upper arm)
            var bodyLArm1 = new Body(Body.BodyType.Dynamic, new Vec2(-1.25f, 6.5f));
            var larm1Shape = ShapePoly.CreateBox(0, 0, 1.3f, 0.35f);
            larm1Shape.Elasticity = 0.1f;
            larm1Shape.Friction = 0.8f;
            larm1Shape.Density = 3;
            bodyLArm1.AddShape(larm1Shape);
            bodyLArm1.ResetMassData();
            space.AddBody(bodyLArm1);

            // Left Arm2 (forearm)
            var bodyLArm2 = new Body(Body.BodyType.Dynamic, new Vec2(-2.4f, 6.5f));
            var larm2Shape = ShapePoly.CreateBox(0, 0, 1.4f, 0.35f);
            larm2Shape.Elasticity = 0.1f;
            larm2Shape.Friction = 0.8f;
            larm2Shape.Density = 3;
            bodyLArm2.AddShape(larm2Shape);
            bodyLArm2.ResetMassData();
            space.AddBody(bodyLArm2);

            // Right Arm1 (upper arm)
            var bodyRArm1 = new Body(Body.BodyType.Dynamic, new Vec2(1.25f, 6.5f));
            var rarm1Shape = ShapePoly.CreateBox(0, 0, 1.3f, 0.35f);
            rarm1Shape.Elasticity = 0.1f;
            rarm1Shape.Friction = 0.8f;
            rarm1Shape.Density = 3;
            bodyRArm1.AddShape(rarm1Shape);
            bodyRArm1.ResetMassData();
            space.AddBody(bodyRArm1);

            // Right Arm2 (forearm)
            var bodyRArm2 = new Body(Body.BodyType.Dynamic, new Vec2(2.4f, 6.5f));
            var rarm2Shape = ShapePoly.CreateBox(0, 0, 1.4f, 0.35f);
            rarm2Shape.Elasticity = 0.1f;
            rarm2Shape.Friction = 0.8f;
            rarm2Shape.Density = 3;
            bodyRArm2.AddShape(rarm2Shape);
            bodyRArm2.ResetMassData();
            space.AddBody(bodyRArm2);

            // Left Leg1 (thigh)
            var bodyLLeg1 = new Body(Body.BodyType.Dynamic, new Vec2(-0.42f, 3.6f));
            var lleg1Shape = ShapePoly.CreateBox(0, 0, 0.5f, 2);
            lleg1Shape.Elasticity = 0.1f;
            lleg1Shape.Friction = 0.8f;
            lleg1Shape.Density = 3;
            bodyLLeg1.AddShape(lleg1Shape);
            bodyLLeg1.ResetMassData();
            space.AddBody(bodyLLeg1);

            // Left Leg2 (shin)
            var bodyLLeg2 = new Body(Body.BodyType.Dynamic, new Vec2(-0.42f, 1.9f));
            var lleg2Shape = ShapePoly.CreateBox(0, 0, 0.5f, 2);
            lleg2Shape.Elasticity = 0.1f;
            lleg2Shape.Friction = 0.8f;
            lleg2Shape.Density = 3;
            bodyLLeg2.AddShape(lleg2Shape);
            bodyLLeg2.ResetMassData();
            space.AddBody(bodyLLeg2);

            // Right Leg1 (thigh)
            var bodyRLeg1 = new Body(Body.BodyType.Dynamic, new Vec2(0.42f, 3.6f));
            var rleg1Shape = ShapePoly.CreateBox(0, 0, 0.5f, 2);
            rleg1Shape.Elasticity = 0.1f;
            rleg1Shape.Friction = 0.8f;
            rleg1Shape.Density = 3;
            bodyRLeg1.AddShape(rleg1Shape);
            bodyRLeg1.ResetMassData();
            space.AddBody(bodyRLeg1);

            // Right Leg2 (shin)
            var bodyRLeg2 = new Body(Body.BodyType.Dynamic, new Vec2(0.42f, 1.9f));
            var rleg2Shape = ShapePoly.CreateBox(0, 0, 0.5f, 2);
            rleg2Shape.Elasticity = 0.1f;
            rleg2Shape.Friction = 0.8f;
            rleg2Shape.Density = 3;
            bodyRLeg2.AddShape(rleg2Shape);
            bodyRLeg2.ResetMassData();
            space.AddBody(bodyRLeg2);

            // Create joints with limits

            // Head to Spine1 (neck)
            var neckJoint = new RevoluteJoint(bodyHead, bodySpine1, new Vec2(0, 6.8f));
            neckJoint.EnableLimit(true);
            neckJoint.SetLimits(-40 * MathUtil.Deg2Rad, 40 * MathUtil.Deg2Rad);
            space.AddJoint(neckJoint);

            // Spine1 to Spine2
            var spine12Joint = new RevoluteJoint(bodySpine1, bodySpine2, new Vec2(0, 5.8f));
            spine12Joint.CollideConnected = false;
            spine12Joint.EnableLimit(true);
            spine12Joint.SetLimits(-5 * MathUtil.Deg2Rad, 5 * MathUtil.Deg2Rad);
            space.AddJoint(spine12Joint);

            // Spine2 to Pelvis
            var spine2PelvisJoint = new RevoluteJoint(bodySpine2, bodyPelvis, new Vec2(0, 5.1f));
            spine2PelvisJoint.CollideConnected = false;
            spine2PelvisJoint.EnableLimit(true);
            spine2PelvisJoint.SetLimits(-20 * MathUtil.Deg2Rad, 20 * MathUtil.Deg2Rad);
            space.AddJoint(spine2PelvisJoint);

            // Left shoulder
            var lShoulderJoint = new RevoluteJoint(bodySpine1, bodyLArm1, new Vec2(-0.75f, 6.5f));
            lShoulderJoint.EnableLimit(true);
            lShoulderJoint.SetLimits(-100 * MathUtil.Deg2Rad, 100 * MathUtil.Deg2Rad);
            space.AddJoint(lShoulderJoint);

            // Left elbow
            var lElbowJoint = new RevoluteJoint(bodyLArm1, bodyLArm2, new Vec2(-1.8f, 6.5f));
            lElbowJoint.CollideConnected = false;
            lElbowJoint.EnableLimit(true);
            lElbowJoint.SetLimits(-170 * MathUtil.Deg2Rad, 10 * MathUtil.Deg2Rad);
            space.AddJoint(lElbowJoint);

            // Right shoulder
            var rShoulderJoint = new RevoluteJoint(bodySpine1, bodyRArm1, new Vec2(0.75f, 6.5f));
            rShoulderJoint.EnableLimit(true);
            rShoulderJoint.SetLimits(-100 * MathUtil.Deg2Rad, 100 * MathUtil.Deg2Rad);
            space.AddJoint(rShoulderJoint);

            // Right elbow
            var rElbowJoint = new RevoluteJoint(bodyRArm1, bodyRArm2, new Vec2(1.8f, 6.5f));
            rElbowJoint.CollideConnected = false;
            rElbowJoint.EnableLimit(true);
            rElbowJoint.SetLimits(-10 * MathUtil.Deg2Rad, 170 * MathUtil.Deg2Rad);
            space.AddJoint(rElbowJoint);

            // Left hip
            var lHipJoint = new RevoluteJoint(bodyPelvis, bodyLLeg1, new Vec2(-0.42f, 4.4f));
            lHipJoint.CollideConnected = false;
            lHipJoint.EnableLimit(true);
            lHipJoint.SetLimits(-100 * MathUtil.Deg2Rad, 50 * MathUtil.Deg2Rad);
            space.AddJoint(lHipJoint);

            // Left knee
            var lKneeJoint = new RevoluteJoint(bodyLLeg1, bodyLLeg2, new Vec2(-0.42f, 2.75f));
            lKneeJoint.CollideConnected = false;
            lKneeJoint.EnableLimit(true);
            lKneeJoint.SetLimits(-15 * MathUtil.Deg2Rad, 150 * MathUtil.Deg2Rad);
            space.AddJoint(lKneeJoint);

            // Right hip
            var rHipJoint = new RevoluteJoint(bodyPelvis, bodyRLeg1, new Vec2(0.42f, 4.4f));
            rHipJoint.CollideConnected = false;
            rHipJoint.EnableLimit(true);
            rHipJoint.SetLimits(-50 * MathUtil.Deg2Rad, 100 * MathUtil.Deg2Rad);
            space.AddJoint(rHipJoint);

            // Right knee
            var rKneeJoint = new RevoluteJoint(bodyRLeg1, bodyRLeg2, new Vec2(0.42f, 2.75f));
            rKneeJoint.CollideConnected = false;
            rKneeJoint.EnableLimit(true);
            rKneeJoint.SetLimits(-150 * MathUtil.Deg2Rad, 15 * MathUtil.Deg2Rad);
            space.AddJoint(rKneeJoint);

            // Give the ragdoll an initial push
            bodyHead.ApplyLinearImpulse(new Vec2(120, 0), new Vec2(0, 7.34f));
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