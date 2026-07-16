using System;

namespace Prowl.Editor.Core;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class InitializeOnLoadAttribute : Attribute { }
