using System.Globalization;
using System.Collections.ObjectModel;
using Alice.Activities;
using Alice.Actors;
using Alice.Interaction;
using Alice.World;

namespace Alice.Npc;



/// <summary>Deterministic identities derived injectively from the full window and opportunity identities.</summary>
public sealed record LifestylePlanIdentitySet
{
    private const string GoalPrefix = "lifestyle-experience-goal-v1:";
    private const string PlanPrefix = "lifestyle-experience-plan-v1:";
    private const string ReachStepPrefix = "lifestyle-reach-step-v1:";
    private const string ExperienceStepPrefix = "lifestyle-experience-step-v1:";

    private LifestylePlanIdentitySet(GoalId goalId, PlanId planId, PlanStepId reachStepId, PlanStepId experienceStepId)
    {
        GoalId = goalId;
        PlanId = planId;
        ReachStepId = reachStepId;
        ExperienceStepId = experienceStepId;
    }

    public GoalId GoalId { get; }
    public PlanId PlanId { get; }
    public PlanStepId ReachStepId { get; }
    public PlanStepId ExperienceStepId { get; }

    public static LifestylePlanIdentitySet Derive(FreeWindowId windowId, LifestyleOpportunityId opportunityId)
    {
        ArgumentNullException.ThrowIfNull(windowId);
        ArgumentNullException.ThrowIfNull(opportunityId);
        string encoded = Encode(windowId.Value) + Encode(opportunityId.Value);
        return new LifestylePlanIdentitySet(
            new GoalId(GoalPrefix + encoded),
            new PlanId(PlanPrefix + encoded),
            new PlanStepId(ReachStepPrefix + encoded),
            new PlanStepId(ExperienceStepPrefix + encoded));
    }

    private static string Encode(string value) =>
        string.Concat(value.Length.ToString(CultureInfo.InvariantCulture), ":", value);
}

/// <summary>Materializes the bounded immutable Reach then Experience Plan.</summary>
public static class LifestylePlanMaterializer
{
    public static NpcPlan Materialize(
        FreeWindowLifestyleContext context,
        FreeWindowLifestyleOpportunity opportunity)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(opportunity);
        if (opportunity.ActorId != context.ActorId ||
            !context.Opportunities.Any(value => value == opportunity))
        {
            throw new ArgumentException("Lifestyle opportunity must be an exact member of the actor-local Free-window snapshot.", nameof(opportunity));
        }

        LifestylePlanIdentitySet identities = LifestylePlanIdentitySet.Derive(context.WindowId, opportunity.OpportunityId);
        var experience = new ExperienceObjective(opportunity.ExperienceId);
        var goal = new NpcGoal(identities.GoalId, experience);
        var reach = new PlanStep(
            identities.ReachStepId,
            new ReachTargetObjective(opportunity.TargetRef),
            null,
            opportunity.TargetRef,
            new InteractionTargetReached(context.ActorId, opportunity.TargetRef, opportunity.InteractionRange));
        var performExperience = new PlanStep(
            identities.ExperienceStepId,
            experience,
            null,
            null,
            new ExperienceCompleted(context.ActorId, opportunity.ExperienceId));
        return new NpcPlan(identities.PlanId, context.ActorId, goal, 1, [reach, performExperience]);
    }
}


public enum AutonomousGoalGenerationDecisionKind
{
    NoCandidate,
    Candidate,
    GoalConstraintConflictRequired
}

public sealed record AutonomousGoalGenerationDecision
{
    public AutonomousGoalGenerationDecision(
        AutonomousGoalGenerationDecisionKind kind,
        FreeWindowLifestyleOpportunity? opportunity)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (kind == AutonomousGoalGenerationDecisionKind.Candidate != (opportunity is not null))
        {
            throw new ArgumentException("Only a candidate decision may contain one Lifestyle opportunity.", nameof(opportunity));
        }

        Kind = kind;
        Opportunity = opportunity;
    }

    public AutonomousGoalGenerationDecisionKind Kind { get; }
    public FreeWindowLifestyleOpportunity? Opportunity { get; }
}

/// <summary>Pure deterministic selection for the bounded fixture-local Lifestyle baseline.</summary>
public static class AutonomousGoalGenerator
{
    public static AutonomousGoalGenerationDecision Generate(
        FreeWindowLifestyleContext context,
        NpcPersonalityState personality)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(personality);
        if (context.CurrentTime.CompareTo(context.StartsAt) < 0 ||
            context.CurrentTime.CompareTo(context.EndsAt) >= 0)
        {
            return new AutonomousGoalGenerationDecision(AutonomousGoalGenerationDecisionKind.NoCandidate, null);
        }

        FreeWindowLifestyleOpportunity? candidate = null;
        foreach (FreeWindowLifestyleOpportunity opportunity in context.Opportunities)
        {
            if (!personality.Traits.Contains(opportunity.RequiredTrait))
            {
                continue;
            }

            if (candidate is not null)
            {
                return new AutonomousGoalGenerationDecision(
                    AutonomousGoalGenerationDecisionKind.GoalConstraintConflictRequired,
                    null);
            }

            candidate = opportunity;
        }

