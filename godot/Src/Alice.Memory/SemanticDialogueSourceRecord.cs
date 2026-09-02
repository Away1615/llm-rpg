using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Alice.Activities;
using Alice.Actors;
using Alice.Social;

namespace Alice.Memory;

/// <summary>One immutable canonical source for an accepted semantic dialogue turn.</summary>
public sealed class SemanticDialogueSourceRecord
{
    public const string ProtocolVersion = "semantic-dialogue-source-v1";
    private const string IdentityProtocolVersion = "semantic-dialogue-source-id-v1";
    private readonly byte[] _canonicalBytes;
    private readonly ActorId _speakerActorId;
    private readonly ActorId[] _recipientActorIds;

    private SemanticDialogueSourceRecord(
        DecisionMemorySourceId sourceId,
        ConversationSessionId sessionId,
        SemanticDialogueActId actId,
        int turnSequence,
        SimTime occurredAt,
        ActorId speakerActorId,
        ActorId[] recipientActorIds,
        byte[] canonicalBytes)
    {
        SourceId = sourceId;
        SessionId = sessionId;
        ActId = actId;
        TurnSequence = turnSequence;
        OccurredAt = occurredAt;
        _speakerActorId = speakerActorId;
        _recipientActorIds = recipientActorIds;
        _canonicalBytes = canonicalBytes;
    }

    public DecisionMemorySourceId SourceId { get; }
    public ConversationSessionId SessionId { get; }
    public SemanticDialogueActId ActId { get; }
    public int TurnSequence { get; }
    public SimTime OccurredAt { get; }

    public byte[] GetCanonicalBytes() => (byte[])_canonicalBytes.Clone();

    internal static SemanticDialogueSourceRecord Create(
        ConversationSession session,
        SemanticDialogueTurn turn,
        SimTime occurredAt)
    {
        DecisionMemorySourceId sourceId = CreateSourceId(session.SessionId, turn.Act.ActId);
        byte[] canonicalBytes = CreateCanonicalBytes(sourceId, session.SessionId, turn, occurredAt);
        return new SemanticDialogueSourceRecord(
            sourceId,
            session.SessionId,
            turn.Act.ActId,
            turn.Sequence,
            occurredAt,
            turn.Act.Speaker,
            turn.Act.Recipients.ToArray(),
            canonicalBytes);
    }

    internal bool HasExactContent(SemanticDialogueSourceRecord other) =>
        SourceId == other.SourceId && _canonicalBytes.AsSpan().SequenceEqual(other._canonicalBytes);

    internal bool TryGetExpectedRole(ActorId actorId, out ActorExperienceRole role)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        if (_speakerActorId == actorId)
        {
            role = ActorExperienceRole.Caused;
            return true;
        }

        if (_recipientActorIds.Contains(actorId))
        {
            role = ActorExperienceRole.Received;
            return true;
        }

        role = default;
        return false;
    }

    private static DecisionMemorySourceId CreateSourceId(
        ConversationSessionId sessionId,
        SemanticDialogueActId actId)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", IdentityProtocolVersion);
            writer.WriteString("session_id", sessionId.Value);
            writer.WriteString("act_id", actId.Value);
            writer.WriteEndObject();
        }

        string digest = Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
        return new DecisionMemorySourceId(string.Concat("semantic-dialogue:", digest));
    }

    private static byte[] CreateCanonicalBytes(
        DecisionMemorySourceId sourceId,
        ConversationSessionId sessionId,
        SemanticDialogueTurn turn,
        SimTime occurredAt)
    {
        SemanticDialogueAct act = turn.Act;
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", ProtocolVersion);
            writer.WriteString("source_id", sourceId.Value);
            writer.WriteString("session_id", sessionId.Value);
            writer.WriteNumber("turn_sequence", turn.Sequence);
            writer.WriteNumber("sim_time_ticks", occurredAt.Ticks);
            writer.WriteString("act_id", act.ActId.Value);
            writer.WriteString("act_kind", GetActKindToken(act.Kind));
            writer.WriteString("speaker_actor_id", act.Speaker.Value);
            writer.WriteStartArray("recipient_actor_ids");
            foreach (var recipient in act.Recipients)
            {
                writer.WriteStringValue(recipient.Value);
            }
            writer.WriteEndArray();
            if (act.TopicRef is { } topicRef)
            {
                writer.WriteString("topic_ref", topicRef.Value);
            }
            else
            {
                writer.WriteNull("topic_ref");
            }

            writer.WriteStartArray("claims");
            foreach (DialogueClaimReference claim in act.ClaimReferences)
            {
                writer.WriteStartObject();
                writer.WriteString("epistemic_status", "received_claim");
                writer.WriteString("claim_ref", claim.ClaimRef.Value);
                writer.WriteString("provenance_ref", claim.ProvenanceRef.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            if (act.InvitePayload is { } invite)
            {
                writer.WriteStartObject("invite_payload");
                writer.WriteString("gathering_ref", invite.GatheringRef.Value);
                writer.WriteNumber("expected_gathering_revision", invite.ExpectedGatheringRevision);
                writer.WriteString("invited_actor_id", invite.InvitedActorId.Value);
                if (invite.BelievedAuthorizationRef is { } authorizationRef)
                {
                    writer.WriteString("believed_authorization_ref", authorizationRef.Value);
                }
                else
                {
                    writer.WriteNull("believed_authorization_ref");
                }
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNull("invite_payload");
            }

            writer.WriteString("response_expectation", GetResponseExpectationToken(act.ResponseExpectation));
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static string GetActKindToken(SemanticDialogueActKind kind) => kind switch
    {
        SemanticDialogueActKind.Ask => "ask",
        SemanticDialogueActKind.Inform => "inform",
        SemanticDialogueActKind.Clarify => "clarify",
        SemanticDialogueActKind.Request => "request",
        SemanticDialogueActKind.Offer => "offer",
        SemanticDialogueActKind.Recommend => "recommend",
        SemanticDialogueActKind.Invite => "invite",
        SemanticDialogueActKind.Accept => "accept",
        SemanticDialogueActKind.Decline => "decline",
        SemanticDialogueActKind.CounterOffer => "counter_offer",
        SemanticDialogueActKind.Warn => "warn",
        SemanticDialogueActKind.Apologize => "apologize",
        SemanticDialogueActKind.Thank => "thank",
        SemanticDialogueActKind.Complain => "complain",
        SemanticDialogueActKind.Tease => "tease",
        SemanticDialogueActKind.Comfort => "comfort",
        SemanticDialogueActKind.Congratulate => "congratulate",
        SemanticDialogueActKind.CasualComment => "casual_comment",
        SemanticDialogueActKind.ShareNews => "share_news",
        SemanticDialogueActKind.ShareGossip => "share_gossip",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string GetResponseExpectationToken(DialogueResponseExpectation expectation) => expectation switch
    {
        DialogueResponseExpectation.Required => "required",
        DialogueResponseExpectation.None => "none",
        _ => throw new ArgumentOutOfRangeException(nameof(expectation))
    };
}
