// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.ParticleSystem.Modules;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.ParticleSystem;

public enum SimulationSpace
{
    /// <summary>Particles move with the emitter.</summary>
    Local,
    /// <summary>Particles stay where they were born when the emitter moves.</summary>
    World,
    /// <summary>Particles move with <see cref="ParticleSystemComponent.CustomSimulationSpace"/>.</summary>
    Custom
}

public enum ParticleScalingMode
{
    /// <summary>The emitter's world scale scales the shape, the particles and their movement.</summary>
    Hierarchy,
    /// <summary>Like Hierarchy but only the emitter's own local scale counts, not its parents'.</summary>
    Local,
    /// <summary>Scale only stretches the emission shape. Particle sizes and movement ignore it.</summary>
    Shape
}

public enum ParticleStopAction
{
    None,
    /// <summary>Disables the GameObject when the system finishes.</summary>
    Disable,
    /// <summary>Destroys the GameObject when the system finishes.</summary>
    Destroy,
    /// <summary>Only raises <see cref="ParticleSystemComponent.Stopped"/>, which every stop does anyway.</summary>
    Callback
}

public enum ParticleCullingMode
{
    AlwaysSimulate,
    /// <summary>Stops simulating while no camera sees the system.</summary>
    Pause,
    /// <summary>Stops simulating while unseen, then fast forwards the missed time once seen again.</summary>
    PauseAndCatchUp
}

public enum ParticleStopBehavior
{
    /// <summary>No new particles, the live ones play out.</summary>
    StopEmitting,
    /// <summary>No new particles and the live ones are removed.</summary>
    StopEmittingAndClear
}

public enum EmitterVelocityMode
{
    /// <summary>Measured from how far the transform moved.</summary>
    Transform,
    /// <summary>Read from a Rigidbody3D on the same GameObject, falling back to the transform.</summary>
    Rigidbody
}

/// <summary>Overrides for <see cref="ParticleSystemComponent.Emit(in EmitParams, int)"/>. Unset values come from the modules.</summary>
public struct EmitParams
{
    /// <summary>Position in simulation space. Replaces the shape's spawn point.</summary>
    public Float3? Position;
    /// <summary>Velocity in simulation space. Replaces the shape direction times start speed.</summary>
    public Float3? Velocity;
    public Float3? StartSize;
    /// <summary>Rotation in degrees.</summary>
    public Float3? Rotation;
    public Color? StartColor;
    public float? StartLifetime;
}

/// <summary>
/// A CPU simulated particle system drawn with GPU instancing. Settings are grouped into modules, much
/// like the particle systems artists already know.
/// <para>
/// In edit mode a system only simulates while it, or anything above it in the hierarchy, is selected,
/// and it clears again when deselected.
/// </para>
/// </summary>
[AddComponentMenu("Effects/Particle System")]
[ExecuteAlways]
[ComponentIcon("")] // WandMagicSparkles
public class ParticleSystemComponent : MonoBehaviour
{
    #region Main

    [Tooltip("Seconds in one loop.")]
    public float Duration = 5.0f;

    public bool Looping = true;

    [ShowIf(nameof(Looping)), Tooltip("Start as if a whole loop had already played.")]
    public bool Prewarm = false;

    [Tooltip("Seconds to wait before emitting, sampled on every Play.")]
    public MinMaxCurve StartDelay = new(0f);

    public SimulationSpace SimulationSpace = SimulationSpace.Local;

    [ShowIf(nameof(IsCustomSpace))]
    public GameObject? CustomSimulationSpace;

    [Tooltip("Multiplies how fast time passes for this system.")]
    public float SimulationSpeed = 1f;

    [Tooltip("Ignore Time.TimeScale.")]
    public bool UseUnscaledTime = false;

    public ParticleScalingMode ScalingMode = ParticleScalingMode.Hierarchy;

    public bool PlayOnEnable = true;

    public int MaxParticles = 1000;

    [Tooltip("A new random sequence every Play. Off replays the same sequence from Random Seed.")]
    public bool AutoRandomSeed = true;

    [ShowIf(nameof(UsesFixedSeed))]
    public uint RandomSeed = 0;

    public EmitterVelocityMode EmitterVelocityMode = EmitterVelocityMode.Transform;

    public ParticleStopAction StopAction = ParticleStopAction.None;

    public ParticleCullingMode CullingMode = ParticleCullingMode.AlwaysSimulate;

    private bool IsCustomSpace => SimulationSpace == SimulationSpace.Custom;
    private bool UsesFixedSeed => !AutoRandomSeed;

    #endregion

    #region Modules

    [HideInInspector] public InitialModule Initial = new();
    [HideInInspector] public EmissionModule Emission = new();
    [HideInInspector] public ShapeModule Shape = new();
    [HideInInspector] public VelocityOverLifetimeModule VelocityOverLifetime = new();
    [HideInInspector] public LimitVelocityOverLifetimeModule LimitVelocityOverLifetime = new();
    [HideInInspector] public InheritVelocityModule InheritVelocity = new();
    [HideInInspector] public ForceOverLifetimeModule ForceOverLifetime = new();
    [HideInInspector] public WindModule Wind = new();
    [HideInInspector] public ColorOverLifetimeModule ColorOverLifetime = new();
    [HideInInspector] public ColorBySpeedModule ColorBySpeed = new();
    [HideInInspector] public SizeOverLifetimeModule SizeOverLifetime = new();
    [HideInInspector] public SizeBySpeedModule SizeBySpeed = new();
    [HideInInspector] public RotationOverLifetimeModule RotationOverLifetime = new();
    [HideInInspector] public RotationBySpeedModule RotationBySpeed = new();
    [HideInInspector] public CollisionModule Collision = new();
    [HideInInspector] public SubEmittersModule SubEmitters = new();
    [HideInInspector] public TextureSheetAnimationModule TextureSheet = new();
    [HideInInspector] public TrailModule Trails = new();
    [HideInInspector] public CustomDataModule CustomData = new();
    [HideInInspector] public RendererModule Renderer = new();

    #endregion

    #region Events

    /// <summary>Raised when the system finishes or is stopped and cleared.</summary>
    public event Action<ParticleSystemComponent>? Stopped;

    /// <summary>Raised for every particle collision, after the simulation step that found it.</summary>
    public event Action<ParticleSystemComponent, ParticleCollisionEvent>? ParticleCollided;

    #endregion

    #region State

    private const float MaxStep = 0.1f;
    private const float MaxCatchUp = 10f;
    private const float MaxFrameDelta = 0.25f;
    private const int MaxCollisionEvents = 4096;
    private const float PreviewRestartDelay = 1f;

    private Particle[] _particles = Array.Empty<Particle>();
    private int _count;
    private Random _random = new();

    private bool _isPlaying;
    private bool _isPaused;
    private bool _isEmitting;
    private float _time;
    private float _delay;
    private double _simTime;
    private float _catchUp;
    private float _culledTime;
    private float _culledDeadline;
    private bool _prewarming;

