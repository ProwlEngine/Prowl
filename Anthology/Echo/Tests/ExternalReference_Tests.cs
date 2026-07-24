// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

namespace Prowl.Echo.Test
{
    public class ExternalReference_Tests
    {
        // Stand-in for a scene object (GameObject/Component). References to objects inside the
        // copied selection should be duplicated; references pointing out of it should resolve back
        // to the original live instance.
        private sealed class SceneObject
        {
            public Guid Id = Guid.NewGuid();
            public string Name = "";
            public SceneObject? Target;          // may point outside the selection
            public SceneObject? Alt;             // used to check internal sharing survives
            public List<SceneObject> Children = new();
        }

        private sealed class SceneRefResolver : IExternalReferenceResolver
        {
            private readonly HashSet<SceneObject> _selection;
            private readonly Dictionary<Guid, SceneObject> _scene;

            public SceneRefResolver(HashSet<SceneObject> selection, Dictionary<Guid, SceneObject> scene)
            {
                _selection = selection;
                _scene = scene;
            }

            public object? GetReferenceKey(object value)
                => value is SceneObject so && !_selection.Contains(so) ? so.Id : null;

            public object? ResolveReference(object key, Type targetType)
                => key is Guid g && _scene.TryGetValue(g, out var so) ? so : null;
        }

        private static (SceneObject clone, SceneObject external) CopyPaste()
        {
            var external = new SceneObject { Name = "External" };
            var root = new SceneObject { Name = "Root", Target = external };
            var child = new SceneObject { Name = "Child", Target = external };
            root.Children.Add(child);
            root.Alt = child; // second internal reference to the same child

            var scene = new Dictionary<Guid, SceneObject>
            {
                [external.Id] = external,
                [root.Id] = root,
                [child.Id] = child,
            };
            var selection = new HashSet<SceneObject> { root, child };

            var ctx = new SerializationContext
            {
                ExternalReferences = new SceneRefResolver(selection, scene)
            };

            var echo = Serializer.Serialize(root, ctx);
            var clone = Serializer.Deserialize<SceneObject>(echo, new SerializationContext
            {
                ExternalReferences = new SceneRefResolver(selection, scene)
            })!;

            return (clone, external);
        }

        [Fact]
        public void InSelectionObjectsAreCopied()
        {
            var (clone, external) = CopyPaste();

            Assert.NotNull(clone);
            Assert.Equal("Root", clone.Name);
            Assert.Single(clone.Children);
            Assert.Equal("Child", clone.Children[0].Name);
            Assert.NotSame(external, clone); // root itself was copied, not linked
        }

        [Fact]
        public void OutOfSelectionReferencesResolveToLiveInstance()
        {
            var (clone, external) = CopyPaste();

            Assert.Same(external, clone.Target);             // root -> external is the live object
            Assert.Same(external, clone.Children[0].Target); // child -> external is the same live object
        }

        [Fact]
        public void InternalSharingIsPreserved()
        {
            var (clone, _) = CopyPaste();

            // Both internal references pointed at the same child; the clone must keep that identity.
            Assert.Same(clone.Children[0], clone.Alt);
        }

        [Fact]
        public void UnresolvableReferenceBecomesNull()
        {
            var external = new SceneObject { Name = "External" };
            var root = new SceneObject { Name = "Root", Target = external };

            var selection = new HashSet<SceneObject> { root };
            var serializeScene = new Dictionary<Guid, SceneObject> { [external.Id] = external };

            var echo = Serializer.Serialize(root, new SerializationContext
            {
                ExternalReferences = new SceneRefResolver(selection, serializeScene)
            });

            // Paste into a scene where the external object no longer exists.
            var clone = Serializer.Deserialize<SceneObject>(echo, new SerializationContext
            {
                ExternalReferences = new SceneRefResolver(selection, new Dictionary<Guid, SceneObject>())
            })!;

            Assert.NotNull(clone);
            Assert.Null(clone.Target);
        }

        [Fact]
        public void NoResolverCopiesEverything()
        {
            var external = new SceneObject { Name = "External" };
            var root = new SceneObject { Name = "Root", Target = external };

            var echo = Serializer.Serialize(root);
            var clone = Serializer.Deserialize<SceneObject>(echo)!;

            Assert.NotNull(clone.Target);
            Assert.NotSame(external, clone.Target); // without a resolver, the reference is deep-copied
            Assert.Equal("External", clone.Target!.Name);
        }
    }
}
