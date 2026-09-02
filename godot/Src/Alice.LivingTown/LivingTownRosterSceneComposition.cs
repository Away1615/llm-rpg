using System.Collections.ObjectModel;
using Alice.Actors;
using Alice.NpcExecution;
using Godot;

namespace Alice.LivingTown;

public interface ILivingTownActorSceneBinding : IDisposable
{
    INpcEntityProjectionPort ProjectionPort { get; }
}

public interface ILivingTownActorSceneFactory
{
    ILivingTownActorSceneBinding Create(LivingTownNpcProfile profile);
}

/// <summary>Owns exact manifest-Actor to scene-port bindings; it never owns semantic NPC state.</summary>
public sealed class LivingTownRosterSceneComposition : IDisposable
{
    private readonly ReadOnlyCollection<ILivingTownActorSceneBinding> _bindings;
    private bool _disposed;

    public LivingTownRosterSceneComposition(
        TownPopulationManifest manifest,
        LivingTownPopulationRuntime runtime,
        ILivingTownActorSceneFactory sceneFactory)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(sceneFactory);
        if (manifest.ManifestId != runtime.ManifestId
            || manifest.Actors.Count != runtime.Npcs.Count)
            throw new ArgumentException("Roster scene composition must exact-bind the active manifest runtime.", nameof(runtime));

        SceneRegistry = new ActorSceneRegistry();
        Projection = new NpcProjectionCoordinator(runtime, SceneRegistry);
        Lod = new TownLodCoordinator(Projection);
        var bindings = new List<ILivingTownActorSceneBinding>();
        try
        {
            foreach (LivingTownNpcRuntime npc in runtime.Npcs)
            {
                ILivingTownActorSceneBinding binding = sceneFactory.Create(npc.State.Profile)
                    ?? throw new InvalidOperationException("Living Town scene factory returned no Actor binding.");
                if (binding.ProjectionPort.ActorId != npc.ActorId)
                    throw new InvalidOperationException("Living Town scene factory cross-wired an Actor binding.");
                SceneRegistry.Register(binding.ProjectionPort);
                bindings.Add(binding);
            }
            Lod.SetProjectedActors(runtime.Npcs.Select(npc => npc.ActorId));
        }
        catch
        {
            DisposeBindings(bindings);
            throw;
        }
        _bindings = Array.AsReadOnly(bindings.ToArray());
    }

    public ActorSceneRegistry SceneRegistry { get; }
    public NpcProjectionCoordinator Projection { get; }
    public TownLodCoordinator Lod { get; }
    public IReadOnlyList<ILivingTownActorSceneBinding> Bindings => _bindings;

    public void RefreshAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (ActorId actorId in SceneRegistry.ActorIds)
        {
            if (!Projection.Project(actorId))
                throw new InvalidOperationException($"Failed to refresh Actor {actorId.Value} projection.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Lod.SetProjectedActors([]);
        foreach (ILivingTownActorSceneBinding binding in _bindings)
            SceneRegistry.Unregister(binding.ProjectionPort.ActorId, binding.ProjectionPort);
        DisposeBindings(_bindings);
    }

    private static void DisposeBindings(IEnumerable<ILivingTownActorSceneBinding> bindings)
    {
        foreach (ILivingTownActorSceneBinding binding in bindings.Reverse()) binding.Dispose();
    }
}

/// <summary>Godot scene adapter that instantiates the authored NpcEntity scene once per manifest Actor.</summary>
public sealed class GodotLivingTownActorSceneFactory : ILivingTownActorSceneFactory
{
    private readonly PackedScene _npcEntityScene;
    private readonly Node _parent;
    private readonly Alice.Navigation.RoadTravelSpeedProfile _travelSpeedProfile;

    public GodotLivingTownActorSceneFactory(
        PackedScene npcEntityScene,
        Node parent,
        Alice.Navigation.RoadTravelSpeedProfile travelSpeedProfile)
    {
        ArgumentNullException.ThrowIfNull(npcEntityScene);
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(travelSpeedProfile);
        _npcEntityScene = npcEntityScene;
        _parent = parent;
        _travelSpeedProfile = travelSpeedProfile;
    }

    public ILivingTownActorSceneBinding Create(LivingTownNpcProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        NpcEntity entity = _npcEntityScene.Instantiate<NpcEntity>();
        entity.Name = new StringName(profile.ActorId.Value);
        entity.ActorIdentity = profile.ActorId.Value;
        entity.DisplayName = profile.DisplayName;
        entity.StartProjectionActive = false;
        entity.ConfigureTravelSpeedProfile(_travelSpeedProfile);
        entity.ConfigureMarker(new Color(profile.Appearance.FillColor));
        entity.Position = new Vector2((float)profile.StartingPosition.X, (float)profile.StartingPosition.Y);
        _parent.AddChild(entity);
        return new GodotLivingTownActorSceneBinding(
            _parent,
            entity,
            new NpcEntitySceneProjectionPort(profile.ActorId, entity));
    }

    private sealed class GodotLivingTownActorSceneBinding : ILivingTownActorSceneBinding
    {
        private readonly Node _parent;
        private NpcEntity? _entity;

        public GodotLivingTownActorSceneBinding(
            Node parent,
            NpcEntity entity,
            INpcEntityProjectionPort projectionPort)
        {
            _parent = parent;
            _entity = entity;
            ProjectionPort = projectionPort;
        }

        public INpcEntityProjectionPort ProjectionPort { get; }

        public void Dispose()
        {
            NpcEntity? entity = _entity;
            if (entity is null) return;
            _entity = null;
            if (entity.GetParent() == _parent) _parent.RemoveChild(entity);
            entity.QueueFree();
        }
    }
}