    // Spaces. Particles live in simulation space, the emitter's shape lives in emitter space.
    private Float4x4 _simToWorld = Float4x4.Identity;
    private Float4x4 _worldToSim = Float4x4.Identity;
    private Float4x4 _emitterToSim = Float4x4.Identity;
    private Float4x4 _emitterToWorld = Float4x4.Identity;
    private bool _simIsWorld;
    private bool _spacesValid;
    private (SimulationSpace, ParticleScalingMode, GameObject?) _spaceKey;
    private Float3 _emitterOriginSim;
    private float _sizeScale = 1f;

    // Emitter motion over the frame being simulated.
    private bool _hasEmitterHistory;
    private Float3 _previousEmitterPosition;
    private Float3 _previousEmitterOriginSim;
    private Float3 _emitterVelocitySim;
    private Float3 _emitterMoveSim;
    private float _emitterMoveDistance;
    private Float3 _gravitySim;
    private PhysicsWorld? _physics;
    private float _frameDt;
    private float _stepStart;
    private float _stepDt;

    private AABB _worldBounds;
    private bool _hasBounds;
    private Float3 _simBoundsMin;
    private Float3 _simBoundsMax;
    private float _boundsPad;

    private long _lastVisibleFrame = long.MinValue / 2;
    private long _selectedFrame = long.MinValue / 2;
    private bool _previewActive;
    private bool _previewHeld;
    private float _previewIdle;

    // The system whose sub emitter list names this one. While it does, this one never emits on its own.
    private ParticleSystemComponent? _driver;
    private long _emitSpacesFrame = long.MinValue;

    private readonly List<ParticleCollisionEvent> _collisionEvents = new();
    private readonly List<SubEmitRequest> _subEmitQueue = new();
    private readonly List<ParticleSystemComponent> _subEmitTargets = new();
    private bool _hasBirthSubEmitters;
    private bool _hasCollisionSubEmitters;
    private bool _hasDeathSubEmitters;

    private struct SubEmitRequest
    {
        public int Emitter;
        public int Count;
        public Float3 Position;
        /// <summary>Where the parent particle started this step. Birth emission spreads along the way.</summary>
        public Float3 PreviousPosition;
        /// <summary>Seconds the spread covers, 0 to emit everything at <see cref="Position"/>.</summary>
        public float Span;
        public Float3 Velocity;
        public Color Color;
        public Float3 Size;
        public Float3 Rotation;
        public float LifeLeft;
    }

    #endregion

    #region Public API

    /// <summary>Playing and not paused. Stays true after emission ends while particles are still alive.</summary>
    public bool IsPlaying => _isPlaying && !_isPaused;
    public bool IsPaused => _isPlaying && _isPaused;
    public bool IsStopped => !_isPlaying;
    public bool IsEmitting => _isPlaying && !_isPaused && _isEmitting;
    public int ParticleCount => _count;

    /// <summary>The live particles. Valid until the next simulation step.</summary>
    public ReadOnlySpan<Particle> Particles => new(_particles, 0, _count);

    /// <summary>Seconds into the current loop.</summary>
    public float PlaybackTime
    {
        get => _time;
        set => _time = Maths.Clamp(value, 0f, EffectiveDuration);
    }

    /// <summary>Seconds simulated since the system was last cleared.</summary>
    public float TotalTime => (float)_simTime;

    internal double SimulationTime => _simTime;
    internal int Capacity => _particles.Length;

    /// <summary>Collisions found during the last simulated frame.</summary>
    public IReadOnlyList<ParticleCollisionEvent> CollisionEvents => _collisionEvents;

    /// <summary>World bounds of the live particles.</summary>
    public AABB WorldBounds => _worldBounds;
    public bool HasBounds => _hasBounds;

    /// <summary>
    /// Starts the system, or resumes it when paused. A playing system that is still emitting is left alone.
    /// </summary>
    public void Play(bool withChildren = true)
    {
        _previewHeld = false;
        if (_isPlaying && _isPaused)
            _isPaused = false;
        else if (!_isPlaying || !_isEmitting)
            StartPlayback();

        if (withChildren)
            foreach (ParticleSystemComponent child in Children())
                child.Play(false);
    }

    /// <summary>Freezes the system where it is. <see cref="Play"/> resumes it.</summary>
    public void Pause(bool withChildren = true)
    {
        if (!Application.IsPlaying) _previewHeld = true;
        if (_isPlaying)
            _isPaused = true;

        if (withChildren)
            foreach (ParticleSystemComponent child in Children())
                child.Pause(false);
    }

    public void Stop(bool withChildren = true, ParticleStopBehavior behavior = ParticleStopBehavior.StopEmitting)
    {
        if (!Application.IsPlaying) _previewHeld = true;
        if (_isPlaying)
        {
            _isEmitting = false;
            _isPaused = false;
            if (behavior == ParticleStopBehavior.StopEmittingAndClear)
                ClearParticles();
            CheckFinished();
        }

        if (withChildren)
            foreach (ParticleSystemComponent child in Children())
                child.Stop(false, behavior);
    }

    /// <summary>
    /// Clears the system and plays it again from the start. Unlike stopping and playing, this never runs
    /// the stop action.
    /// </summary>
    public void Restart(bool withChildren = true)
    {
        _previewHeld = false;
        ClearParticles();
        StartPlayback();

        if (withChildren)
            foreach (ParticleSystemComponent child in Children())
                child.Restart(false);
    }

    /// <summary>
    /// Forgets where the emitter was, so its next move is not treated as travel. Call after teleporting it,
    /// or a world space system fills the jump with particles and hands them the jump as velocity.
    /// </summary>
    public void ResetMotionHistory() => _hasEmitterHistory = false;

    /// <summary>Removes every particle and trail without stopping the system.</summary>
    public void Clear(bool withChildren = true)
    {
        ClearParticles();

        if (withChildren)
            foreach (ParticleSystemComponent child in Children())
                child.Clear(false);
    }

    /// <summary>
    /// Fast forwards the system by <paramref name="time"/> seconds and leaves it paused there, which is
    /// handy for scrubbing. <paramref name="restart"/> starts from the beginning first.
    /// </summary>
    public void Simulate(float time, bool withChildren = true, bool restart = true)
    {
        var systems = new List<ParticleSystemComponent> { this };
        if (withChildren)
            systems.AddRange(Children());

        // Children restart first, so nothing a parent emits into them is cleared afterwards.
        for (int i = systems.Count - 1; i >= 0; i--)
        {
            ParticleSystemComponent system = systems[i];
            if (restart)
            {
                system.ClearParticles();
                system.StartPlayback();
            }
            else if (!system._isPlaying)
            {
                system.StartPlayback();
            }
        }

        // The whole tree steps together, so sub emitter children receive and simulate their particles
        // at the same moments they would in play.
        StepTogether(systems.ToArray(), time, false);

        foreach (ParticleSystemComponent system in systems)
            system._isPaused = true;
    }

    /// <summary>Emits <paramref name="count"/> particles from the shape right now, whether or not the system is emitting.</summary>
    public void Emit(int count) => Emit(new EmitParams(), count);

