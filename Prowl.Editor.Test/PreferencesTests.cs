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

    [Fact]
    public void AFileEditedOutsideTheEditorIsLoadedButItsOwnSavesAreNot()
    {
        PreferencesSection section = Preferences.Create(typeof(SamplePreferences));
        var prefs = (SamplePreferences)section.Instance;
        prefs.Size = 5f;
        Preferences.Changed(section);

        prefs.Size = 6f;
        Preferences.ReloadIfChanged(section);
        Assert.Equal(6f, prefs.Size);

        string path = Path.Combine(Preferences.FolderPath, section.FileName);
        File.WriteAllText(path, File.ReadAllText(path).Replace("5", "9"));
        Preferences.ReloadIfChanged(section);
        Assert.Equal(9f, prefs.Size);
    }
}

public class ProjectPreferencesTests : EditorTestHarness
{
    [EditorPreferences("Project Preferences Test", ProjectOverridable = true)]
    public sealed class SharedPreferences : EditorPreferences
    {
        public int TabSize = 4;
    }

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ProwlPreferencesTests", Guid.NewGuid().ToString("N"));
    private readonly string _previousFolder = Preferences.FolderPath;

    public ProjectPreferencesTests() => Preferences.FolderPath = _folder;

    public override void Dispose()
    {
        Preferences.FolderPath = _previousFolder;
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        base.Dispose();
    }

    [Fact]
    public void AProjectOverrideIsSavedWithTheProjectAndLeavesTheUsersValuesAlone()
    {
        PreferencesSection section = Preferences.Create(typeof(SharedPreferences));
        ((SharedPreferences)section.Instance).TabSize = 2;
        Preferences.Changed(section);

        Preferences.SetProjectOverride(section, true);
        ((SharedPreferences)section.Instance).TabSize = 8;
        Preferences.Changed(section);

        Assert.True(File.Exists(Path.Combine(Project.ProjectSettingsPath, "Preferences", section.FileName)));
        Assert.Equal(8, ((SharedPreferences)Preferences.Create(typeof(SharedPreferences)).Instance).TabSize);

        Preferences.SetProjectOverride(section, false);

        Assert.False(section.IsProjectOverride);
        Assert.Equal(2, ((SharedPreferences)section.Instance).TabSize);
    }
}
