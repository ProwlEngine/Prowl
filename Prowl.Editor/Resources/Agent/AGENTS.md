# Prowl project

This is a game made with the Prowl engine. While the Prowl editor has this project open, you can drive it from the terminal.

- Run the CLI from the project root as `Library/prowl` (bash) or `Library\prowl.cmd` (cmd or PowerShell). From a subfolder use the path to it, such as `../../Library/prowl`. It finds the open editor from any folder inside the project. `Library/prowl command` lists every command and its arguments.
- Prowl looks like Unity but is not Unity. Names, signatures and behaviour differ. Before writing code against an engine API, look it up with `Library/prowl command api --type Transform` or `--search Rigidbody`.
- After editing scripts, run `Library/prowl command compile` and fix every error it reports before moving on.

Read these guides before working in their area:

- `.claude/skills/prowl-cli/SKILL.md`: driving the editor, refs, building scenes, checking your work.
- `.claude/skills/prowl-scenes/SKILL.md`: scenes, GameObjects, components, the script lifecycle, Transform, input and time.
- `.claude/skills/prowl-assets/SKILL.md`: asset references, AssetRef, loading and unloading, finding assets at runtime.
- `.claude/skills/prowl-serialization/SKILL.md`: what gets saved, attributes, custom serialization, references.
- `.claude/skills/prowl-physics/SKILL.md`: raycasts, rigidbodies, colliders, the character controller, collision and trigger events.
- `.claude/skills/prowl-animation/SKILL.md`: model import, the Animator, animation graphs, root motion, attaching to bones.
- `.claude/skills/prowl-ui/SKILL.md`: game UI with Paper in OnGui or GameObject UI, cursor locking.
- `.claude/skills/prowl-rendering/SKILL.md`: materials, cameras, lights, environment, lightmaps, particles.
- `.claude/skills/prowl-paper/SKILL.md`: the Paper UI library in depth: layout, styling, animation, events, text fields, scrolling, custom drawing.