    /// <summary>Emits <paramref name="count"/> particles right now, with any values in <paramref name="overrides"/> taking precedence.</summary>
    public void Emit(in EmitParams overrides, int count)
    {
        if (count <= 0) return;
        ApplyMaxParticles();
        RefreshSpaces();
        if (!_isPlaying)
        {
            _isPlaying = true;
            _isEmitting = false;
        }

        float time01 = _time / EffectiveDuration;
        for (int i = 0; i < count && _count < MaxParticles; i++)
        {
            Shape.Sample(_random, out Float3 localPosition, out Float3 localDirection);
            Float3 position = overrides.Position ?? Point(_emitterToSim, localPosition);
            Float3 direction = Float3.NormalizeSafe(Vector(_emitterToSim, localDirection), Float3.UnitY);

            ref Particle p = ref AddParticle();
            InitParticle(ref p, position, direction, time01, _emitterVelocitySim);
            if (overrides.Velocity.HasValue) p.Velocity = overrides.Velocity.Value;
            if (overrides.StartSize.HasValue) p.Size = p.StartSize = overrides.StartSize.Value;
            if (overrides.Rotation.HasValue) p.Rotation = overrides.Rotation.Value;
            if (overrides.StartColor.HasValue) p.Color = p.StartColor = overrides.StartColor.Value;
            if (overrides.StartLifetime.HasValue) p.Lifetime = p.StartLifetime = MathF.Max(0f, overrides.StartLifetime.Value);
            FinishSpawn(ref p);
        }
        RecalculateBounds();
    }

    /// <summary>Copies the live particles into <paramref name="destination"/>. Returns how many were copied.</summary>
    public int GetParticles(Span<Particle> destination)
    {
        int n = Math.Min(destination.Length, _count);
        new ReadOnlySpan<Particle>(_particles, 0, n).CopyTo(destination);
        return n;
    }

    /// <summary>Replaces every live particle. Trails restart from the new positions.</summary>
    public void SetParticles(ReadOnlySpan<Particle> particles)
    {
        Trails.Clear();
        int count = Math.Min(particles.Length, Math.Max(0, MaxParticles));
        if (_particles.Length < count)
            Array.Resize(ref _particles, count);
        _count = count;
        particles[.._count].CopyTo(_particles);
        for (int i = 0; i < _count; i++)
            _particles[i].TrailSlot = 0;
        if (_count > 0 && !_isPlaying)
        {
            _isPlaying = true;
            _isEmitting = false;
        }
        RefreshSpaces();
        RecalculateBounds();
    }

    /// <summary>True while it has particles or trails, or is still going to emit. Includes sub emitter children.</summary>
    public bool IsAlive(bool withChildren = true)
    {
        if (_count > 0 || Trails.HasOrphans || (_isPlaying && _isEmitting))
            return true;

        if (withChildren)
            foreach (ParticleSystemComponent child in Children())
                if (child.IsAlive(false))
                    return true;

        if (SubEmitters.Enabled)
            foreach (SubEmitter entry in SubEmitters.Emitters)
                if (entry.System.IsValid() && !ReferenceEquals(entry.System, this) && entry.System.ParticleCount > 0)
                    return true;

        return false;
    }

    /// <summary>Fires sub emitter <paramref name="index"/> from every live particle.</summary>
    public void TriggerSubEmitter(int index) => TriggerSubEmitter(index, 0, _count);

    /// <summary>Fires sub emitter <paramref name="index"/> from one particle.</summary>
    public void TriggerSubEmitter(int index, int particleIndex) => TriggerSubEmitter(index, particleIndex, 1);

    private void TriggerSubEmitter(int index, int first, int count)
    {
        if ((uint)index >= (uint)SubEmitters.Emitters.Count || first < 0 || first + count > _count) return;
        RefreshSpaces();
        for (int i = first; i < first + count; i++)
            QueueSubEmit(index, -1, SimPointToWorld(_particles[i].Position), in _particles[i]);
        FlushSubEmitters();
    }

    #endregion

    #region Lifecycle

    public override void OnEnable()
    {
        // Claim sub emitter children before any of them gets to emit on its own.
        ClaimSubEmitters();
        if (Application.IsPlaying && PlayOnEnable)
            Play(false);
    }

    public override void OnDisable()
    {
        _isPlaying = false;
        _isPaused = false;
        _isEmitting = false;
        _previewActive = false;
        ClearParticles();
        ReleaseSubEmitters();
        Renderer.ReleaseMeshes();
    }

    public override void OnValidate()
    {
        Duration = MathF.Max(0.05f, Duration);
        MaxParticles = Math.Max(0, MaxParticles);
        SimulationSpeed = MathF.Max(0f, SimulationSpeed);
    }

    public override void Update()
    {
        // Claimed every frame, even while stopped, so a child never gets a frame to emit on its own.
        ClaimSubEmitters();

        if (!Application.IsPlaying && !UpdateEditorPreview())
        {
            _hasEmitterHistory = false;
            return;
        }

        // Any frame the emitter is not simulated breaks its motion history, so a system that resumes
        // after a pause or a cull does not treat everything it missed as one frame of travel.
        float dt = (UseUnscaledTime ? Time.UnscaledDeltaTime : Time.DeltaTime) * MathF.Max(0f, SimulationSpeed);
        if (!_isPlaying || _isPaused || dt <= 0f)
        {
            _hasEmitterHistory = false;
            return;
        }

        // A debugger pause or loading hitch must not turn into hundreds of simulation steps.
        dt = MathF.Min(dt, MaxFrameDelta);

        if (Application.IsPlaying && CullingMode != ParticleCullingMode.AlwaysSimulate && !IsDriven && Time.FrameCount - _lastVisibleFrame > 2)
        {
            UpdateCulled(dt);
            _hasEmitterHistory = false;
            return;
        }

        _culledTime = 0f;
        dt += _catchUp;
        _catchUp = 0f;
        Advance(dt);
    }

    /// <summary>
    /// Keeps time for a system no camera sees. A non looping one still has to finish, so its stop action
    /// runs off screen too: once everything it could have emitted would be dead, it is cleared and stopped.
    /// </summary>
    private void UpdateCulled(float dt)
    {
        if (CullingMode == ParticleCullingMode.PauseAndCatchUp)
            _catchUp = MathF.Min(_catchUp + dt, MaxCatchUp);

        if (Looping) return;

        if (_culledTime <= 0f)
            _culledDeadline = CulledLifespan();
        _culledTime += dt;
        if (_culledTime < _culledDeadline) return;

        _culledTime = 0f;
        _catchUp = 0f;
        _isEmitting = false;
        ClearParticles();
        CheckFinished();
    }

    /// <summary>The longest this system could still have something alive, counting emission still to come.</summary>
    private float CulledLifespan()
    {
        float longest = 0f;
        for (int i = 0; i < _count; i++)
            longest = MathF.Max(longest, _particles[i].Lifetime);

        float emitting = _isEmitting ? _delay + MathF.Max(0f, EffectiveDuration - _time) : 0f;
        float newest = _isEmitting ? Initial.StartLifetime.EstimateMax() : 0f;
        float life = MathF.Max(longest, newest);
        if (Trails.Enabled && !Trails.DieWithParticles)
            life *= 1f + MathF.Max(0f, Trails.Lifetime.EstimateMax());
        return emitting + life;
    }

