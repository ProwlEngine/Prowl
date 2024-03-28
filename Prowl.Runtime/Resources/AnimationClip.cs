using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Runtime
{
    public sealed class AnimationClip : EngineObject, ISerializable
    {
        public double Duration;
        public double TicksPerSecond;
        public double DurationInTicks;

        public MultiValueDictionary<string, AnimCurve> Curves { get; private set; } = [];

        public void SetCurve(string nodeName, Type component, string propertyName, AnimationCurve curve)
        {
            Curves.Add(nodeName, new AnimCurve {
                Component = component,
                PropertyName = propertyName,
                Curve = curve
            });
        }

        public void Deserialize(SerializedProperty value, Serializer.SerializationContext ctx)
        {
            Duration = value.Get("Duration").DoubleValue;
            TicksPerSecond = value.Get("TicksPerSecond").DoubleValue;
            DurationInTicks = value.Get("DurationInTicks").DoubleValue;

            var curveList = value.Get("Curves");
            foreach (var curve in curveList.List)
            {
                var nodeName = curve.Get("NodeName").StringValue;
                var animCurves = curve.Get("AnimCurves");
                foreach (var animCurveProp in animCurves.List)
                {
                    var component = RuntimeUtils.FindType(animCurveProp.Get("Component").StringValue);
                    var propertyName = animCurveProp.Get("PropertyName").StringValue;
                    var curveValue = Serializer.Deserialize<AnimationCurve>(animCurveProp.Get("Curve"), ctx);
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

            var curveList = SerializedProperty.NewList();
            foreach (var curve in Curves)
            {
                var curveValue = SerializedProperty.NewCompound();
                curveValue.Add("NodeName", new SerializedProperty(curve.Key));
                var animCurves = SerializedProperty.NewList();
                foreach (var animCurve in curve.Value)
                {
                    var animCurveProp = SerializedProperty.NewCompound();
                    animCurveProp.Add("Component", new SerializedProperty(animCurve.Component.FullName));
                    animCurveProp.Add("PropertyName", new SerializedProperty(animCurve.PropertyName));
                    animCurveProp.Add("Curve", Serializer.Serialize(animCurve.Curve, ctx));
                    animCurves.ListAdd(animCurveProp);
                }
                curveValue.Add("AnimCurves", animCurves);

                curveList.ListAdd(curveValue);
            }

            value.Add("Curves", curveList);
            return value;
        }


        public class AnimCurve
        {
            public Type Component;
            public string PropertyName;
            public AnimationCurve Curve;
        }

    }
}
