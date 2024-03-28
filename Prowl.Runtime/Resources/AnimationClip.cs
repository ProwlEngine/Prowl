using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Prowl.Runtime
{
    public sealed class AnimationClip : EngineObject, ISerializable
    {
        public double Duration;
        public double TicksPerSecond;
        public double DurationInTicks;

        public enum WrapMode
        {
            Once,
            Loop,
            PingPong,
            ClampForever,
        }

        public WrapMode Wrap;

        public MultiValueDictionary<string, Track> Targets { get; private set; } = [];

        public void ClearCurves() => Targets.Clear();

        public void SetCurve(string nodeName, Type component, string propertyName, AnimationCurve curve)
        {
            var animCurve = new Track {
                Component = component,
                PropertyName = propertyName,
                Curve = curve
            };

            // Cache MemberInfo
            // PropertyName can be a field or property name
            // And can either be the name directly or a path to the property
            // e.g. "playerSpeed" or "localPosition.x"
            // It can however only be a simple path, no arrays or lists and only 1 level deep
            // As well as only floats and doublesCertainly! Here's an updated implementation of the AnimationClip class with support for caching the MemberInfo and handling property names that can go one level deep:

            var propertyParts = propertyName.Split('.');
            if (propertyParts.Length > 2)
                throw new Exception($"PropertyName {propertyName} is too complex. Only simple paths (1 level deep) are supported.");

            MemberInfo member;
            MemberInfo parent = null;
            if (propertyParts.Length == 1)
            {
                animCurve.isDirect = true;
                member = component.GetMember(propertyName)?.FirstOrDefault() ?? throw new Exception($"Member {propertyName} not found on {component.FullName}");
            }
            else
            {
                animCurve.isDirect = false;
                parent = component.GetMember(propertyParts[0])?.FirstOrDefault() ?? throw new Exception($"Member {propertyParts[0]} not found on {component.FullName}");
                if (parent == null)
                    throw new Exception($"Member {propertyParts[0]} not found on {component.FullName}");

                var parentType = (parent as PropertyInfo)?.PropertyType ?? (parent as FieldInfo)?.FieldType;
                member = parentType.GetMember(propertyParts[1])?.FirstOrDefault() ?? throw new Exception($"Member {propertyParts[1]} not found on {parentType.FullName}");
            }

            var memberType = (member as PropertyInfo)?.PropertyType ?? (member as FieldInfo)?.FieldType;
            if (memberType != typeof(double))
                throw new Exception($"Member {propertyName} on {component.FullName} is not of type double.");

            animCurve.Parent = parent;
            animCurve.Member = member;



            Targets.Add(nodeName, animCurve);
        }

        public void Deserialize(SerializedProperty value, Serializer.SerializationContext ctx)
        {
            Duration = value.Get("Duration").DoubleValue;
            TicksPerSecond = value.Get("TicksPerSecond").DoubleValue;
            DurationInTicks = value.Get("DurationInTicks").DoubleValue;
            Wrap = (WrapMode)value.Get("Wrap").IntValue;

            var targets = value.Get("Targets");
            foreach (var target in targets.List)
            {
                var nodeName = target.Get("NodeName").StringValue;
                var tracks = target.Get("Tracks");
                foreach (var trackProp in tracks.List)
                {
                    var component = RuntimeUtils.FindType(trackProp.Get("Component").StringValue);
                    var propertyName = trackProp.Get("PropertyName").StringValue;
                    var curveValue = Serializer.Deserialize<AnimationCurve>(trackProp.Get("Curve"), ctx);
                    SetCurve(nodeName, component, propertyName, curveValue);
                }

            }
        }

        public SerializedProperty Serialize(Serializer.SerializationContext ctx)
        {
            var value = SerializedProperty.NewCompound();
            value.Add("Duration", new SerializedProperty(Duration));
            value.Add("TicksPerSecond", new SerializedProperty(TicksPerSecond));
            value.Add("DurationInTicks", new SerializedProperty(DurationInTicks));
            value.Add("Wrap", new SerializedProperty((int)Wrap));

            var targets = SerializedProperty.NewList();
            foreach (var kvp in Targets)
            {
                var kvpProp = SerializedProperty.NewCompound();
                kvpProp.Add("NodeName", new SerializedProperty(kvp.Key));
                var tracks = SerializedProperty.NewList();
                foreach (var track in kvp.Value)
                {
                    var trackProp = SerializedProperty.NewCompound();
                    trackProp.Add("Component", new SerializedProperty(track.Component.FullName));
                    trackProp.Add("PropertyName", new SerializedProperty(track.PropertyName));
                    trackProp.Add("Curve", Serializer.Serialize(track.Curve, ctx));
                    tracks.ListAdd(trackProp);
                }
                kvpProp.Add("Tracks", tracks);

                targets.ListAdd(kvpProp);
            }

            value.Add("Targets", targets);
            return value;
        }


        public class Track
        {
            public Type Component;
            public string PropertyName;
            public AnimationCurve Curve;

            public bool isDirect;
            public MemberInfo Parent;
            public MemberInfo Member;
        }

    }
}
