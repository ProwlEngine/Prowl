# Contributing to NeoVeldrid

Thanks for your interest in contributing!

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A GPU that supports at least one of: Vulkan, D3D11, OpenGL

## Building

```bash
dotnet build NeoVeldrid.slnx
```

## Testing

Run all tests (SPIRV tests are non-GPU, the rest require a GPU):

```bash
dotnet test
```

To run GPU tests for a specific backend:

```bash
dotnet test tests/NeoVeldrid.Tests/NeoVeldrid.Tests.csproj --filter "Backend=Vulkan"
```

Available backends: `Vulkan`, `D3D11`, `OpenGL`, `OpenGLES`.

## Before Submitting a PR

NeoVeldrid is a graphics abstraction layer. We can't run GPU tests or visual validation in CI, so **contributors are responsible for testing locally** before submitting.

- **Run the test suite** on your machine. At minimum, run `dotnet test`. If your change touches a specific backend, run the tests for that backend.
- **Run the samples visually** if your change touches rendering code. The samples in `samples/` are the primary way to catch rendering regressions. Try at least GettingStarted and NeoDemo on the backends available to you.
- **Test on multiple platforms if you can.** This is a cross-platform library. We understand not everyone has access to Windows, Linux, and macOS, but testing on more than one platform is appreciated.
- **Write tests when possible.** If you're fixing a bug, a test that reproduces it is ideal. We know GPU tests are harder to write than traditional unit tests, so this isn't a hard requirement, but the effort is valued.

## Guidelines

- **No API breaking changes.** NeoVeldrid 1.x preserves API compatibility with upstream [Veldrid](https://github.com/mellinoe/veldrid). If you want to propose an API change, open an issue to discuss it first. Breaking changes will be considered for 2.0.
- **Keep PRs focused.** One logical change per PR. Don't bundle unrelated fixes or reformatting.
- **Follow existing patterns.** Match the coding style of the surrounding code. Avoid reformatting code you didn't change.
- **All tests must pass.** PRs with failing tests won't be merged.
