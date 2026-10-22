using System;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>Debug group payload of one indirect draw call: its world, shader and program, and what each command draws.</summary>
[Serializable]
public sealed record DrawCallGroup(string World, string Shader, string Program, DrawnCommand[] Commands);

/// <summary>What one indirect command of a draw call draws, one entry per instance.</summary>
[Serializable]
public sealed record DrawnCommand(DrawnRenderable[] Renderables);

/// <summary>A renderable as a command drew it that frame. The handle is invalid for provider items and instance batches.</summary>
[Serializable]
public sealed record DrawnRenderable(RenderableHandle Handle, string? Name, string? Material, Double4x4 Transform);

/// <summary>Debug group payload of one shadow tile: the light, its face or cascade, and the tile's place in its page.</summary>
[Serializable]
public sealed record ShadowFaceGroup(string World, LightHandle Light, string? LightName, LightType Type, int Face, bool Cascade, int X, int Y, int Size);

internal static class DebugGroups
{
    public static DrawCallGroup DrawCall(RenderWorld world, Shader shader, GraphicsProgram program, DrawList list, DrawCall call)
    {
        RenderWorld.RenderSide side = world.Render;
        DrawnCommand[] commands = new DrawnCommand[call.CommandCount];
        for (int c = 0; c < call.CommandCount; c++)
        {
            int command = call.FirstCommand + c;
            DrawRecord record = list.Records[command];
            uint count = list.Commands[command].InstanceCount;
            bool shared = (record.Flags & DrawRecord.SharedObject) != 0;
            DrawnRenderable[] drawn = new DrawnRenderable[shared ? 1 : count];
            for (int i = 0; i < drawn.Length; i++)
            {
                int index = shared ? (int)record.Object : (int)list.Instances[record.FirstInstance + i];
                drawn[i] = Renderable(side, index, record);
            }
            commands[c] = new DrawnCommand(drawn);
        }

        return new DrawCallGroup(world.Name, shader.Name, program.Name, commands);
    }

    public static ShadowFaceGroup ShadowFace(RenderWorld world, int light, int face, bool cascade, ShadowTile tile)
    {
        ref readonly LightDesc desc = ref world.Lighting.Light(light);
        return new ShadowFaceGroup(world.Name, world.Lighting.Handle(light), desc.Name, desc.Type, face, cascade, tile.X, tile.Y, tile.Size);
    }

    private static DrawnRenderable Renderable(RenderWorld.RenderSide side, int index, in DrawRecord record)
    {
        if (index >= side.Capacity || !side.Alive[index] || (record.Flags & DrawRecord.DirectSlot) != 0)
            return new DrawnRenderable(default, null, null, Double4x4.Identity);

        Material? material = record.SubMesh < side.SliceCount[index] ? side.SliceMaterials[side.SliceBase[index] + record.SubMesh] : null;
        return new DrawnRenderable(new RenderableHandle(index, side.Generation[index]), side.Name[index], material?.Name ?? material?.Shader.Name, side.Transform[index]);
    }
}