        return candidate is null
            ? new AutonomousGoalGenerationDecision(AutonomousGoalGenerationDecisionKind.NoCandidate, null)
            : new AutonomousGoalGenerationDecision(AutonomousGoalGenerationDecisionKind.Candidate, candidate);
    }
}



public sealed record FreeWindowId
{
    public FreeWindowId(string value)
    {
        LifestylePlanningIdentity.Validate(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
}

public sealed record LifestyleOpportunityId
{
    public LifestyleOpportunityId(string value)
    {
        LifestylePlanningIdentity.Validate(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
}

public sealed record ExperienceId
{
    public ExperienceId(string value)
    {
        LifestylePlanningIdentity.Validate(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
}

public sealed record FreeWindowLifestyleOpportunity
{
    public FreeWindowLifestyleOpportunity(
        LifestyleOpportunityId opportunityId,
        ActorId actorId,
        ExperienceId experienceId,
        PersonalityTagId requiredTrait,
        TargetRef targetRef,
        InteractionRange interactionRange)
    {
        ArgumentNullException.ThrowIfNull(opportunityId);
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(experienceId);
        ArgumentNullException.ThrowIfNull(requiredTrait);
        ArgumentNullException.ThrowIfNull(targetRef);
        if (interactionRange.Value <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(interactionRange), "Lifestyle opportunity range must be positive and finite.");
        }

        OpportunityId = opportunityId;
        ActorId = actorId;
        ExperienceId = experienceId;
        RequiredTrait = requiredTrait;
        TargetRef = targetRef;
        InteractionRange = interactionRange;
    }

    public LifestyleOpportunityId OpportunityId { get; }
    public ActorId ActorId { get; }
    public ExperienceId ExperienceId { get; }
    public PersonalityTagId RequiredTrait { get; }
    public TargetRef TargetRef { get; }
    public InteractionRange InteractionRange { get; }
}

/// <summary>Immutable actor-local fixture snapshot for one bounded Free window.</summary>
public sealed class FreeWindowLifestyleContext : IEquatable<FreeWindowLifestyleContext>
{
    private readonly ReadOnlyCollection<FreeWindowLifestyleOpportunity> _opportunities;

    public FreeWindowLifestyleContext(
        ActorId actorId,
        FreeWindowId windowId,
        SimTime startsAt,
        SimTime endsAt,
        SimTime currentTime,
        IEnumerable<FreeWindowLifestyleOpportunity> opportunities)
    {
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(windowId);
        ArgumentNullException.ThrowIfNull(opportunities);
        if (endsAt.CompareTo(startsAt) <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(endsAt), "Free window end must be after its start.");
        }

        FreeWindowLifestyleOpportunity[] snapshot = opportunities.ToArray();
        var opportunityIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (FreeWindowLifestyleOpportunity opportunity in snapshot)
        {
            ArgumentNullException.ThrowIfNull(opportunity);
            if (opportunity.ActorId != actorId)
            {
                throw new ArgumentException("Every Lifestyle opportunity must belong to the Free-window actor.", nameof(opportunities));
            }

            if (!opportunityIds.Add(opportunity.OpportunityId.Value))
            {
                throw new ArgumentException("Lifestyle opportunity identities must be unique.", nameof(opportunities));
            }
        }

        Array.Sort(snapshot, OpportunityComparer.Instance);
        ActorId = actorId;
        WindowId = windowId;
        StartsAt = startsAt;
        EndsAt = endsAt;
        CurrentTime = currentTime;
        _opportunities = Array.AsReadOnly(snapshot);
    }

    public ActorId ActorId { get; }
    public FreeWindowId WindowId { get; }
    public SimTime StartsAt { get; }
    public SimTime EndsAt { get; }
    public SimTime CurrentTime { get; }
    public IReadOnlyList<FreeWindowLifestyleOpportunity> Opportunities => _opportunities;

    public bool Equals(FreeWindowLifestyleContext? other) =>
        other is not null &&
        ActorId == other.ActorId &&
        WindowId == other.WindowId &&
        StartsAt == other.StartsAt &&
        EndsAt == other.EndsAt &&
        CurrentTime == other.CurrentTime &&
        Opportunities.SequenceEqual(other.Opportunities);

    public override bool Equals(object? obj) => Equals(obj as FreeWindowLifestyleContext);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ActorId);
        hash.Add(WindowId);
        hash.Add(StartsAt);
        hash.Add(EndsAt);
        hash.Add(CurrentTime);
        foreach (FreeWindowLifestyleOpportunity opportunity in Opportunities)
        {
            hash.Add(opportunity);
        }

        return hash.ToHashCode();
    }

    private sealed class OpportunityComparer : IComparer<FreeWindowLifestyleOpportunity>
    {
        public static OpportunityComparer Instance { get; } = new();

        public int Compare(FreeWindowLifestyleOpportunity? left, FreeWindowLifestyleOpportunity? right) =>
            StringComparer.Ordinal.Compare(left?.OpportunityId.Value, right?.OpportunityId.Value);
    }
}

internal static class LifestylePlanningIdentity
{
    public static void Validate(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Lifestyle planning identity must be non-empty.", parameterName);
        }
    }
}
