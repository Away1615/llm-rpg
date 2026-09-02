using Alice.Memory;
using Alice.Npc;

namespace Alice.Cognition;

public static class PlanlessStrategicDecisionAdmission
{
    public static L2PlanlessStrategicContext Admit(
        DecisionNeedStore store,
        DecisionNeed need,
        ActorDecisionView actorView,
        NpcPlanningState planningSnapshot,
        MemoryPacket packet)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(need);
        ArgumentNullException.ThrowIfNull(actorView);
        ArgumentNullException.ThrowIfNull(planningSnapshot);
        ArgumentNullException.ThrowIfNull(packet);
        if (need.State != DecisionNeedState.Queued)
        {
            throw new InvalidOperationException("Planless strategic admission requires the exact Queued Decision Need.");
        }

        if (!store.IsCurrentPlanlessStrategic(need)
            || need.PlanId is not null
            || need.PlanStepId is not null
            || need.ProblemDescriptor is not PlanlessStrategicDecisionProblemDescriptor descriptor)
        {
            throw new ArgumentException("Planless strategic admission requires the exact current Store subject.", nameof(need));
        }

        if (actorView.ActorId != need.NpcId
            || packet.CandidateSet.ActorId != need.NpcId
            || descriptor.ActorId != need.NpcId)
        {
            throw new ArgumentException("Need, actor decision view and MemoryPacket must identify the same actor.");
        }

        if (actorView.CurrentPlan is not null
            || actorView.CurrentStep is not null
            || planningSnapshot.CurrentPlan is not null
            || actorView.ActiveGoals.Count == 0
            || !actorView.ActiveGoals.SequenceEqual(planningSnapshot.ActiveGoals)
            || !actorView.ActiveGoals.SequenceEqual(descriptor.ActiveGoals))
        {
            throw new ArgumentException("Admission requires one exact planless actor view and equal non-empty Goal snapshots.");
        }

        int admittedAttempt = checked(need.AttemptCount + 1);
        byte[] shared = L2PlanningContextCanonicalJson.SerializePlanlessStrategicShared(actorView, descriptor);
        byte[] visible = L2PlanningContextCanonicalJson.SerializeModelVisible(
            shared,
            packet.GetModelVisibleBytes());
        var context = new L2PlanlessStrategicContext(
            need,
            actorView,
            planningSnapshot,
            packet,
            admittedAttempt,
            shared,
            visible);

        need.BeginInFlightAttempt();
        return context;
    }
}
