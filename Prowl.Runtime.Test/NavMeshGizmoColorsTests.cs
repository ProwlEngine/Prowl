// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Navigation;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>Exercises <see cref="NavMeshGizmoColors"/>: the built-in Humanoid type keeps its historical
/// blue, every id gets a color, and two different ids never collide.</summary>
public class NavMeshGizmoColorsTests
{
    [Fact]
    public void HumanoidType_KeepsTheHistoricalBlue()
    {
        Color color = NavMeshGizmoColors.ForAgentType(NavMeshAgentTypes.HumanoidId);
        Assert.Equal(new Color(0.1f, 0.6f, 1f, 1f), color);
    }

    [Fact]
    public void SameId_AlwaysProducesTheSameColor()
    {
        Color first = NavMeshGizmoColors.ForAgentType(7);
        Color second = NavMeshGizmoColors.ForAgentType(7);
        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    [InlineData(1, 5)]
    public void DifferentIds_ProduceDifferentColors(int a, int b)
    {
        Color colorA = NavMeshGizmoColors.ForAgentType(a);
        Color colorB = NavMeshGizmoColors.ForAgentType(b);
        Assert.NotEqual(colorA, colorB);
    }

    [Fact]
    public void EveryColor_IsFullyOpaque()
    {
        for (int id = 0; id < 8; id++)
            Assert.Equal(1f, NavMeshGizmoColors.ForAgentType(id).A);
    }
}
