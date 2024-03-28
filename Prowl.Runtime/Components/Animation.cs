using System;
using System.Collections.Generic;
using System.Reflection;
using static Prowl.Runtime.AnimationClip;

namespace Prowl.Runtime
{
    public class Animation : MonoBehaviour
    {

        public List<AssetRef<AnimationClip>> Clips;
        public AssetRef<AnimationClip> DefaultClip;
        public bool PlayAutomatically = true;
        public double Speed = 1.0;

        private Dictionary<string, ActiveClip> activeClips = new(StringComparer.OrdinalIgnoreCase);

        public override void OnEnable()
        {
            // Assign DefaultClip to the first clip if it's not set
            if (!DefaultClip.IsAvailable && Clips.Count > 0)
                DefaultClip = Clips[0];

            foreach (var clip in Clips)
                if(clip.IsAvailable)
                    ProcessClip(clip.Res!);
            if (DefaultClip.IsAvailable) {
                ProcessClip(DefaultClip.Res!);
                if (PlayAutomatically)
                    Play(DefaultClip.Res!.Name);
            }
        }

        public void AddClip(AnimationClip clip) => ProcessClip(clip);

        public void Blend(string clipName, double targetWeight, double fadeLength = 0.3f)
        {
            if (activeClips.ContainsKey(clipName))
                activeClips[clipName].CrossFade(targetWeight, fadeLength);
        }

        public void CrossFade(string clipName, double fadeLength = 0.3f)
        {
            double totalWeight = 0;
            foreach (var activeClip in activeClips)
            {
                if (activeClip.Key.Equals(clipName, StringComparison.OrdinalIgnoreCase))
                    activeClip.Value.CrossFade(1, fadeLength);
                else
                    activeClip.Value.CrossFade(0, fadeLength);

                totalWeight += activeClip.Value.Weight;
            }

            // Normalize weights
            foreach (var activeClip in activeClips)
                activeClip.Value.NormalizeWeight(totalWeight);
        }

        public ActiveClip? Play(string clipName) 
        {
            if (activeClips.TryGetValue(clipName, out ActiveClip? value))
            {
                value.Play();
                return value;
            }
            return null;
        }

        public void Stop() 
        { 
            // Stop all
            foreach (var activeClip in activeClips)
                activeClip.Value.Stop();
        }

        public override void Update()
        {


            foreach (var activeClip in activeClips)
                activeClip.Value.Update(this);
        }


        private void ProcessClip(AnimationClip clip)
        {
            foreach (var nodeName in clip.Targets.Keys)
            {
                var target = this.GameObject.transform.DeepFind(nodeName);
                if (target == null)
                    continue;

                ActiveClip activeClip = new(clip);

                var tracks = clip.Targets[nodeName];
                foreach (var track in tracks)
                {
                    // Transform is a special case
                    if (track.Component == typeof(Transform))
                    {
                        activeClip.Tracks.Add(new ActiveTracks(target, track));
                        continue;
                    }

                    // Find the component on the target
                    var component = target.gameObject.GetComponent(track.Component);
                    if (component == null)
                        continue;

                    activeClip.Tracks.Add(new ActiveTracks(component, track));
                }
            
            }
        }

        public class ActiveClip(AnimationClip clip)
        {
            public AnimationClip Clip = clip;

            public bool Playing = false;
            public double Time = 0;
            public List<ActiveTracks> Tracks = [];

            public double Weight => weight;

            private double weight = 0;

            private double targetWeight = 0;
            private double fadeLength = 0.3f;
            private double startTime = 0;

            private double direction = 1.0f;

            public void NormalizeWeight(double totalWeight)
            {
                weight /= totalWeight;
            }

            public void CrossFade(double targetWeight, double fadeLength)
            {
                this.targetWeight = targetWeight;
                this.fadeLength = fadeLength;
                startTime = Runtime.Time.time;
            }

            public void Play() => Playing = true;

            public void Stop()
            {
                Playing = false;
                Time = 0;
                weight = 0;
            }

            public void Update(Animation host)
            {
                if (!Playing)
                    return;

                Time += (Runtime.Time.deltaTimeF * host.Speed) * direction;
                if (Time > Clip.Duration)
                {
                    if (Clip.Wrap == WrapMode.Once)
                    {
                        Playing = false;
                        Time = 0;
                        weight = 0;
                    }
                    else if (Clip.Wrap == WrapMode.Loop)
                        Time = 0;
                    else if (Clip.Wrap == WrapMode.PingPong)
                    {
                        Time = Clip.Duration;
                        direction *= -1; // Reverse the direction
                    }
                    else if (Clip.Wrap == WrapMode.ClampForever)
                    {
                        Time = Clip.Duration;
                    }
                }

                // Update Weight
                if (weight != targetWeight)
                {
                    var t = (Runtime.Time.time - startTime) / fadeLength;
                    weight = Mathf.LerpClamped(weight, targetWeight, t);
                }
            }
        }

        public class ActiveTracks
        {
            public object Target;

            public double value = 0;
            private Track track;

            public ActiveTracks(object target, Track animCurve)
            {
                Target = target;
                track = animCurve;

                value = track.Curve.Evaluate(0);
            }

            public void SetField(double value, double weight)
            {
                // Weights should always be normalized between all active clips
                // So it should be safe to apply the weight here
                if (track.Parent == null)
                {
                    track.Member.SetValue(Target, value * weight);
                }
                else
                {
                    var parent = track.Parent.GetValue(Target);
                    track.Member.SetValue(parent, value * weight);
                }
            }
        }

    }
}