    /// <summary>Records that a camera sees the system this frame, which keeps a culled system simulating.</summary>
    internal void MarkVisible() => _lastVisibleFrame = Time.FrameCount;

    public override void OnRenderCollect(SceneCuller culler)
    {
        if (_count == 0 && !Trails.HasOrphans && CullingMode == ParticleCullingMode.AlwaysSimulate)
            return;

        RefreshSpaces();
        MarkVisible();
    }

    public override void DrawGizmosSelected()
    {
        PreviewRoot()._selectedFrame = Time.FrameCount;

        if (Shape.Enabled)
        {
            RefreshSpaces();
            Shape.DrawGizmo(_emitterToWorld, new Color(0.35f, 0.75f, 1f, 1f));
        }
    }

    #endregion

    #region Playback

    private float EffectiveDuration => MathF.Max(0.05f, Duration);

    /// <summary>
    /// Whether a live, enabled system currently lists this one as a sub emitter. Checked against the
    /// driver's list every time, so removing the entry or disabling the driver hands the system back.
    /// </summary>
    private bool IsDriven
    {
        get
        {
            ParticleSystemComponent? driver = _driver;
            if (driver.IsNotValid() || !driver.EnabledInHierarchy || !driver.SubEmitters.Enabled) return false;
            foreach (SubEmitter entry in driver.SubEmitters.Emitters)
                if (ReferenceEquals(entry.System, this))
                    return true;
            return false;
        }
    }

    private void StartPlayback()
    {
        ClaimSubEmitters();
        _isPlaying = true;
        _isPaused = false;
        _isEmitting = !IsDriven;
        _time = 0f;
        _catchUp = 0f;
        _culledTime = 0f;
        _hasEmitterHistory = false;

        if (!AutoRandomSeed)
            _random = new Random(unchecked((int)RandomSeed));

        Emission.Reset();
        bool prewarm = Prewarm && Looping;
        _delay = prewarm ? 0f : MathF.Max(0f, StartDelay.Evaluate(0f, _random));

        if (prewarm)
        {
            _prewarming = true;
            StepTogether([this], EffectiveDuration, true);
            _prewarming = false;
        }
    }

    /// <summary>Advances <paramref name="systems"/> side by side in thirtieths of a second. <paramref name="stayStill"/> ignores emitter motion.</summary>
    private static void StepTogether(ReadOnlySpan<ParticleSystemComponent> systems, float time, bool stayStill)
    {
        const float step = 1f / 30f;
        for (float done = 0f; done < time - 1e-6f; done += step)
        {
            float dt = MathF.Min(step, time - done);
            foreach (ParticleSystemComponent system in systems)
            {
                if (stayStill) system._hasEmitterHistory = false;
                system.Advance(dt);
            }
        }
    }

    private void ClearParticles()
    {
        _count = 0;
        _simTime = 0.0;
        _hasBounds = false;
        _collisionEvents.Clear();
        _subEmitQueue.Clear();
        Trails.Clear();
    }

    /// <summary>Stops once emission is over and nothing is left alive, then runs the stop action.</summary>
    private void CheckFinished()
    {
        if (!_isPlaying || _isEmitting) return;
        if (_count > 0 || Trails.HasOrphans) return;

        bool driven = IsDriven;
        if (!driven && SubEmitters.Enabled)
            foreach (SubEmitter entry in SubEmitters.Emitters)
                if (entry.System.IsValid() && !ReferenceEquals(entry.System, this) && entry.System.ParticleCount > 0)
                    return;

        _isPlaying = false;
        _isPaused = false;

        // A driven system goes quiet once drained and is woken by its driver. It has not finished, so it
        // raises nothing and never runs its stop action.
        if (driven) return;

        try { Stopped?.Invoke(this); }
        catch (Exception ex) { Debug.LogError($"[{Name}] Stopped handler threw: {ex.Message}\n{ex.StackTrace}"); }

        // A handler that played the system again has taken it over, so the stop action no longer applies.
        if (_isPlaying || !Application.IsPlaying || _prewarming) return;

        switch (StopAction)
        {
            case ParticleStopAction.Disable:
                GameObject.Enabled = false;
                break;
            case ParticleStopAction.Destroy:
                GameObject.Destroy();
                break;
        }
    }

    /// <summary>The topmost particle system in this one's parent chain. Selecting any system previews its whole tree.</summary>
    private ParticleSystemComponent PreviewRoot()
    {
        ParticleSystemComponent root = this;
        for (GameObject? go = GameObject.Parent; go.IsValid(); go = go.Parent)
        {
            ParticleSystemComponent? system = go.GetComponent<ParticleSystemComponent>();
            if (system.IsValid())
                root = system;
        }
        return root;
    }

    /// <summary>
    /// Edit mode simulation only runs while the system is selected, so an open scene does not burn time
    /// on every effect in it. Returns whether to simulate this frame.
    /// </summary>
    private bool UpdateEditorPreview()
    {
        bool selected = PreviewRoot()._selectedFrame >= Time.FrameCount - 1 || (IsDriven && _driver!._previewActive);
        if (!selected)
        {
            if (_previewActive)
            {
                _previewActive = false;
                _previewHeld = false;
                _isPlaying = false;
                _isPaused = false;
                _isEmitting = false;
                ClearParticles();
            }
            return false;
        }

        if (!_previewActive)
        {
            _previewActive = true;
            _previewIdle = 0f;
            if (!_isPlaying)
                Play(false);
        }
        else if (!_isPlaying && !_previewHeld && !IsDriven)
        {
            // A system that finished on its own plays again after a moment, so a one shot effect keeps
            // previewing. One the user stopped stays stopped.
            _previewIdle += Time.UnscaledDeltaTime;
            if (_previewIdle >= PreviewRestartDelay)
            {
                _previewIdle = 0f;
                Restart(false);
            }
        }
        return true;
    }

    private IEnumerable<ParticleSystemComponent> Children()
    {
        if (GameObject.IsNotValid()) yield break;
        foreach (ParticleSystemComponent system in GameObject.GetComponentsInChildren<ParticleSystemComponent>(false))
            if (!ReferenceEquals(system, this))
                yield return system;
    }

    #endregion

    #region Simulation

    /// <summary>Simulates one frame of <paramref name="dt"/> seconds, split into steps no longer than <see cref="MaxStep"/>.</summary>
    private void Advance(float dt)
    {
        if (dt <= 0f) return;

        ApplyMaxParticles();
        UpdateEmitterMotion(dt);
        ClaimSubEmitters();
        _collisionEvents.Clear();
        Scene? scene = Scene;
        _physics = scene.IsValid() ? scene.Physics : null;
        if (Wind.Enabled) Wind.BeginStep(this);

        _frameDt = dt;
        float done = 0f;
        while (done < dt - 1e-7f)
        {
            float step = MathF.Min(MaxStep, dt - done);
            _stepStart = done;
            Step(step);
            done += step;
        }

        FlushSubEmitters();
        DispatchCollisionEvents();
        RecalculateBounds();
        CheckFinished();
    }

