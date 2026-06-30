using Prowl.Drift;
using SFML.Graphics;
using SFML.System;
using System.Numerics;

namespace DriftDemo
{
    public class Renderer
    {
        private RenderWindow _window;
        private readonly float _pixelsPerMeter = 50f;
        private readonly Vector2f _center;

        public Renderer(RenderWindow window)
        {
            _window = window;
            _center = new Vector2f(window.Size.X / 2f, window.Size.Y / 2f);
        }

        private Color GetColor(int bodyId)
        {
            Random rand = new Random(bodyId);

            int r = (rand.Next(200) + 55);
            int g = (rand.Next(200) + 55);
            int b = (rand.Next(200) + 55);

            return new Color((byte)r, (byte)g, (byte)b);
        }

        private Vector2f WorldToScreen(Vector2 worldPos)
        {
            return new Vector2f(
                _center.X + worldPos.X * _pixelsPerMeter,
                _center.Y + _window.Size.Y * 0.5f - worldPos.Y * _pixelsPerMeter  // Flip Y axis and offset down
            );
        }

        public void DrawSpace(Space space)
        {
            foreach (var body in space.Bodies)
            {
                DrawBody(body);
            }

            // draw contacts
            //foreach (var contactSolver in space.Contacts)
            //{
            //    foreach (var contact in contactSolver.Contacts)
            //    {
            //        var pos = WorldToScreen(contact.Position);
            //        var nor = WorldToScreen(contact.NormalTowardTwo);
            //        var depth = contact.Depth * _pixelsPerMeter;
            //
            //        var circle = new CircleShape(3)
            //        {
            //            Position = new Vector2f(pos.X - 3, pos.Y - 3),
            //            FillColor = Color.Red
            //        };
            //
            //        _window.Draw(circle);
            //
            //        var line = new Vertex[]
            //        {
            //            new Vertex(pos, Color.Yellow),
            //            new Vertex(new Vector2f(pos.X + contact.NormalTowardTwo.X * depth, pos.Y - contact.NormalTowardTwo.Y * depth), Color.Yellow)
            //        };
            //        _window.Draw(line, PrimitiveType.Lines);
            //    }
            //}

            //// Draw Bounds
            //foreach (var body in space.Bodies)
            //{
            //    if (body == null) continue;
            //    var b = body.Bounds;
            //    var topLeft = WorldToScreen(new Vector2(b.Mins.X, b.Maxs.Y));
            //    var size = new Vector2f((b.Maxs.X - b.Mins.X) * _pixelsPerMeter, (b.Maxs.Y - b.Mins.Y) * _pixelsPerMeter);
            //    var rect = new RectangleShape(size)
            //    {
            //        Position = topLeft,
            //        FillColor = Color.Transparent,
            //        OutlineColor = Color.Blue,
            //        OutlineThickness = 1
            //    };
            //    _window.Draw(rect);
            //}

            // Draw joints with just lines
            foreach (var joint in space.Joints)
            {
                if (joint == null) continue;
                var a = WorldToScreen(joint.GetWorldAnchor1());
                var b = WorldToScreen(joint.GetWorldAnchor2());
                var line = new Vertex[]
                {
                    new Vertex(a, Color.Magenta),
                    new Vertex(b, Color.Magenta)
                };
                _window.Draw(line, PrimitiveType.Lines);
            }
        }

        private void DrawBody(Body body)
        {
            Color color;

            if (body.Type == Body.BodyType.Static)
            {
                color = new Color(150, 150, 150);
            }
            else
            {
                color = GetColor(body.Id);
            }

            foreach (var shape in body.Shapes)
            {
                DrawShape(shape, color, true);
            }
        }

        private void DrawShape(Prowl.Drift.Shape shape, Color color, bool isAwake)
        {
            switch (shape)
            {
                case ShapeCircle circle:
                    DrawCircle(circle, color, isAwake);
                    break;
                case ShapePoly poly:
                    DrawPoly(poly, color, isAwake);
                    break;
                case ShapeSegment segment:
                    DrawSegment(segment, color, isAwake);
                    break;
            }
        }

        static CircleShape? circleShape;

        private void DrawCircle(ShapeCircle circle, Color color, bool isAwake)
        {
            var center = WorldToScreen(circle.TransformedCenter);
            var radius = circle.Radius * _pixelsPerMeter;

            if (circleShape == null)
            {
                circleShape = new CircleShape();
                //circleShape.SetPointCount(12);
            }
            if(radius != circleShape.Radius)
                circleShape.Radius = radius;
            circleShape.Position = new Vector2f(center.X - radius, center.Y - radius);
            circleShape.FillColor = color;

            _window.Draw(circleShape);
        }

