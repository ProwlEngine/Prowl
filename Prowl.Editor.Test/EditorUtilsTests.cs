// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Utils;

using Xunit;

namespace Prowl.Editor.Test;

public class EditorUtilsTests
{
    /// <summary>
    /// Guards every place a name the user typed becomes a folder or a file: creating a project,
    /// renaming an asset, naming a build folder or a macOS bundle, and keying the variant cache.
    /// </summary>
    public class SafeFileNameTests
    {
        [Theory]
        [InlineData("MyGame", "MyGame")]
        [InlineData("My Game 2", "My Game 2")]
        [InlineData("  padded  ", "padded")]
        public void OrdinaryNames_PassThrough(string input, string expected)
            => Assert.Equal(expected, EditorUtils.SafeFileName(input, "fallback"));

        // A separator is the dangerous one: Path.Combine would put the result somewhere else.
        [Theory]
        [InlineData("a/b")]
        [InlineData("a\\b")]
        [InlineData("a:b")]
        [InlineData("a*b?")]
        [InlineData("a\"b<c>d|e")]
        public void SeparatorsAndReservedCharacters_AreReplaced(string input)
        {
            string safe = EditorUtils.SafeFileName(input, "fallback");

            Assert.DoesNotContain(Path.GetInvalidFileNameChars(), safe.Contains);
            Assert.Equal(safe, Path.GetFileName(safe));
        }

        // These survive the character filter and still walk up a directory, which is the whole reason
        // dropping invalid characters is not enough on its own.
        [Theory]
        [InlineData(".")]
        [InlineData("..")]
        [InlineData("...")]
        [InlineData("")]
        [InlineData("   ")]
        public void TraversalAndEmptyNames_BecomeTheFallback(string input)
            => Assert.Equal("fallback", EditorUtils.SafeFileName(input, "fallback"));

        // A rooted name is what makes Path.Combine discard the folder it was given entirely.
        [Fact]
        public void ARootedName_CannotEscapeTheFolderItIsCombinedWith()
        {
            string safe = EditorUtils.SafeFileName(@"C:\Windows\System32", "fallback");
            string combined = Path.Combine(@"C:\Projects", safe);

            Assert.StartsWith(@"C:\Projects", combined);
        }

        [Fact]
        public void ALeadingDot_IsKeptWhenTheRestIsRealName()
            => Assert.Equal(".gitignore", EditorUtils.SafeFileName(".gitignore", "fallback"));
    }
}