    private void Step(float dt)
    {
        _stepDt = dt;
        _simTime += dt;

        if (Trails.ResetIfResized())
            for (int i = 0; i < _count; i++)
                _particles[i].TrailSlot = 0;

        int index = 0;
        while (index < _count)
        {
            if (UpdateParticle(ref _particles[index], dt))
                index++;
            else
                Kill(index);
        }

        Trails.UpdateOrphans(_simTime);
        AdvanceTime(dt);
    }

    /// <summary>Moves the loop clock forward and emits for the time that passed, looping or ending as it goes.</summary>
    private void AdvanceTime(float dt)
    {
        float remaining = dt;
        if (_delay > 0f)
        {
            float waited = MathF.Min(_delay, remaining);
            _delay -= waited;
            remaining -= waited;
        }

        float duration = EffectiveDuration;
        bool emits = Emission.Enabled && !IsDriven;
        int guard = 0;
        while (remaining > 1e-7f && _isEmitting && guard++ < 1000)
        {
            float span = MathF.Max(0f, MathF.Min(remaining, duration - _time));
            if (span > 0f && emits)
            {
                float distance = _frameDt > 0f ? _emitterMoveDistance * (span / _frameDt) : 0f;
                Emission.Emit(this, _time, _time + span, duration, distance, dt - remaining, _random);
            }
            _time += span;
            remaining -= span;

            if (_time >= duration - 1e-6f)
            {
                if (Looping)
                {
                    _time = 0f;
                    Emission.ResetBursts();
                }
                else
                {
                    _time = duration;
                    _isEmitting = false;
                }
            }
        }
    }

    /// <summary>
    /// Emits <paramref name="count"/> particles spread evenly between two moments of the current step,
    /// given in seconds from its start. Each one is simulated for the rest of the step on its own, so a
    /// long frame produces a smooth stream rather than a clump.
    /// </summary>
    internal void SpawnBatch(int count, float first, float last)
    {
        float time01 = _time / EffectiveDuration;
        for (int k = 0; k < count && _count < MaxParticles; k++)
        {
            float offset = count == 1 ? first : Maths.LerpUnclamped(first, last, k / (float)(count - 1));
            offset = Maths.Clamp(offset, 0f, _stepDt);

            // Where the emitter was at that moment, for systems it moves through.
            float framePoint = _frameDt > 0f ? (_stepStart + offset) / _frameDt : 1f;
            Shape.Sample(_random, out Float3 localPosition, out Float3 localDirection);
            Float3 position = Point(_emitterToSim, localPosition) - _emitterMoveSim * (1f - framePoint);
            Float3 direction = Float3.NormalizeSafe(Vector(_emitterToSim, localDirection), Float3.UnitY);

            ref Particle p = ref AddParticle();
            InitParticle(ref p, position, direction, time01, _emitterVelocitySim);
            FinishSpawn(ref p);

            float age = _stepDt - offset;
            if (age > 0f && !UpdateParticle(ref p, age))
                Kill(_count - 1);
        }
    }

    /// <summary>Takes the next free slot. Callers check <see cref="MaxParticles"/> first, storage grows on demand.</summary>
    private ref Particle AddParticle()
    {
        if (_count >= _particles.Length)
            Array.Resize(ref _particles, Math.Min(Math.Max(0, MaxParticles), Math.Max(64, _particles.Length * 2)));
        ref Particle p = ref _particles[_count++];
        p = default;
        p.RandomSeed = (uint)_random.NextInt64(0, uint.MaxValue);
        return ref p;
    }

    private void InitParticle(ref Particle p, Float3 position, Float3 direction, float time01, Float3 emitterVelocity)
    {
        p.Position = position;
        Initial.Apply(ref p, time01, _random);
        p.Velocity = direction * Initial.Speed(time01, _random);

        if (Shape.Enabled && Shape.AlignToDirection && UsesFullRotation)
        {
            Quaternion facing = Quaternion.LookRotation(direction, MathF.Abs(direction.Y) > 0.99f ? Float3.UnitZ : Float3.UnitY);
            p.Rotation = Quaternion.ToEuler(facing * Quaternion.FromEuler(p.Rotation));
        }

        if (InheritVelocity.Enabled)
            p.Velocity += InheritVelocity.SpawnVelocity(in p, emitterVelocity, time01);

        if (TextureSheet.Enabled) TextureSheet.Apply(ref p, 0f, Float3.Length(p.Velocity));
        if (CustomData.Enabled) CustomData.Apply(ref p, 0f);
    }

    /// <summary>Last step of every spawn, after any overrides have settled the particle's lifetime.</summary>
    private void FinishSpawn(ref Particle p)
    {
        if (Trails.Enabled) p.TrailSlot = Trails.Allocate(in p);
    }

    /// <summary>Whether particles show all three rotation axes, rather than only a roll around the view.</summary>
    private bool UsesFullRotation => Renderer.RenderMode == ParticleRenderMode.Mesh
        || (Renderer.RenderMode == ParticleRenderMode.Billboard && Renderer.Alignment is ParticleRenderAlignment.World or ParticleRenderAlignment.Local);

    private bool UpdateParticle(ref Particle p, float dt)
    {
        p.Lifetime -= dt;
        if (p.Lifetime <= 0f)
            return false;

        float age = p.NormalizedAge;

        if (Initial.GravityModifier != 0f)
            p.Velocity += _gravitySim * (Initial.GravityModifier * dt);
        if (ForceOverLifetime.Enabled) ForceOverLifetime.Apply(this, ref p, age, dt, _random);
        if (Wind.Enabled) Wind.Apply(this, ref p, dt);
        if (LimitVelocityOverLifetime.Enabled) LimitVelocityOverLifetime.Apply(ref p, age, dt);

        p.AnimatedVelocity = Float3.Zero;
        float speedModifier = 1f;
        if (VelocityOverLifetime.Enabled)
        {
            VelocityOverLifetime.Apply(this, ref p, age, dt);
            speedModifier = VelocityOverLifetime.Speed(in p, age);
        }
        if (InheritVelocity.Enabled) InheritVelocity.Apply(this, ref p, age);

        Float3 previous = p.Position;
        p.Position += p.TotalVelocity * (speedModifier * dt);

        if (Collision.Enabled && Collision.Apply(this, _physics, ref p, previous, out ParticleCollisionEvent hit))
            OnCollision(in p, in hit);

        float speed = Float3.Length(p.TotalVelocity);

        Float3 spin = p.AngularVelocity;
        if (RotationOverLifetime.Enabled) spin += RotationOverLifetime.Evaluate(in p, age);
        if (RotationBySpeed.Enabled) spin += RotationBySpeed.Evaluate(in p, speed);
        if (spin != Float3.Zero)
            p.Rotation += spin * (Initial.RotationSign(in p) * dt);

        Float3 size = p.StartSize;
        if (SizeOverLifetime.Enabled) size *= SizeOverLifetime.Evaluate(in p, age);
        if (SizeBySpeed.Enabled) size *= SizeBySpeed.Evaluate(in p, speed);
        p.Size = size;

        Color color = p.StartColor;
        if (ColorOverLifetime.Enabled) color *= ColorOverLifetime.Evaluate(in p, age);
        if (ColorBySpeed.Enabled) color *= ColorBySpeed.Evaluate(in p, speed);
        p.Color = color;

        if (TextureSheet.Enabled) TextureSheet.Apply(ref p, age, speed);
        if (CustomData.Enabled) CustomData.Apply(ref p, age);
        if (p.TrailSlot != 0) Trails.Record(this, in p, _simTime);
        if (_hasBirthSubEmitters) QueueBirth(in p, previous, dt);

        return p.Lifetime > 0f;
    }

