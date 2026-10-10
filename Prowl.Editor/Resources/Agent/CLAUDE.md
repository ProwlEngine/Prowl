# Prowl project

This is a game made with the Prowl engine. While the Prowl editor has this project open, you can drive it from the terminal.

- Run the CLI from the project root as `Library/prowl` (bash) or `Library\prowl.cmd` (cmd or PowerShell). From a subfolder use the path to it, such as `../../Library/prowl`. It finds the open editor from any folder inside the project. `Library/prowl command` lists every command and its arguments.
- Prowl looks like Unity but is not Unity. Names, signatures and behaviour differ. Before writing code against an engine API, look it up with `Library/prowl command api --type Transform` or `--search Rigidbody`.
- After editing scripts, run `Library/prowl command compile` and fix every error it reports before moving on.
