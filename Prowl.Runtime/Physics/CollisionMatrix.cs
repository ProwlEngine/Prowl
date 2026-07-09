// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Utils;

namespace Prowl.Runtime;

public static class CollisionMatrix
{
    public static Boolean32Matrix s_collisionMatrix = new(true);

    /// <summary>
    /// Sets the collision matrix for two layers
    /// </summary>
    public static void SetLayerCollision(int layer1Index, int layer2Index, bool shouldCollide)
    {
        s_collisionMatrix.SetSymmetric(layer1Index, layer2Index, shouldCollide);
    }

    /// <summary>
    /// Makes sure the collision matrix is symmetric (if [a,b] collides, [b,a] should too)
    /// </summary>
    public static void EnsureSymmetric()
    {
        s_collisionMatrix.MakeSymmetric();
    }

    /// <summary>
    /// Sets all collisions for a specific layer
    /// </summary>
    public static void SetLayerCollisions(int layer, bool shouldCollide)
    {
        s_collisionMatrix.SetRow(layer, shouldCollide);
        // Make sure to maintain symmetry
        s_collisionMatrix.SetColumn(layer, shouldCollide);
    }

    /// <summary>
    /// Gets weather two layers should collide
    /// </summary>
    public static bool GetLayerCollision(int layer1, int layer2)
    {
        return s_collisionMatrix[layer1, layer2];
    }

    /// <summary>
    /// Gets all collisions for a specific layer
    /// </summary>
    public static bool[] GetLayerCollisions(int layer)
    {
        return s_collisionMatrix.GetRow(layer);
    }

    /// <summary>
    /// Sets all layers to collide or not collide
    /// </summary>
    public static void SetAllCollisions(bool shouldCollide)
    {
        s_collisionMatrix.SetAll(shouldCollide);
    }
}
