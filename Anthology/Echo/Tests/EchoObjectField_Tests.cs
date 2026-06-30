// Tests for serializing objects that contain EchoObject fields (e.g. PrefabAsset with serialized GameObjects)

namespace Prowl.Echo.Test;

public class FakeComponent
{
    public string Name;
    public int Priority;
    public float Speed;
}

public class FakeGameObject
{
    public string Name;
    public bool IsActive;
    public List<FakeComponent> Components = new();
    public Dictionary<string, string> Tags = new();
}

public class PrefabAsset
{
    public int Id;
    public string PrefabName;
    public EchoObject GameObjectData;
}

public class SceneAsset
{
    public string SceneName;
    public List<EchoObject> GameObjects = new();
}

public class PrefabWithMetadata
{
    public int Version;
    public string Author;
    public EchoObject Data;
    public Dictionary<string, EchoObject> Overrides = new();
}

public class EchoObjectField_Tests
{
    [Fact]
    public void PrefabAsset_WithSimpleEchoObject_RoundTrips()
    {
        var gameObj = new FakeGameObject
        {
            Name = "Player",
            IsActive = true,
        };

        var prefab = new PrefabAsset
        {
            Id = 1,
            PrefabName = "PlayerPrefab",
            GameObjectData = Serializer.Serialize(typeof(FakeGameObject), gameObj),
        };

        var serialized = Serializer.Serialize(prefab);
        var deserialized = Serializer.Deserialize<PrefabAsset>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal(1, deserialized.Id);
        Assert.Equal("PlayerPrefab", deserialized.PrefabName);
        Assert.NotNull(deserialized.GameObjectData);

        // Verify we can deserialize the inner EchoObject back to the original type
        var restoredObj = Serializer.Deserialize<FakeGameObject>(deserialized.GameObjectData);
        Assert.NotNull(restoredObj);
        Assert.Equal("Player", restoredObj.Name);
        Assert.True(restoredObj.IsActive);
    }

    [Fact]
    public void PrefabAsset_WithComponents_RoundTrips()
    {
        var gameObj = new FakeGameObject
        {
            Name = "Enemy",
            IsActive = true,
            Components = new List<FakeComponent>
            {
                new() { Name = "Health", Priority = 1, Speed = 0f },
                new() { Name = "AI", Priority = 2, Speed = 5.5f },
                new() { Name = "Renderer", Priority = 10, Speed = 0f },
            }
        };

        var prefab = new PrefabAsset
        {
            Id = 42,
            PrefabName = "EnemyPrefab",
            GameObjectData = Serializer.Serialize(typeof(FakeGameObject), gameObj),
        };

        var serialized = Serializer.Serialize(prefab);
        var deserialized = Serializer.Deserialize<PrefabAsset>(serialized);

        var restored = Serializer.Deserialize<FakeGameObject>(deserialized.GameObjectData);
        Assert.NotNull(restored);
        Assert.Equal("Enemy", restored.Name);
        Assert.Equal(3, restored.Components.Count);
        Assert.Equal("Health", restored.Components[0].Name);
        Assert.Equal(5.5f, restored.Components[1].Speed);
        Assert.Equal(10, restored.Components[2].Priority);
    }

    [Fact]
    public void PrefabAsset_WithTags_RoundTrips()
    {
        var gameObj = new FakeGameObject
        {
            Name = "Chest",
            IsActive = false,
            Tags = new Dictionary<string, string>
            {
                ["layer"] = "interactable",
                ["loot_table"] = "common",
            }
        };

        var prefab = new PrefabAsset
        {
            Id = 7,
            PrefabName = "ChestPrefab",
            GameObjectData = Serializer.Serialize(typeof(FakeGameObject), gameObj),
        };

        var serialized = Serializer.Serialize(prefab);
        var deserialized = Serializer.Deserialize<PrefabAsset>(serialized);

        var restored = Serializer.Deserialize<FakeGameObject>(deserialized.GameObjectData);
        Assert.NotNull(restored);
        Assert.False(restored.IsActive);
        Assert.Equal("interactable", restored.Tags["layer"]);
        Assert.Equal("common", restored.Tags["loot_table"]);
    }

    [Fact]
    public void PrefabAsset_EchoObjectFieldIsNull_RoundTrips()
    {
        var prefab = new PrefabAsset
        {
            Id = 99,
            PrefabName = "EmptyPrefab",
            GameObjectData = null,
        };

        var serialized = Serializer.Serialize(prefab);
        var deserialized = Serializer.Deserialize<PrefabAsset>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal(99, deserialized.Id);
        Assert.Null(deserialized.GameObjectData);
    }