    private void Kill(int index)
    {
        ref Particle p = ref _particles[index];
        if (p.TrailSlot != 0)
            Trails.Release(this, in p, _simTime);
        if (_hasDeathSubEmitters)
            QueueSubEmitters(SubEmitterType.Death, SimPointToWorld(p.Position), in p);

        _particles[index] = _particles[--_count];
    }

    /// <summary>Trims to a lowered <see cref="MaxParticles"/>, releasing what the dropped particles held.</summary>
    private void ApplyMaxParticles()
    {
        int max = Math.Max(0, MaxParticles);
        while (_count > max)
        {
            ref Particle p = ref _particles[--_count];
            if (p.TrailSlot != 0)
                Trails.Release(this, in p, _simTime);
        }
        if (_particles.Length > max)
            Array.Resize(ref _particles, max);
    }

    private void RecalculateBounds()
    {
        if (_count == 0)
        {
            _hasBounds = false;
            return;
        }

        Float3 min = new(float.MaxValue);
        Float3 max = new(float.MinValue);
        float largest = 0f;
        float fastest = 0f;
        for (int i = 0; i < _count; i++)
        {
            ref readonly Particle p = ref _particles[i];
            min = Maths.Min(min, p.Position);
            max = Maths.Max(max, p.Position);
            largest = MathF.Max(largest, MathF.Max(MathF.Abs(p.Size.X), MathF.Max(MathF.Abs(p.Size.Y), MathF.Abs(p.Size.Z))));
            if (Renderer.RenderMode == ParticleRenderMode.StretchedBillboard)
                fastest = MathF.Max(fastest, Float3.LengthSquared(p.TotalVelocity));
        }

        // A rotated quad reaches half its diagonal from the center, a mesh reaches its farthest corner,
        // and a pivot pushes either further out.
        float reach = 0.7072f;
        if (Renderer.RenderMode == ParticleRenderMode.Mesh && Renderer.Mesh.IsValid())
        {
            AABB mesh = Renderer.Mesh.bounds;
            reach = Float3.Length(Maths.Max(Maths.Abs(mesh.Min), Maths.Abs(mesh.Max)));
        }
        float pivot = 1f + 2f * MathF.Max(MathF.Abs(Renderer.Pivot.X), MathF.Max(MathF.Abs(Renderer.Pivot.Y), MathF.Abs(Renderer.Pivot.Z)));
        float pad = largest * _sizeScale * reach * pivot;
        if (Renderer.RenderMode == ParticleRenderMode.StretchedBillboard)
            pad = pad * MathF.Max(1f, Renderer.LengthScale) + MathF.Sqrt(fastest) * MathF.Abs(Renderer.VelocityScale) * 0.5f;

        _simBoundsMin = min;
        _simBoundsMax = max;
        _boundsPad = pad;
        _hasBounds = true;
        UpdateWorldBounds();
    }

    /// <summary>Places the simulation space bounds in the world. Rerun whenever the space moves, so a paused local system culls where it is now.</summary>
    private void UpdateWorldBounds()
    {
        if (!_hasBounds) return;

        AABB box = new(_simBoundsMin, _simBoundsMax);
        if (!_simIsWorld)
            box = box.TransformBy(_simToWorld);
        _worldBounds = new AABB(box.Min - new Float3(_boundsPad), box.Max + new Float3(_boundsPad));
    }

    #endregion

    #region Spaces

    /// <summary>
    /// Recomputes the space matrices from the transform, before simulating and before drawing so local
    /// particles never lag a transform that moved late in the frame. Changing the simulation space carries
    /// live particles over so they stay put in the world.
    /// </summary>
    internal void RefreshSpaces()
    {
        Transform t = Transform;
        Float3 position = t.Position;
        Quaternion rotation = t.Rotation;
        Float3 scale = ScalingMode == ParticleScalingMode.Local ? t.LocalScale : t.LossyScale;

        _emitterToWorld = ScalingMode == ParticleScalingMode.Hierarchy
            ? t.LocalToWorldMatrix
            : Float4x4.CreateTRS(position, rotation, scale);

        Float4x4 previousSimToWorld = _simToWorld;
        GameObject? custom = SimulationSpace == SimulationSpace.Custom && CustomSimulationSpace.IsValid() ? CustomSimulationSpace : null;
        var key = (SimulationSpace, ScalingMode, custom);

        // The transform the simulation space follows, with the scale it uses. Null for world space.
        Transform? space = SimulationSpace == SimulationSpace.Local ? t : custom.IsValid() ? custom.Transform : null;
        Float3 spaceScale = custom != null ? space!.LossyScale : ScalingMode == ParticleScalingMode.Shape ? Float3.One : scale;

        _simIsWorld = space == null;
        _simToWorld = _simIsWorld ? Float4x4.Identity
            : custom != null || ScalingMode == ParticleScalingMode.Hierarchy ? space!.LocalToWorldMatrix
            : Float4x4.CreateTRS(position, rotation, spaceScale);

        if (_simIsWorld)
        {
            _worldToSim = Float4x4.Identity;
        }
        else if (Float4x4.Invert(_simToWorld, out Float4x4 inverse) && IsFinite(inverse))
        {
            _worldToSim = inverse;
        }
        else
        {
            // A zero scale leaves nothing to invert. Squash to a tiny scale instead, so particles keep
            // finite values and come back intact when the scale does.
            _simToWorld = Float4x4.CreateTRS(space!.Position, space.Rotation, SafeScale(spaceScale));
            _worldToSim = _simToWorld.Invert();
        }

        _emitterToSim = _worldToSim * _emitterToWorld;
        _emitterOriginSim = WorldPointToSim(position);
        _sizeScale = ScalingMode == ParticleScalingMode.Shape
            ? 1f
            : (MathF.Abs(scale.X) + MathF.Abs(scale.Y) + MathF.Abs(scale.Z)) / 3f;

        if (_spacesValid && key != _spaceKey)
        {
            if (_count > 0 || Trails.HasOrphans)
                MigrateParticles(previousSimToWorld);
            _hasEmitterHistory = false;
        }
        _spaceKey = key;
        _spacesValid = true;
        UpdateWorldBounds();
    }

