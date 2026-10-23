---
name: prowl-profiler
description: Capture, save, load, inspect and replay frame snapshots of the Prowl renderer from the terminal. Use when debugging how a frame is drawn, finding what a render pass or draw call produced, checking GPU time per pass, comparing a bug against a saved frame, or getting an image of an intermediate render target.
---

# Frame snapshots

A snapshot is a deep recording of one editor frame made by Graphite, the renderer. It holds every group, pass, command, resource and shader of that frame, so you can read it without the editor redrawing anything. The same snapshots open in the editor's render profiler panels (Window menu), and files saved from either side load in the other.

The editor must be open on this project and drawing frames. All commands run as `Library/prowl command <name> ...`.

## Workflow

1. Get a snapshot: `snapshot --action capture`. It records the next frame and returns a summary. To look at a game frame, first `play --action start` and `play --action wait --seconds 1`. Pass `--mode ReplayOnly` for a much smaller snapshot that re-executes passes instead of storing every output.
2. Find the pass: `snapshot_dump` lists groups and passes with draws, dispatches, CPU and GPU milliseconds, what each reads and writes, and its `events` count.
3. Read the pass: `snapshot_dump --kind pass --group 2 --pass 0` lists its commands (page with `--from` and `--count`). Commands that draw, dispatch, clear or copy have an `event` number.
4. See what it produced: `snapshot_replay --group 2 --pass 0` writes each output texture as a PNG and returns the paths. Open the images to look at them.
5. Narrow down: `snapshot_replay --group 2 --pass 0 --event 3` runs the pass only up to its fourth event, so you can see the image after a single draw. `--event -1` runs none and shows what the pass starts from. Bisect `--event` to find the draw that breaks the picture.

```bash
Library/prowl command snapshot --action capture
Library/prowl command snapshot_dump
Library/prowl command snapshot_dump --kind pass --group 1 --pass 0 --count 50
Library/prowl command snapshot_replay --group 1 --pass 0 --resource SceneColor --exposure 1
```

## Commands

| Command | Use |
| --- | --- |
| `snapshot --action capture\|info\|save\|load` | Capture, summarize, write or read a snapshot file. `save --path x.gsn` defaults to `Library/Cli/Snapshots`. |
| `snapshot_dump --kind groups\|pass\|resources\|programs` | Read the snapshot. `groups` is the default and the best place to start. |
| `snapshot_replay --group G --pass P` | Replay a pass and write its outputs. Options: `--event`, `--resource <name>`, `--exposure`, `--resolution <px>`, `--full`, `--out <folder>`. |

Group index `-1` holds passes added outside any group. Use the indices exactly as `snapshot_dump` prints them.

## Reading results

- `snapshot_replay` returns `status`. `Restored` means the images are the copies taken during the frame, `Reexecuted` means the GPU ran the pass again from recorded inputs. A snapshot taken in `Full` mode restores whole passes and only re-executes when you pass `--event`.
- `matchesRecorded` on an output compares the replayed bytes with the recorded ones. `false` after a re-execution can mean nondeterministic shaders as well as a real difference.
- A pass can be `notReplayable`, with the reason in `snapshot_dump`. Replaying a later pass that depends on it fails with the same reason.
- Outputs are 8 bit PNGs. HDR targets clip at 1, so raise or lower `--exposure` to see dark or bright detail. Depth is stretched over the range of depths it actually covers. Formats that cannot be turned into a PNG (compressed, integer) and buffers are written as raw `.bin` files.
- Images are at most 2048 pixels on their longer side by default. Larger targets, such as an 8192 shadow atlas, are shrunk by skipping texels, so thin detail can drop out. `--resolution 512` makes a quicker, smaller image and `--full` keeps every texel. Each file lists its image `width` and `height` next to the texture's `textureWidth` and `textureHeight`.
- Resource names in the dump are the render graph names, such as the ones passes declare they read and write. Pass one to `--resource` to write only that output.

## Gotchas

- `capture` records the frame the editor draws next, which is the scene view, game view, and editor UI together. Group names show which part drew what.
- Loading a snapshot replaces the current one. The editor frees the old one, so `save` first if you need it.
- Saved files are gzip compressed, so `save` reports far fewer bytes than the summary's `blobBytes`.
- CPU and GPU times leave out the recording itself and its copies, but the recording still disturbs caches, so treat small per-pass CPU times as approximate.
- Replays run on the editor's GPU, which may be a different backend from the one that recorded a loaded file. The result `Reason` says so.
