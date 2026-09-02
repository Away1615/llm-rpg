using Alice.Actors;

namespace Alice.Npc;

/// <summary>Incomplete NPC-only long-lived composition spine joined to Shared Actor state.</summary>
public sealed class NpcState : IEquatable<NpcState>
{
    public NpcState(ActorId actorId, NpcPersonalityState personality, NpcKnowledgeState knowledge, NpcPlanningState planning)
        : this(actorId, personality, knowledge, planning, new NpcSocialState(actorId, []))
    {
    }

    public NpcState(
        ActorId actorId,
        NpcPersonalityState personality,
        NpcKnowledgeState knowledge,
        NpcPlanningState planning,
        NpcSocialState social)
    {
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(personality);
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(planning);
        ArgumentNullException.ThrowIfNull(social);
        if (planning.CurrentPlan is not null && planning.CurrentPlan.ActorId != actorId)
        {
            throw new ArgumentException("Current plan must belong to the NPC actor.", nameof(planning));
        }

        if (social.ActorId != actorId)
        {
            throw new ArgumentException("Social state must belong to the NPC actor.", nameof(social));
        }

        ActorId = actorId;
        Personality = personality;
        Knowledge = knowledge;
        Planning = planning;
        Social = social;
    }

    public ActorId ActorId { get; }
    public NpcPersonalityState Personality { get; }
    public NpcKnowledgeState Knowledge { get; }
    public NpcPlanningState Planning { get; }
    public NpcSocialState Social { get; }

    public bool Equals(NpcState? other) => other is not null && ActorId == other.ActorId && Personality.Equals(other.Personality) && Knowledge.Equals(other.Knowledge) && Planning.Equals(other.Planning) && Social.Equals(other.Social);
    public override bool Equals(object? obj) => Equals(obj as NpcState);
    public override int GetHashCode() => HashCode.Combine(ActorId, Personality, Knowledge, Planning, Social);
}