    private static Float3 SafeScale(Float3 scale)
    {
        const float min = 1e-4f;
        static float Safe(float v) => MathF.Abs(v) < min ? (v < 0f ? -min : min) : v;
        return new Float3(Safe(scale.X), Safe(scale.Y), Safe(scale.Z));
    }

    private static bool IsFinite(in Float4x4 m)
        => float.IsFinite(m.c0.X + m.c0.Y + m.c0.Z + m.c1.X + m.c1.Y + m.c1.Z + m.c2.X + m.c2.Y + m.c2.Z + m.c3.X + m.c3.Y + m.c3.Z);

    private void MigrateParticles(Float4x4 previousSimToWorld)
    {
        Float4x4 convert = _worldToSim * previousSimToWorld;

        // Full 3D rotations turn with the space. A billboard's roll is relative to the view and stays.
        bool turn = UsesFullRotation;
        Quaternion delta = Quaternion.Identity;
        if (turn)
        {
            Float3 x = Float3.NormalizeSafe(convert.c0.XYZ, Float3.UnitX);
            Float3 y = Float3.NormalizeSafe(convert.c1.XYZ, Float3.UnitY);
            Float3 z = Float3.NormalizeSafe(convert.c2.XYZ, Float3.UnitZ);
            delta = Quaternion.FromMatrix(new Float3x3(x, y, z));
        }

        for (int i = 0; i < _count; i++)
        {
            ref Particle p = ref _particles[i];
            p.Position = Point(convert, p.Position);
            p.Velocity = Vector(convert, p.Velocity);
            if (turn)
                p.Rotation = Quaternion.ToEuler(delta * Quaternion.FromEuler(p.Rotation));
            p.TrailSlot = 0;
        }
        Trails.Clear();
    }

    private void UpdateEmitterMotion(float dt)
    {
        RefreshSpaces();

        // Motion is measured in simulation space, so an emitter riding along with a custom space (an
        // engine on the ship it simulates in) is not moving at all as far as its particles can tell.
        Float3 position = Transform.Position;
        bool history = _hasEmitterHistory;
        Float3 movedWorld = history ? position - _previousEmitterPosition : Float3.Zero;
        Float3 movedSim = history ? _emitterOriginSim - _previousEmitterOriginSim : Float3.Zero;
        _previousEmitterPosition = position;
        _previousEmitterOriginSim = _emitterOriginSim;
        _hasEmitterHistory = true;

        Float3 velocitySim = dt > 0f ? movedSim / dt : Float3.Zero;
        if (EmitterVelocityMode == EmitterVelocityMode.Rigidbody)
        {
            Rigidbody3D? body = GetComponent<Rigidbody3D>();
            if (body.IsValid())
                velocitySim = WorldVectorToSim(body.LinearVelocity);
        }

        // A local space system carries its particles along already, so its own motion is not inherited.
        bool local = SimulationSpace == SimulationSpace.Local;
        _emitterVelocitySim = local ? Float3.Zero : velocitySim;
        _emitterMoveSim = local ? Float3.Zero : movedSim;

        // Rate over distance counts world units. A local system measures its travel through the world,
        // the others measure it through the space their particles live in.
        _emitterMoveDistance = Float3.Length(local ? movedWorld : SimVectorToWorld(movedSim));
        _gravitySim = WorldVectorToSim(new Float3(0f, -9.81f, 0f));
    }

    internal float SizeScale => _sizeScale;
    internal Float3 EmitterOriginSim => _emitterOriginSim;
    internal Float3 EmitterVelocitySim => _emitterVelocitySim;

    internal Float3 SimPointToWorld(Float3 point) => _simIsWorld ? point : Point(_simToWorld, point);
    internal Float3 WorldPointToSim(Float3 point) => _simIsWorld ? point : Point(_worldToSim, point);
    internal Float3 SimVectorToWorld(Float3 vector) => _simIsWorld ? vector : Vector(_simToWorld, vector);
    internal Float3 WorldVectorToSim(Float3 vector) => _simIsWorld ? vector : Vector(_worldToSim, vector);

    /// <summary>A vector along the emitter's own axes, in simulation space.</summary>
    internal Float3 LocalVectorToSim(Float3 vector)
        => SimulationSpace == SimulationSpace.Local ? vector : WorldVectorToSim(Transform.Rotation * vector);

    private static Float3 Point(in Float4x4 m, Float3 p) => (m * new Float4(p, 1f)).XYZ;
    private static Float3 Vector(in Float4x4 m, Float3 v) => (m * new Float4(v, 0f)).XYZ;

    #endregion

    #region Collisions and sub emitters

    private void OnCollision(in Particle p, in ParticleCollisionEvent hit)
    {
        if (!_prewarming && _collisionEvents.Count < MaxCollisionEvents)
            _collisionEvents.Add(hit);
        if (_hasCollisionSubEmitters)
            QueueSubEmitters(SubEmitterType.Collision, hit.Intersection, in p);
    }

    private void DispatchCollisionEvents()
    {
        if (_collisionEvents.Count == 0) return;

        for (int i = 0; i < _collisionEvents.Count; i++)
        {
            ParticleCollisionEvent hit = _collisionEvents[i];
            if (ParticleCollided != null)
            {
                try { ParticleCollided(this, hit); }
                catch (Exception ex) { Debug.LogError($"[{Name}] ParticleCollided handler threw: {ex.Message}\n{ex.StackTrace}"); }
            }

            if (!Collision.SendCollisionMessages || !Application.IsPlaying) continue;
            GameObject? other = hit.Other;
            if (other.IsNotValid()) continue;

            List<MonoBehaviour> components = other._components;
            for (int c = 0; c < components.Count; c++)
            {
                MonoBehaviour component = components[c];
                if (component is not IParticleCollisionHandler handler || !component.EnabledInHierarchy) continue;
                try { handler.OnParticleCollision(this, in hit); }
                catch (Exception ex) { Debug.LogError($"[{component.GetType().Name}] OnParticleCollision threw: {ex.Message}\n{ex.StackTrace}"); }
            }
        }
    }

    /// <summary>
    /// Takes ownership of the systems this one drives, so they stop emitting on their own and only simulate
    /// what they are handed. A child that would drive this system back is skipped with a warning.
    /// </summary>
    private void ClaimSubEmitters()
    {
        _hasBirthSubEmitters = _hasCollisionSubEmitters = _hasDeathSubEmitters = false;
        if (!SubEmitters.Enabled) return;

        foreach (SubEmitter entry in SubEmitters.Emitters)
        {
            ParticleSystemComponent? child = entry.System;
            if (child.IsNotValid() || ReferenceEquals(child, this)) continue;

            if (IsDrivenBy(child))
            {
                Debug.LogWarningOnce($"particle_sub_emitter_cycle_{InstanceID}_{child.InstanceID}",
                    $"[{Name}] Sub emitter {child.Name} already drives this system, the loop is ignored.");
                continue;
            }

            child._driver = this;
            child._isEmitting = false;
            switch (entry.Type)
            {
                case SubEmitterType.Birth: _hasBirthSubEmitters = true; break;
                case SubEmitterType.Collision: _hasCollisionSubEmitters = true; break;
                case SubEmitterType.Death: _hasDeathSubEmitters = true; break;
            }
        }
    }