    [Fact]
    public void PrefabAsset_EchoObjectData_PreservesStructure()
    {
        // Verify the EchoObject itself maintains its compound structure
        var gameObj = new FakeGameObject
        {
            Name = "TestObj",
            IsActive = true,
            Components = new List<FakeComponent>
            {
                new() { Name = "Comp1", Priority = 1, Speed = 2.0f },
            }
        };

        var echoData = Serializer.Serialize(typeof(FakeGameObject), gameObj);

        // Verify the EchoObject has real data before we store it
        Assert.Equal(EchoType.Compound, echoData.TagType);
        Assert.True(echoData.TryGet("Name", out var nameTag));
        Assert.Equal("TestObj", nameTag.StringValue);

        var prefab = new PrefabAsset
        {
            Id = 1,
            PrefabName = "Test",
            GameObjectData = echoData,
        };

        var serialized = Serializer.Serialize(prefab);

        // Check the serialized PrefabAsset still has the nested data
        Assert.True(serialized.TryGet("GameObjectData", out var goData));
        Assert.Equal(EchoType.Compound, goData.TagType);
        Assert.True(goData.TryGet("Name", out var innerName));
        Assert.Equal("TestObj", innerName.StringValue);

        // Full round-trip
        var deserialized = Serializer.Deserialize<PrefabAsset>(serialized);
        Assert.Equal(EchoType.Compound, deserialized.GameObjectData.TagType);
        Assert.True(deserialized.GameObjectData.TryGet("Name", out var restoredName));
        Assert.Equal("TestObj", restoredName.StringValue);
    }

    [Fact]
    public void SceneAsset_ListOfEchoObjects_RoundTrips()
    {
        var obj1 = new FakeGameObject { Name = "Camera", IsActive = true };
        var obj2 = new FakeGameObject { Name = "Light", IsActive = true };
        var obj3 = new FakeGameObject { Name = "Player", IsActive = false };

        var scene = new SceneAsset
        {
            SceneName = "Level1",
            GameObjects = new List<EchoObject>
            {
                Serializer.Serialize(typeof(FakeGameObject), obj1),
                Serializer.Serialize(typeof(FakeGameObject), obj2),
                Serializer.Serialize(typeof(FakeGameObject), obj3),
            }
        };

        var serialized = Serializer.Serialize(scene);
        var deserialized = Serializer.Deserialize<SceneAsset>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal("Level1", deserialized.SceneName);
        Assert.Equal(3, deserialized.GameObjects.Count);

        var cam = Serializer.Deserialize<FakeGameObject>(deserialized.GameObjects[0]);
        var light = Serializer.Deserialize<FakeGameObject>(deserialized.GameObjects[1]);
        var player = Serializer.Deserialize<FakeGameObject>(deserialized.GameObjects[2]);

        Assert.Equal("Camera", cam.Name);
        Assert.Equal("Light", light.Name);
        Assert.Equal("Player", player.Name);
        Assert.False(player.IsActive);
    }

    [Fact]
    public void PrefabWithMetadata_DictionaryOfEchoObjects_RoundTrips()
    {
        var baseObj = new FakeGameObject
        {
            Name = "Base",
            IsActive = true,
            Components = new List<FakeComponent>
            {
                new() { Name = "Renderer", Priority = 1, Speed = 0f },
            }
        };

        var overrideObj = new FakeGameObject
        {
            Name = "Base",
            IsActive = false, // override: disabled
        };

        var prefab = new PrefabWithMetadata
        {
            Version = 3,
            Author = "Wulferis",
            Data = Serializer.Serialize(typeof(FakeGameObject), baseObj),
            Overrides = new Dictionary<string, EchoObject>
            {
                ["variant_disabled"] = Serializer.Serialize(typeof(FakeGameObject), overrideObj),
            }
        };

        var serialized = Serializer.Serialize(prefab);
        var deserialized = Serializer.Deserialize<PrefabWithMetadata>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal(3, deserialized.Version);
        Assert.Equal("Wulferis", deserialized.Author);

        var restoredBase = Serializer.Deserialize<FakeGameObject>(deserialized.Data);
        Assert.Equal("Base", restoredBase.Name);
        Assert.True(restoredBase.IsActive);
        Assert.Single(restoredBase.Components);

        Assert.Single(deserialized.Overrides);
        var restoredOverride = Serializer.Deserialize<FakeGameObject>(deserialized.Overrides["variant_disabled"]);
        Assert.Equal("Base", restoredOverride.Name);
        Assert.False(restoredOverride.IsActive);
    }

