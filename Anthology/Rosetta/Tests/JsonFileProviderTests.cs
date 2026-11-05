// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Rosetta.Tests;

public class JsonFileProviderTests
{
    private static string LocalizationPath => Path.Combine(AppContext.BaseDirectory, "Localization", "{locale}.json");

    [Fact]
    public void SupportsLocale_ReturnsTrueForExistingFile()
    {
        var provider = new JsonFileProvider(LocalizationPath);
        Assert.True(provider.SupportsLocale("en"));
        Assert.True(provider.SupportsLocale("fr"));
    }

    [Fact]
    public void SupportsLocale_ReturnsFalseForMissingFile()
    {
        var provider = new JsonFileProvider(LocalizationPath);
        Assert.False(provider.SupportsLocale("zz"));
    }

    [Fact]
    public void Load_FlattensNestedKeys()
    {
        var provider = new JsonFileProvider(LocalizationPath);
        var data = provider.Load("en");

        // Direct keys
        Assert.Equal("Confirm", data["ui.button.confirm"]);

        // Nested plural keys get flattened
        Assert.Equal("{{count}} item", data["item.collected.one"]);
        Assert.Equal("{{count}} items", data["item.collected.other"]);

        // Nested gender keys get flattened
        Assert.Equal("He nods at you.", data["npc.greeting.male"]);
        Assert.Equal("She smiles at you.", data["npc.greeting.female"]);
    }

    [Fact]
    public void Load_ThrowsForMissingFile()
    {
        var provider = new JsonFileProvider(LocalizationPath);
        Assert.Throws<FileNotFoundException>(() => provider.Load("zz"));
    }

    [Fact]
    public void Changed_TriggersWhenAJsonFileIsModified()
    {
        // Create a temporary JSON localization file.
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """
            {
                "ui.button.confirm": "Confirm",
                "ui.button.cancel": "Cancel"
            }
            """);

            // Check it loads as expected.
            int changeCount = 0;
            var provider = new JsonFileProvider(path);
            provider.Changed += (object? sender, EventArgs e) => changeCount++;
            Assert.Equal("Confirm", provider.Load("en")["ui.button.confirm"]);

            // Now modify the file.
            File.WriteAllText(path, """
            {
                "ui.button.confirm": "Confirm",
                "ui.button.cancel": "Cancel",
                "ui.button.new": "New"
            }
            """);

            // Wait a moment for the file system watcher to detect the change.
            Thread.Sleep(100);

            // The event should have fired.
            Assert.True(changeCount > 0);
        }
        finally
        {
            // Delete the temporary file.
            File.Delete(path);
        }
    }
}
