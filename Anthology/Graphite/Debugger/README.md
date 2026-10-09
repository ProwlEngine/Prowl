# Prowl.Graphite.Debugger

Record a Graphite graph execution, save it to disk, and replay any single pass (or even a single draw) later, on the same GPU or a different one.

There are two kinds of recording:

| Type            | What it holds                                                   | Cost   |
|-----------------|-----------------------------------------------------------------|--------|
| `Recording`     | Views, passes, per-pass stats, GPU timings per command buffer   | Light  |
| `DeepRecording` | Everything in `Recording`, plus commands, programs, and resource contents needed to replay | Heavy  |

Both are passed to `DispatchGraph` as a profiler, used for exactly one execution, and read once the execution is done.

## API

| Type                    | Purpose                                                                  |
|-------------------------|--------------------------------------------------------------------------|
| `Recording`             | Light capture. `IsDone`, `Wait()`, `Views`, `CommandBuffers`.            |
| `DeepRecording`         | Full capture. Same lifecycle, plus `Views` (with commands and copies), `Programs`, `Resources`, `Blobs`. Disposable. |
| `DeepMode`              | `Full` copies every pass output. `ReplayOnly` copies only what replay cannot recreate. |
| `Replayer`              | `new Replayer(device, deepRecording)` then `Replay(request)`.            |
| `ReplayRequest`         | `ViewIndex`, `PassIndex`, optional `EventIndex` (stop after that draw or dispatch). |
| `ReplayResult`          | `Status`, `Reason`, and `Outputs` (the bytes of each pass output).       |
| `ReplayStatus`          | `Restored`, `Reexecuted`, or `NotReplayable`.                            |
| `DebuggerSerialization` | `Register()` once, then use Echo to save and load recordings.            |

Reading `Views`, `Programs` and friends before the recording is done throws. Check `IsDone` or call `Wait()`.

## Samples

Light recording:

```csharp
Recording recording = new(device);
device.DispatchGraph(pipeline, views, recording);
recording.Wait();

foreach (RecordedView view in recording.Views)
    foreach (RecordedPass pass in view.Passes)
        Console.WriteLine($"{view.Name}/{pass.Name}");

foreach (RecordedCommandBuffer buffer in recording.CommandBuffers)
    Console.WriteLine($"{buffer.Name}: {buffer.Milliseconds} ms");
```

Deep recording and replay:

```csharp
using DeepRecording recording = new(device, DeepMode.Full);
device.DispatchGraph(pipeline, views, recording);
recording.Wait();

Replayer replayer = new(device, recording);
ReplayResult result = replayer.Replay(new ReplayRequest { ViewIndex = 0, PassIndex = 2 });

if (result.Status == ReplayStatus.NotReplayable)
    Console.WriteLine(result.Reason);
else
    byte[] pixels = result.Outputs[0].Data.Items.ToArray();
```

Replay up to the first draw of a pass:

```csharp
ReplayResult result = replayer.Replay(new ReplayRequest { ViewIndex = 0, PassIndex = 2, EventIndex = 0 });
```

Save and load:

```csharp
DebuggerSerialization.Register();

using (BinaryWriter writer = new(File.Create("frame.trace")))
    Serializer.Serialize(recording).WriteToBinary(writer);

using BinaryReader reader = new(File.OpenRead("frame.trace"));
DeepRecording loaded = Serializer.Deserialize<DeepRecording>(EchoObject.ReadFromBinary(reader))!;
```

A loaded recording works with `Replayer` the same as a fresh one. A full runnable version is in `Samples/DebuggerReplay`.

## How it works

**Recording.** The recording plugs into the graph as a profiler. While the graph runs it notes every view and pass, and a deep recording also captures each command a pass records (framebuffers, pipelines, properties, draws, dispatches, updates, copies). Programs are stored once by key, with their SPIR-V. When a pass touches a buffer or texture whose contents the recording cannot recreate (for example, something uploaded from the CPU), the recording copies it into a staging buffer. Once the GPU finishes, those copies are read back and stored as blobs, keyed by their SHA-256 hash, so identical data is stored once.

**Replaying.** `Replayer` has two ways to answer a request:

- `Restored`: in `Full` mode the output of every pass was already copied, so asking for a whole pass just returns the stored bytes. No GPU work.
- `Reexecuted`: for a single draw (`EventIndex`), or in `ReplayOnly` mode, it rebuilds the needed textures, buffers, samplers and programs on your device, restores the unreproducible inputs from the blobs, runs the passes up to the one you asked for, and reads the result back. It starts as late as it can, only running earlier passes when their outputs cannot be restored from a copy.

If something cannot be replayed (an undeclared GPU write, a depth or multisampled input that cannot be restored yet, a format the replay device does not support), `Replay` returns `NotReplayable` with a reason instead of throwing.

**Saving.** Everything in a deep recording is plain data (records, ids, and blobs), so Echo can write it to a file and read it back with no GPU.