        private void DrawPoly(ShapePoly poly, Color color, bool isAwake)
        {
            if (poly.TransformedVerts.Count < 3) return;

            var shape = new ConvexShape((uint)poly.TransformedVerts.Count);
            for (int i = 0; i < poly.TransformedVerts.Count; i++)
            {
                var screenPos = WorldToScreen(poly.TransformedVerts[i]);
                shape.SetPoint((uint)i, screenPos);
            }

            shape.FillColor = color;

            _window.Draw(shape);

            // draw plane normals
            /*
            for (int i = 0; i < poly.TransformedVerts.Count; i++)
            {
                var a = poly.TransformedVerts[i];
                var b = poly.TransformedVerts[(i + 1) % poly.TransformedVerts.Count];
                var mid = (a + b) * 0.5f;
                var normalEnd = mid + poly.TransformedPlanes[i].Normal * 0.25f; // Scale normal for visibility
                DrawLine(mid, normalEnd, Color.Red);
            }
            */
        }

        private void DrawLine(Vector2 start, Vector2 end, Color color)
        {
            var a = WorldToScreen(start);
            var b = WorldToScreen(end);
            var line = new Vertex[]
            {
                new Vertex(a, color),
                new Vertex(b, color)
            };
            _window.Draw(line, PrimitiveType.Lines);
        }

        public void DrawRay(Ray ray, Color color)
        {
            var end = ray.GetPoint(ray.MaxDistance);
            DrawLine(ray.Origin, end, color);
        }

        public void DrawRaycastHit(RaycastHit hit, Color color)
        {
            if (!hit.Hit) return;

            // Draw the ray line up to hit point
            DrawLine(hit.Point - hit.Normal * 0.1f, hit.Point, color);

            // Draw hit point
            var hitPoint = WorldToScreen(hit.Point);
            var hitCircle = new CircleShape(4)
            {
                Position = new Vector2f(hitPoint.X - 4, hitPoint.Y - 4),
                FillColor = color
            };
            _window.Draw(hitCircle);

            // Draw normal
            var normalEnd = hit.Point + hit.Normal * 0.5f;
            DrawLine(hit.Point, normalEnd, color);
        }

        private void DrawSegment(ShapeSegment segment, Color color, bool isAwake)
        {
            if (segment.Radius > 0)
            {
                var radius = segment.Radius * _pixelsPerMeter;
                var a = WorldToScreen(segment.TransformedA);
                var b = WorldToScreen(segment.TransformedB);

                // Draw filled circles at endpoints
                var circleA = new CircleShape(radius)
                {
                    Position = new Vector2f(a.X - radius, a.Y - radius),
                    FillColor = color,
                };
                var circleB = new CircleShape(radius)
                {
                    Position = new Vector2f(b.X - radius, b.Y - radius),
                    FillColor = color,
                };
                _window.Draw(circleA);
                _window.Draw(circleB);

                // Draw filled rectangle between the circles to complete the capsule
                var direction = new Vector2f(b.X - a.X, b.Y - a.Y);
                var length = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
                if (length > 0)
                {
                    direction.X /= length;
                    direction.Y /= length;

                    var perpendicular = new Vector2f(-direction.Y, direction.X);
                    var p1 = new Vector2f(a.X + perpendicular.X * radius, a.Y + perpendicular.Y * radius);
                    var p2 = new Vector2f(a.X - perpendicular.X * radius, a.Y - perpendicular.Y * radius);
                    var p3 = new Vector2f(b.X - perpendicular.X * radius, b.Y - perpendicular.Y * radius);
                    var p4 = new Vector2f(b.X + perpendicular.X * radius, b.Y + perpendicular.Y * radius);

                    var quad = new Vertex[]
                    {
                        new Vertex(p1, color),
                        new Vertex(p2, color),
                        new Vertex(p3, color),
                        new Vertex(p4, color)
                    };
                    _window.Draw(quad, PrimitiveType.Quads);
                }
            }
            else
            {
                // For thin segments, draw as a thick line
                var a = WorldToScreen(segment.TransformedA);
                var b = WorldToScreen(segment.TransformedB);
                var thickLine = new Vertex[]
                {
                    new Vertex(a, color),
                    new Vertex(b, color)
                };
                _window.Draw(thickLine, PrimitiveType.Lines);
            }
        }
    }
}