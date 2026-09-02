using System.Text.Json;

namespace Alice.Cognition;

public enum FormalTerminalOutcomeReceiptKind
{
    AuthorityCommit,
    ValidatedDefer,
    ValidatorRejection,
    TransportFailure
}

/// <summary>
/// Sanitized terminal receipt issued only by in-assembly Validator/Authority adapters over their exact bytes.
/// External executors cannot manufacture a formal terminal from an expected-answer hash.
/// </summary>
public sealed class FormalTerminalOutcomeReceipt
{
    private readonly byte[] _canonicalBytes;
    private readonly byte[] _sourceReceiptBytes;

    private FormalTerminalOutcomeReceipt(
        FormalTerminalOutcomeReceiptKind kind,
        string actorId,
        string needId,
        string modelCallId,
        string? gameActionId,
        string? terminalEvidenceHash,
        byte[] sourceReceiptBytes)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        FormalExperimentCanonical.RequireIdentity(actorId, nameof(actorId));
        FormalExperimentCanonical.RequireIdentity(needId, nameof(needId));
        FormalExperimentCanonical.RequireIdentity(modelCallId, nameof(modelCallId));
        if (kind == FormalTerminalOutcomeReceiptKind.AuthorityCommit)
            FormalExperimentCanonical.RequireIdentity(
                gameActionId ?? throw new ArgumentNullException(nameof(gameActionId)),
                nameof(gameActionId));
        else if (gameActionId is not null)
            throw new ArgumentException("A validated defer cannot carry a GameActionId.", nameof(gameActionId));
        if (kind is FormalTerminalOutcomeReceiptKind.AuthorityCommit
                or FormalTerminalOutcomeReceiptKind.ValidatedDefer)
            FormalExperimentCanonical.ValidateSha256(
                terminalEvidenceHash ?? throw new ArgumentNullException(nameof(terminalEvidenceHash)),
                nameof(terminalEvidenceHash));
        else if (terminalEvidenceHash is not null)
            throw new ArgumentException("Rejected formal terminals cannot carry outcome truth.", nameof(terminalEvidenceHash));

        Kind = kind;
        ActorId = actorId;
        NeedId = needId;
        ModelCallId = modelCallId;
        GameActionId = gameActionId;
        if (sourceReceiptBytes.Length == 0)
            throw new ArgumentException("A formal terminal requires exact source receipt bytes.", nameof(sourceReceiptBytes));
        _sourceReceiptBytes = sourceReceiptBytes.ToArray();
        SourceReceiptHash = FormalExperimentCanonical.Hash(_sourceReceiptBytes);
        string? derivedTerminalEvidenceHash = kind is FormalTerminalOutcomeReceiptKind.AuthorityCommit
                or FormalTerminalOutcomeReceiptKind.ValidatedDefer
            ? SourceReceiptHash
            : null;
        if (!StringComparer.Ordinal.Equals(terminalEvidenceHash, derivedTerminalEvidenceHash))
            throw new ArgumentException(
                "Formal terminal truth must be derived from the exact Validator/Authority receipt bytes.",
                nameof(terminalEvidenceHash));
        TerminalEvidenceHash = derivedTerminalEvidenceHash;
        _canonicalBytes = Serialize();
        ReceiptHash = FormalExperimentCanonical.Hash(_canonicalBytes);
    }

    public FormalTerminalOutcomeReceiptKind Kind { get; }
    public string ActorId { get; }
    public string NeedId { get; }
    public string ModelCallId { get; }
    public string? GameActionId { get; }
    public string? TerminalEvidenceHash { get; }
    public string SourceReceiptHash { get; }
    public string ReceiptHash { get; }

    internal static FormalTerminalOutcomeReceipt FromAuthorityCommit(
        string actorId,
        string needId,
        string modelCallId,
        string gameActionId,
        ReadOnlySpan<byte> canonicalAuthorityReceipt) =>
        Create(
            FormalTerminalOutcomeReceiptKind.AuthorityCommit,
            actorId,
            needId,
            modelCallId,
            gameActionId,
            canonicalAuthorityReceipt);

    internal static FormalTerminalOutcomeReceipt FromValidatedDefer(
        string actorId,
        string needId,
        string modelCallId,
        ReadOnlySpan<byte> canonicalValidatorReceipt) =>
        Create(
            FormalTerminalOutcomeReceiptKind.ValidatedDefer,
            actorId,
            needId,
            modelCallId,
            null,
            canonicalValidatorReceipt);

    internal static FormalTerminalOutcomeReceipt FromValidatorRejection(
        string actorId,
        string needId,
        string modelCallId,
        ReadOnlySpan<byte> canonicalValidatorReceipt) =>
        Create(
            FormalTerminalOutcomeReceiptKind.ValidatorRejection,
            actorId,
            needId,
            modelCallId,
            null,
            canonicalValidatorReceipt);

    internal static FormalTerminalOutcomeReceipt FromTransportFailure(
        string actorId,
        string needId,
        string modelCallId,
        ReadOnlySpan<byte> canonicalTransportReceipt) =>
        Create(
            FormalTerminalOutcomeReceiptKind.TransportFailure,
            actorId,
            needId,
            modelCallId,
            null,
            canonicalTransportReceipt);

    public byte[] GetCanonicalBytes() => _canonicalBytes.ToArray();
    public byte[] GetSourceReceiptBytes() => _sourceReceiptBytes.ToArray();

    private static FormalTerminalOutcomeReceipt Create(
        FormalTerminalOutcomeReceiptKind kind,
        string actorId,
        string needId,
        string modelCallId,
        string? gameActionId,
        ReadOnlySpan<byte> sourceReceipt)
    {
        if (sourceReceipt.IsEmpty)
            throw new ArgumentException("A formal terminal requires exact source receipt bytes.", nameof(sourceReceipt));
        return new FormalTerminalOutcomeReceipt(
            kind,
            actorId,
            needId,
            modelCallId,
            gameActionId,
            kind is FormalTerminalOutcomeReceiptKind.AuthorityCommit
                    or FormalTerminalOutcomeReceiptKind.ValidatedDefer
                ? FormalExperimentCanonical.Hash(sourceReceipt)
                : null,
            sourceReceipt.ToArray());
    }

    private byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "alice.formal-terminal-outcome-receipt.v2");
            writer.WriteString("kind", Kind switch
            {
                FormalTerminalOutcomeReceiptKind.AuthorityCommit => "authority_commit",
                FormalTerminalOutcomeReceiptKind.ValidatedDefer => "validated_defer",
                FormalTerminalOutcomeReceiptKind.ValidatorRejection => "validator_rejection",
                FormalTerminalOutcomeReceiptKind.TransportFailure => "transport_failure",
                _ => throw new ArgumentOutOfRangeException()
            });
            writer.WriteString("actor_id", ActorId);
            writer.WriteString("need_id", NeedId);
            writer.WriteString("model_call_id", ModelCallId);
            writer.WriteString("game_action_id", GameActionId);
            writer.WriteString("terminal_evidence_hash", TerminalEvidenceHash);
            writer.WriteString("source_receipt_hash", SourceReceiptHash);
            writer.WriteBase64String("source_receipt", _sourceReceiptBytes);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }
}
