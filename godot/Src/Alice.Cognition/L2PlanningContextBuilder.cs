using System.Security.Cryptography;
using Alice.Memory;
using Alice.Npc;

namespace Alice.Cognition;

public static class L2PlanningContextBuilder
{
    public static L2PlanningContext Create(
        DecisionNeed need,
        ActorCognitionView view,
        NpcPlan currentPlan,
        MemoryPacket packet)
    {
        ArgumentNullException.ThrowIfNull(need);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(currentPlan);
        ArgumentNullException.ThrowIfNull(packet);
        ValidateCorrelation(need, view, currentPlan, packet);

        byte[] sharedBytes = L2PlanningContextCanonicalJson.SerializeShared(view, need.ProblemDescriptor);
        byte[] modelVisibleBytes = L2PlanningContextCanonicalJson.SerializeModelVisible(sharedBytes, packet.GetModelVisibleBytes());
        return new L2PlanningContext(
            view.ActorId,
            need.NeedId,
            need.Fingerprint,
            need.ProblemDescriptor.DescriptorHash,
            packet.CandidateSet.CandidateSetId,
            packet.Strategy,
            packet.TokenizerVersion,
            packet.ConsumedTokens,
            packet.UnspentTokens,
            new L2SharedContextId(Hash(sharedBytes)),
            new L2PlanningContextId(Hash(modelVisibleBytes)),
            new L2SourcePlanBinding(currentPlan, need.PlanStepId!),
            sharedBytes,
            modelVisibleBytes);
    }

    private static void ValidateCorrelation(
        DecisionNeed need,
        ActorCognitionView view,
        NpcPlan currentPlan,
        MemoryPacket packet)
    {
        if (need.State != DecisionNeedState.InFlight || need.AttemptCount <= 0)
            throw new InvalidOperationException("L2 PlanningContext requires an in-flight DecisionNeed with a positive attempt count.");
        if (need.NpcId != view.ActorId || packet.CandidateSet.ActorId != view.ActorId)
            throw new ArgumentException("DecisionNeed, cognition view and packet candidate set must identify the same ActorId.");
        if (need.ProblemDescriptor is not CurrentStepDecisionProblemDescriptor descriptor ||
            need.PlanId is null || need.PlanId != currentPlan.PlanId ||
            need.PlanStepId is null || need.PlanStepId != view.CurrentStep.PlanStepId ||
            currentPlan.ActorId != view.ActorId ||
            currentPlan.Goal != view.CurrentPlan.Goal ||
            !currentPlan.Steps.SequenceEqual(view.CurrentPlan.Steps) ||
            !currentPlan.Steps.Any(step => step.PlanStepId == need.PlanStepId && step == view.CurrentStep) ||
            descriptor.ActorId != view.ActorId ||
            descriptor.CurrentGoalId != view.CurrentPlan.Goal.GoalId ||
            descriptor.CurrentGoalObjective != view.CurrentPlan.Goal.Objective ||
            descriptor.PlanStepId != view.CurrentStep.PlanStepId ||
            descriptor.StepObjective != view.CurrentStep.Objective ||
            descriptor.Target != view.CurrentStep.Target ||
            descriptor.DesiredResult != view.CurrentStep.DesiredResult)
            throw new ArgumentException("DecisionNeed current-step descriptor must match the actor-visible current Goal and Step.");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
