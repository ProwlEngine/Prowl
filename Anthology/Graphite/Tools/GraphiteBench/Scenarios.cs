using System;
using System.Collections.Generic;

using Prowl.Graphite;

using Prowl.Vector;

namespace Prowl.Graphite.Bench;

public sealed record ScenarioConfig(int Passes, int DrawsPerPass, int WarmupFrames, int Frames, int Repetitions);


public static class Scenarios
{
    public static readonly string[] Names = ["draws-changing", "passes-shared-view", "draws-identical", "draws-shared-mesh"];

    public static ScenarioConfig DefaultConfig(string name) => name switch
    {
        "passes-shared-view" => new ScenarioConfig(Passes: 32, DrawsPerPass: 8, WarmupFrames: 64, Frames: 128, Repetitions: 7),
        _ => new ScenarioConfig(Passes: 1, DrawsPerPass: 2048, WarmupFrames: 32, Frames: 64, Repetitions: 7)
    };

    public static BenchResult Run(GraphicsDevice gd, BenchScene scene, BenchProfiler profiler, string name, ScenarioConfig config)
        => name switch
        {
            "draws-changing" => DrawsChanging(gd, scene, profiler, config),
            "passes-shared-view" => PassesSharedView(gd, scene, profiler, config),
            "draws-identical" => DrawsIdentical(gd, scene, profiler, config),
            "draws-shared-mesh" => DrawsSharedMesh(gd, scene, profiler, config),
            _ => throw new ArgumentException($"Unknown scenario '{name}'. Known: {string.Join(", ", Names)}", nameof(name))
        };

    private static BenchResult DrawsChanging(GraphicsDevice gd, BenchScene scene, BenchProfiler profiler, ScenarioConfig config)
    {
        PropertySet[] models = CreateModels(scene, config.DrawsPerPass);
        float spin = 0f;

        return BenchRunner.Run(gd, scene, profiler, "draws-changing", config.Passes, config.DrawsPerPass,
            config.WarmupFrames, config.Frames, config.Repetitions, (cmd, passIndex) =>
            {
                spin += 0.01f;
                cmd.SetShader(scene.Program);
                cmd.SetProperties(scene.ViewProperties);
                cmd.SetVertexSource(scene.VertexSource);

                for (int i = 0; i < models.Length; i++)
                {
                    models[i].SetMatrix("MatrixModel", Float4x4.CreateTranslation(new Float3(spin + i, 0, 0)));
                    cmd.SetProperties(models[i]);
                    cmd.DrawIndexed();
                }
            });
    }

    private static BenchResult PassesSharedView(GraphicsDevice gd, BenchScene scene, BenchProfiler profiler, ScenarioConfig config)
    {
        PropertySet[] models = CreateModels(scene, config.DrawsPerPass);

        return BenchRunner.Run(gd, scene, profiler, "passes-shared-view", config.Passes, config.DrawsPerPass,
            config.WarmupFrames, config.Frames, config.Repetitions, (cmd, passIndex) =>
            {
                cmd.SetShader(scene.Program);
                cmd.SetProperties(scene.ViewProperties);
                cmd.SetVertexSource(scene.VertexSource);

                for (int i = 0; i < models.Length; i++)
                {
                    cmd.SetProperties(models[i]);
                    cmd.DrawIndexed();
                }
            });
    }

    private static BenchResult DrawsIdentical(GraphicsDevice gd, BenchScene scene, BenchProfiler profiler, ScenarioConfig config)
    {
        PropertySet[] equivalent =
        [
            scene.CreateModelProperties(new Float4(1, 1, 1, 1)),
            scene.CreateModelProperties(new Float4(1, 1, 1, 1))
        ];

        return BenchRunner.Run(gd, scene, profiler, "draws-identical", config.Passes, config.DrawsPerPass,
            config.WarmupFrames, config.Frames, config.Repetitions, (cmd, passIndex) =>
            {
                cmd.SetShader(scene.Program);
                cmd.SetProperties(scene.ViewProperties);
                cmd.SetVertexSource(scene.VertexSource);

                for (int i = 0; i < config.DrawsPerPass; i++)
                {
                    cmd.SetProperties(equivalent[i & 1]);
                    cmd.DrawIndexed();
                }
            });
    }

    private static BenchResult DrawsSharedMesh(GraphicsDevice gd, BenchScene scene, BenchProfiler profiler, ScenarioConfig config)
    {
        PropertySet model = scene.CreateModelProperties(new Float4(1, 1, 1, 1));

        return BenchRunner.Run(gd, scene, profiler, "draws-shared-mesh", config.Passes, config.DrawsPerPass,
            config.WarmupFrames, config.Frames, config.Repetitions, (cmd, passIndex) =>
            {
                cmd.SetShader(scene.Program);
                cmd.SetProperties(scene.ViewProperties);
                cmd.SetProperties(model);

                for (int i = 0; i < config.DrawsPerPass; i++)
                {
                    cmd.SetVertexSource(scene.VertexSource);
                    cmd.DrawIndexed();
                }
            });
    }

    private static PropertySet[] CreateModels(BenchScene scene, int count)
    {
        PropertySet[] models = new PropertySet[count];
        for (int i = 0; i < count; i++)
            models[i] = scene.CreateModelProperties(new Float4(i / (float)count, 0.5f, 1f, 1f));

        return models;
    }
}