    /// <summary>True when <paramref name="system"/> drives this one, directly or through other systems.</summary>
    private bool IsDrivenBy(ParticleSystemComponent system)
    {
        ParticleSystemComponent? current = this;
        for (int depth = 0; depth < 64 && current.IsValid() && current.IsDriven; depth++)
        {
            current = current._driver;
            if (ReferenceEquals(current, system))
                return true;
        }
        return false;
    }

    /// <summary>Hands every system this one drives back to itself.</summary>
    private void ReleaseSubEmitters()
    {
        foreach (SubEmitter entry in SubEmitters.Emitters)
            if (entry.System.IsValid() && ReferenceEquals(entry.System._driver, this))
                entry.System._driver = null;
    }

    private void QueueSubEmitters(SubEmitterType type, Float3 worldPosition, in Particle p)
    {
        List<SubEmitter> emitters = SubEmitters.Emitters;
        for (int i = 0; i < emitters.Count; i++)
            if (emitters[i].Type == type)
                QueueSubEmit(i, -1, worldPosition, in p);
    }

    private void QueueSubEmit(int index, int count, Float3 worldPosition, in Particle p, Float3 previousWorld = default, float span = 0f)
    {
        SubEmitter entry = SubEmitters.Emitters[index];
        if (entry.System.IsNotValid() || ReferenceEquals(entry.System, this)) return;
        if (entry.Probability < 1f && _random.NextSingle() >= entry.Probability) return;

        _subEmitQueue.Add(new SubEmitRequest
        {
            Emitter = index,
            Count = count,
            Position = worldPosition,
            PreviousPosition = previousWorld,
            Span = span,
            Velocity = SimVectorToWorld(p.TotalVelocity),
            Color = p.Color,
            Size = p.Size,
            Rotation = p.Rotation,
            LifeLeft = 1f - p.NormalizedAge,
        });
    }

    // Birth sub emitters run at the child's own rates, sampled over this particle's life.
    private void QueueBirth(in Particle p, Float3 previous, float dt)
    {
        List<SubEmitter> emitters = SubEmitters.Emitters;
        float age = p.NormalizedAge;
        for (int i = 0; i < emitters.Count; i++)
        {
            SubEmitter entry = emitters[i];
            if (entry.Type != SubEmitterType.Birth || entry.System.IsNotValid() || ReferenceEquals(entry.System, this)) continue;

            EmissionModule emission = entry.System.Emission;
            if (!emission.Enabled) continue;
            float amount = MathF.Max(0f, emission.RateOverTime.Evaluate(age, p.Random((uint)(0x171 + i)))) * dt;
            float perUnit = emission.RateOverDistance.Evaluate(age, p.Random((uint)(0x181 + i)));
            if (perUnit > 0f)
                amount += perUnit * Float3.Length(SimVectorToWorld(p.Position - previous));

            int count = (int)amount;
            if (_random.NextSingle() < amount - count) count++;
            if (count > 0)
                QueueSubEmit(i, count, SimPointToWorld(p.Position), in p, SimPointToWorld(previous), dt);
        }
    }

    private void FlushSubEmitters()
    {
        for (int i = 0; i < _subEmitQueue.Count; i++)
        {
            SubEmitRequest request = _subEmitQueue[i];
            if ((uint)request.Emitter >= (uint)SubEmitters.Emitters.Count) continue;
            SubEmitter entry = SubEmitters.Emitters[request.Emitter];
            ParticleSystemComponent? target = entry.System;
            if (target.IsNotValid()) continue;

            target.EmitFromParent(in request, entry);
            if (!_subEmitTargets.Contains(target))
                _subEmitTargets.Add(target);
        }
        _subEmitQueue.Clear();

        // Bounds once per child, so what it was just handed is drawn this frame.
        foreach (ParticleSystemComponent target in _subEmitTargets)
            target.RecalculateBounds();
        _subEmitTargets.Clear();
    }

    /// <summary>Emits into this system on behalf of a parent particle, with the shape centered on it.</summary>
    private void EmitFromParent(in SubEmitRequest request, SubEmitter entry)
    {
        if (!EnabledInHierarchy) return;

        ApplyMaxParticles();
        // Once a frame, the child may have moved since it last simulated.
        if (!_spacesValid || _emitSpacesFrame != Time.FrameCount)
        {
            RefreshSpaces();
            _emitSpacesFrame = Time.FrameCount;
        }
        if (!_isPlaying)
        {
            _isPlaying = true;
            _isEmitting = false;
        }
        _isPaused = false;

        int count = request.Count >= 0 ? request.Count : BurstTotal();
        Float3 scale = ScalingMode == ParticleScalingMode.Local ? Transform.LocalScale : Transform.LossyScale;
        Quaternion rotation = Transform.Rotation;
        Float3 inherited = WorldVectorToSim(request.Velocity);
        float time01 = _time / EffectiveDuration;
        bool spread = request.Span > 0f && count > 0;

        for (int k = 0; k < count && _count < MaxParticles; k++)
        {
            // Birth emission is spread along the parent's path this step and aged to match, so a fast
            // parent leaves a stream rather than a bead at the end of every step.
            float along = spread ? (k + 1f) / count : 1f;
            Float3 at = spread ? Maths.LerpUnclamped(request.PreviousPosition, request.Position, along) : request.Position;
            Float4x4 emitterToSim = _worldToSim * Float4x4.CreateTRS(at, rotation, scale);

            Shape.Sample(_random, out Float3 localPosition, out Float3 localDirection);
            ref Particle p = ref AddParticle();
            InitParticle(ref p, Point(emitterToSim, localPosition),
                Float3.NormalizeSafe(Vector(emitterToSim, localDirection), Float3.UnitY), time01, inherited);
            p.InheritedVelocity = inherited;
            p.HasInheritedVelocity = true;

            if (entry.InheritColor) p.Color = p.StartColor *= request.Color;
            if (entry.InheritSize) p.Size = p.StartSize *= request.Size;
            if (entry.InheritRotation) p.Rotation += request.Rotation;
            if (entry.InheritLifetime) p.Lifetime = p.StartLifetime *= Maths.Saturate(request.LifeLeft);
            FinishSpawn(ref p);

            float age = spread ? request.Span * (1f - along) : 0f;
            if (age > 0f && !UpdateParticle(ref p, age))
                Kill(_count - 1);
        }
    }

    /// <summary>What a burst style sub emitter fires: one cycle of every burst, or a single particle when there are none.</summary>
    private int BurstTotal()
    {
        if (!Emission.Enabled || Emission.Bursts.Count == 0) return 1;

        long total = 0;
        foreach (ParticleBurst burst in Emission.Bursts)
            total += EmissionModule.RollBurst(burst, MaxParticles, _random);
        return (int)Math.Min(total, Math.Max(0, MaxParticles));
    }

    #endregion
}
