# API Validation

NeoVeldrid validates API usage at runtime. When you pass an invalid buffer offset, use a mismatched resource layout, or forget to set a required pipeline state, NeoVeldrid throws a descriptive exception instead of crashing silently or producing undefined behavior.

This validation is enabled by default in all NuGet packages. The performance cost is negligible. Benchmarks across all backends show no measurable difference with validation enabled vs disabled.

In the majority of cases, there is no point in disabling these validations. However, if you are developing a heavy CPU-bound project and you need to squeeze out every last CPU cycle, you can build NeoVeldrid from source passing the `DisableValidation=true` MSBuild flag:

```bash
dotnet build -p:DisableValidation=true
```