    [Fact]
    public void PrefabAsset_ManuallyBuiltEchoObject_RoundTrips()
    {
        // Simulate what might happen if EchoObject is built manually rather than via Serialize
        var manualData = EchoObject.NewCompound();
        manualData["Name"] = new EchoObject("ManualObj");
        manualData["IsActive"] = new EchoObject(true);
        manualData["Health"] = new EchoObject(100);
        manualData["Speed"] = new EchoObject(3.14f);

        var prefab = new PrefabAsset
        {
            Id = 55,
            PrefabName = "ManualPrefab",
            GameObjectData = manualData,
        };

        var serialized = Serializer.Serialize(prefab);
        var deserialized = Serializer.Deserialize<PrefabAsset>(serialized);

        Assert.NotNull(deserialized.GameObjectData);
        Assert.Equal(EchoType.Compound, deserialized.GameObjectData.TagType);
        Assert.Equal("ManualObj", deserialized.GameObjectData["Name"].StringValue);
        Assert.Equal(true, deserialized.GameObjectData["IsActive"].BoolValue);
        Assert.Equal(100, deserialized.GameObjectData["Health"].IntValue);
        Assert.Equal(3.14f, deserialized.GameObjectData["Speed"].FloatValue);
    }

    [Fact]
    public void PrefabAsset_BinaryRoundTrip_PreservesEchoObject()
    {
        var gameObj = new FakeGameObject
        {
            Name = "BinaryTest",
            IsActive = true,
            Components = new List<FakeComponent>
            {
                new() { Name = "Physics", Priority = 5, Speed = 9.8f },
            }
        };

        var prefab = new PrefabAsset
        {
            Id = 10,
            PrefabName = "BinaryPrefab",
            GameObjectData = Serializer.Serialize(typeof(FakeGameObject), gameObj),
        };

        // Serialize to EchoObject, then to binary, then back
        var echoData = Serializer.Serialize(prefab);
        var binary = new EchoBinaryFormat();
        var bytes = binary.WriteToBytes(echoData);
        var fromBinary = binary.ReadFromBytes(bytes);

        var deserialized = Serializer.Deserialize<PrefabAsset>(fromBinary);

        Assert.NotNull(deserialized);
        Assert.Equal(10, deserialized.Id);

        var restored = Serializer.Deserialize<FakeGameObject>(deserialized.GameObjectData);
        Assert.Equal("BinaryTest", restored.Name);
        Assert.Single(restored.Components);
        Assert.Equal("Physics", restored.Components[0].Name);
        Assert.Equal(9.8f, restored.Components[0].Speed);
    }

    [Fact]
    public void PrefabAsset_StringRoundTrip_PreservesEchoObject()
    {
        var gameObj = new FakeGameObject
        {
            Name = "StringTest",
            IsActive = false,
            Tags = new Dictionary<string, string> { ["type"] = "npc" }
        };

        var prefab = new PrefabAsset
        {
            Id = 20,
            PrefabName = "StringPrefab",
            GameObjectData = Serializer.Serialize(typeof(FakeGameObject), gameObj),
        };

        // Serialize to EchoObject, then to string, then back
        var echoData = Serializer.Serialize(prefab);
        var text = EchoTextFormat.Write(echoData);
        var fromText = EchoTextFormat.Read(text);

        var deserialized = Serializer.Deserialize<PrefabAsset>(fromText);

        Assert.NotNull(deserialized);
        Assert.Equal(20, deserialized.Id);

        var restored = Serializer.Deserialize<FakeGameObject>(deserialized.GameObjectData);
        Assert.Equal("StringTest", restored.Name);
        Assert.False(restored.IsActive);
        Assert.Equal("npc", restored.Tags["type"]);
    }

    [Fact]
    public void Deserialization_DoesNotMutateSourceEchoObject()
    {
        var gameObj = new FakeGameObject
        {
            Name = "Player",
            IsActive = true,
            Components = new List<FakeComponent>
            {
                new() { Name = "Health", Priority = 1, Speed = 0f },
            }
        };

        // Serialize with type info at root (no targetType = Auto includes $type)
        var serialized = Serializer.Serialize(gameObj);

        // Snapshot the keys before deserialization
        var keysBefore = serialized.Tags.Keys.ToHashSet();
        Assert.Contains("$type", keysBefore);

        // Deserialize
        var deserialized = Serializer.Deserialize<FakeGameObject>(serialized);
        Assert.Equal("Player", deserialized.Name);

        // The source EchoObject must NOT have been mutated
        var keysAfter = serialized.Tags.Keys.ToHashSet();
        Assert.Equal(keysBefore, keysAfter);
        Assert.Contains("$type", keysAfter);
    }

    [Fact]
    public void Deserialization_SameEchoObject_CanBeDeserializedMultipleTimes()
    {
        var gameObj = new FakeGameObject
        {
            Name = "Enemy",
            IsActive = false,
        };

        var serialized = Serializer.Serialize(gameObj);

        // Deserialize the same EchoObject multiple times — should all succeed
        for (int i = 0; i < 3; i++)
        {
            var result = Serializer.Deserialize<FakeGameObject>(serialized);
            Assert.NotNull(result);
            Assert.Equal("Enemy", result.Name);
            Assert.False(result.IsActive);
        }
    }
}
