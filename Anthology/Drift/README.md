![Github top languages](https://img.shields.io/github/languages/top/prowlengine/prowl.drift)
[![GitHub version](https://img.shields.io/github/v/release/prowlengine/prowl.drift?include_prereleases&style=flat-square)](https://github.com/prowlengine/prowl.drift/releases)
[![GitHub license](https://img.shields.io/github/license/prowlengine/prowl.drift?style=flat-square)](https://github.com/prowlengine/prowl.drift/blob/main/LICENSE.txt)
[![GitHub issues](https://img.shields.io/github/issues/prowlengine/prowl.drift?style=flat-square)](https://github.com/prowlengine/prowl.drift/issues)
[![GitHub stars](https://img.shields.io/github/stars/prowlengine/prowl.drift?style=flat-square)](https://github.com/prowlengine/prowl.drift/stargazers)
[![Discord](https://img.shields.io/discord/1151582593519722668?logo=discord
)](https://discord.gg/BqnJ9Rn4sn)

<span id="readme-top"></span>

## <p>Table of Contents</p>
1. [About The Project](#-about-the-project-)
2. [Features](#-features-)
3. [Getting Started](#-getting-started-)
   * [Installation](#installation)
   * [Basic Usage](#basic-usage)
   * [Creating Bodies](#creating-bodies)
   * [Shapes and Collision](#shapes-and-collision)
   * [Joints and Constraints](#joints-and-constraints)
4. [Contributing](#-contributing-)
5. [Contributors](#contributors-)
7. [License](#-license-)

# <span align="center">🎱 About The Project 🎱

Prowl.Drift is an open-source, **[MIT-licensed](#span-aligncenter-license-span)** 2D physics engine built for the Prowl Game Engine. It provides a lightweight, high-performance physics simulation with support for rigid body dynamics, collision detection, and constraint solving.

Prowl.Drift follows a simple and intuitive API design, making it easy to integrate physics into your games and applications. Whether you're building a platformer, a physics puzzle game, or need realistic object interactions, Prowl.Drift has you covered.

Here's a basic example:
```cs
// Create a physics space
var space = new Space();

// Create a dynamic body with a box shape
var body = new Body(Body.BodyType.Dynamic, new Vector2(100, 50));
var boxShape = ShapePoly.CreateBox(0, 0, 32, 32);
body.AddShape(boxShape);
space.AddBody(body);

// Apply a force
body.ApplyForceToCenter(new Vector2(0, -500));

// Step the simulation (with solver iterations)
space.Step(1f / 60f, 8, 3, true); // dt, velocity iterations, position iterations, warm starting
```

# <span align="center">✨ Features ✨</span>

-   **General:**
    - Cross-Platform! Windows, Linux & Mac!
    - 100% C#!
    - Built on System.Numerics for performance
    - Supports .NET 6, 7, 8, and 9
    - Highly Portable
        - Very easy to integrate
    - Lightweight and fast

-   **Physics Features:**
    - Rigid Body Dynamics
        - Static, Kinematic, and Dynamic bodies
        - Mass, velocity, and angular velocity
        - Force and torque accumulation
        - Linear and angular damping
        - Fixed rotation support
    - Shape Support
        - Circles
        - Polygons (convex)
        - Line segments (With Rounded Thickness)
        - Multiple shapes per body (Compound)
    - Collision Detection
        - Broad-phase spatial hashing (Very Simple)
        - Narrow-phase SAT (Separating Axis Theorem)
        - Contact manifold generation
    - Constraint Solving
    - Joint System
        - Distance joints
        - Revolute joints (pin joints)
        - Prismatic joints (slider joints)
        - Weld joints
        - Wheel joints
        - Rope joints
        - Angle joints
        - Mouse joints (for dragging)

<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <span align="center">🚀 Getting Started 🚀</span>

## Installation
```
dotnet add package Prowl.Drift
```

## Basic Usage
Prowl.Drift is designed to be simple to use. Create a space, add bodies with shapes, and step the simulation.

```cs
using Prowl.Drift;
using System.Numerics;

// Create a physics space
var space = new Space();
space.Gravity = new Vector2(0, -10); // Set gravity

// Create the ground (static body)
var ground = new Body(Body.BodyType.Static, new Vector2(0, 400));
var groundShape = ShapePoly.CreateBox(0, 0, 800, 20);
ground.AddShape(groundShape);
space.AddBody(ground);

// Create a falling box (dynamic body)
var box = new Body(Body.BodyType.Dynamic, new Vector2(100, 50));
var boxShape = ShapePoly.CreateBox(0, 0, 32, 32);
box.AddShape(boxShape);
space.AddBody(box);

// Main game loop
while (gameRunning)
{
    // Step the physics simulation (60 FPS)
    space.Step(1f / 60f, 8, 3, true);

    // Use body.Position and body.Angle for rendering
    RenderBox(box.Position, box.Angle);
}
```

## Creating Bodies
Bodies are the fundamental objects in the physics simulation. They can be static (immovable), kinematic (movable but not affected by forces), or dynamic (fully simulated).

```cs
// Static body (walls, ground, platforms)
var staticBody = new Body(Body.BodyType.Static, position);
space.AddBody(staticBody);

// Kinematic body (moving platforms, elevators)
var kinematicBody = new Body(Body.BodyType.Kinematic, position);
kinematicBody.LinearVelocity = new Vector2(50, 0); // Moves at constant velocity
space.AddBody(kinematicBody);

// Dynamic body (players, projectiles, physics objects)
var dynamicBody = new Body(Body.BodyType.Dynamic, position);
space.AddBody(dynamicBody);
dynamicBody.ApplyForceToCenter(new Vector2(0, -500)); // Apply force
```

## Shapes and Collision
Bodies need shapes to participate in collision detection. Multiple shapes can be attached to a single body.

```cs
// Box shape
var boxShape = ShapePoly.CreateBox(0, 0, width, height);
body.AddShape(boxShape);

// Circle shape
var circleShape = new ShapeCircle(0, 0, radius);
body.AddShape(circleShape);

// Polygon shape (must be convex)
var vertices = new Vector2[] {
    new Vector2(-16, -16),
    new Vector2(16, -16),
    new Vector2(16, 16),
    new Vector2(-16, 16)
};
var polyShape = new ShapePoly(vertices);
body.AddShape(polyShape);

// Line segment
var segmentShape = new ShapeSegment(new Vector2(-50, 0), new Vector2(50, 0), 2); // thickness = 2
body.AddShape(segmentShape);

// Set material properties
boxShape.Density = 1.0f;
boxShape.Friction = 0.7f;
boxShape.Elasticity = 0.2f; // Restitution/bounciness
```

## Joints and Constraints
Joints connect bodies together with various constraints.

```cs
// Distance joint - maintains fixed distance
var distanceJoint = new DistanceJoint(bodyA, bodyB, anchorA, anchorB);
space.AddJoint(distanceJoint);

// Revolute joint - pin joint, allows rotation
var revoluteJoint = new RevoluteJoint(bodyA, bodyB, worldAnchor);
revoluteJoint.CollideConnected = false; // Disable collision between connected bodies
space.AddJoint(revoluteJoint);

// Rope joint - maximum distance constraint
var ropeJoint = new RopeJoint(bodyA, bodyB, anchorA, anchorB);
ropeJoint.CollideConnected = false;
space.AddJoint(ropeJoint);

// Weld joint - rigid connection
var weldJoint = new WeldJoint(bodyA, bodyB, worldAnchor);
space.AddJoint(weldJoint);
```

<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <span align="center">🤝 Contributing 🤝</span>

Check our [Contributing guide](//CONTRIBUTING.md) to see how to be part of this team.

<p align="right">(<a href="#readme-top">back to top</a>)</p>


## Contributors 🌟

<a href="https://github.com/prowlengine/prowl.drift/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=prowlengine/prowl.drift" alt="contrib.rocks image" />
</a>


# <span align="center">📜 License 📜</span>

Distributed under the MIT License. See [LICENSE](//LICENSE) for more information.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

---

### [Join our Discord server! 🎉](https://discord.gg/BqnJ9Rn4sn)
[![Discord](https://img.shields.io/discord/1151582593519722668?logo=discord
)](https://discord.gg/BqnJ9Rn4sn)