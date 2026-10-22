// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Editor.Theming;
using Prowl.Runtime;

using Xunit;

namespace Prowl.Editor.Test;

public class PreferencesTests : IDisposable
{
    [EditorPreferences("Preferences Test")]
    public sealed class SamplePreferences : EditorPreferences
    {
        [Range(1, 10)] public float Size = 4f;
        public bool Enabled = true;
        public List<int> Columns = [80, 120];
    }

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ProwlPreferencesTests", Guid.NewGuid().ToString("N"));
    private readonly string _previousFolder = Preferences.FolderPath;

    public PreferencesTests() => Preferences.FolderPath = _folder;

    public void Dispose()
    {
        Preferences.FolderPath = _previousFolder;
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    [Fact]
    public void ChangesAreSavedAndLoadedBack()
    {
        PreferencesSection section = Preferences.Create(typeof(SamplePreferences));
        var prefs = (SamplePreferences)section.Instance;
        prefs.Size = 7f;
        prefs.Columns.Add(160);
        Preferences.Changed(section);

        var loaded = (SamplePreferences)Preferences.Create(typeof(SamplePreferences)).Instance;

        Assert.Equal(7f, loaded.Size);
        Assert.Equal([80, 120, 160], loaded.Columns);
    }

    [Fact]
    public void ChangedFieldsListsOnlyWhatDiffersFromTheDefaults()
    {
        PreferencesSection section = Preferences.Create(typeof(SamplePreferences));
        var prefs = (SamplePreferences)section.Instance;
        Assert.Empty(Preferences.ChangedFields(section));

        prefs.Enabled = false;
        prefs.Columns = [80, 120];

        Assert.Equal(["Enabled"], Preferences.ChangedFields(section));
    }

    [Fact]
    public void ResettingAFieldCopiesTheDefaultRatherThanSharingIt()
    {
        PreferencesSection section = Preferences.Create(typeof(SamplePreferences));
        var prefs = (SamplePreferences)section.Instance;
        prefs.Columns = [1];

        Preferences.ResetField(section, nameof(SamplePreferences.Columns));
        prefs.Columns.Add(200);

        Assert.Equal([80, 120], ((SamplePreferences)section.Defaults).Columns);
        Assert.Contains("Columns", Preferences.ChangedFields(section));
    }
}
