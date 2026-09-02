using Alice.Memory;
using Alice.Social;

namespace Alice.Cognition;

public static class InviteResponseDecisionAdmission
{
    public static L2InviteResponseContext Admit(
        DecisionNeedStore store,
        DecisionNeed need,
        RoutineSemanticResponseContext responseContext,
        ActorDecisionView actorView,
        MemoryPacket packet)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(need);
        ArgumentNullException.ThrowIfNull(responseContext);
        ArgumentNullException.ThrowIfNull(actorView);
        ArgumentNullException.ThrowIfNull(packet);
        if (need.State != DecisionNeedState.Queued)
        {
            throw new InvalidOperationException("Invite response admission requires the exact Queued Decision Need.");
        }

        if (!store.IsCurrentMandatoryResponse(need)
            || !need.MatchesMandatoryResponseContext(responseContext)
            || need.MandatoryResponseSubject is null)
        {
            throw new ArgumentException("Invite response admission requires the exact live mandatory-response subject.", nameof(need));
        }

        if (actorView.ActorId != need.NpcId || packet.CandidateSet.ActorId != need.NpcId)
        {
            throw new ArgumentException("Need, actor decision view and MemoryPacket must identify the same actor.");
        }

        int admittedAttempt = checked(need.AttemptCount + 1);
        byte[] shared = L2PlanningContextCanonicalJson.SerializeInviteResponseShared(actorView, need.ProblemDescriptor);
        byte[] visible = L2PlanningContextCanonicalJson.SerializeModelVisible(shared, packet.GetModelVisibleBytes());
        MandatoryResponseDecisionSubject subject = need.MandatoryResponseSubject;
        var binding = new InviteResponseDecisionSubjectBinding(
            subject.ActorId,
            subject.SessionId,
            subject.OpportunityId,
            subject.SourceActId);
        var context = new L2InviteResponseContext(
            need,
            actorView,
            packet,
            admittedAttempt,
            binding,
            shared,
            visible);

        need.BeginInFlightAttempt();
        return context;
    }
}
