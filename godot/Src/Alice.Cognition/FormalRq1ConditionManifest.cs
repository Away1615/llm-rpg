using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using Alice.ModelRuntime;

namespace Alice.Cognition;

public sealed record FormalRq1PressureProfileManifestEntry
{
    public FormalRq1PressureProfileManifestEntry(
        PressureProfileId profileId,
        long profileVersion,
        string evaluatorContentHash)
    {
        if (profileVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(profileVersion));
        }

        ProfileId = profileId;
        ProfileVersion = profileVersion;
        EvaluatorContentHash = L2PlanningContextCanonicalJson.ValidateSha256(
            evaluatorContentHash,
            nameof(evaluatorContentHash));
    }

    public PressureProfileId ProfileId { get; }
    public long ProfileVersion { get; }
    public string EvaluatorContentHash { get; }
}

public sealed record FormalRq1PressureStateManifestEntry
{
    public FormalRq1PressureStateManifestEntry(
        PressureId pressureId,
        PressureProfileId profileId,
        long profileVersion,
        string evaluatorContentHash,
        string initialStateHash)
    {
        if (profileVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(profileVersion));
        }

        PressureId = pressureId;
        ProfileId = profileId;
        ProfileVersion = profileVersion;
        EvaluatorContentHash = L2PlanningContextCanonicalJson.ValidateSha256(
            evaluatorContentHash,
            nameof(evaluatorContentHash));
        InitialStateHash = L2PlanningContextCanonicalJson.ValidateSha256(
            initialStateHash,
            nameof(initialStateHash));
    }

    public PressureId PressureId { get; }
    public PressureProfileId ProfileId { get; }
    public long ProfileVersion { get; }
    public string EvaluatorContentHash { get; }
    public string InitialStateHash { get; }
}

public sealed class FormalRq1PressureManifest
{
    private readonly ReadOnlyCollection<FormalRq1PressureProfileManifestEntry> _profiles;
    private readonly ReadOnlyCollection<FormalRq1PressureStateManifestEntry> _states;

    public FormalRq1PressureManifest(
        string evaluatorHostVersion,
        string dependencyIndexVersion,
        string dependencyIndexContentHash,
        IEnumerable<FormalRq1PressureProfileManifestEntry> profiles,
        IEnumerable<FormalRq1PressureStateManifestEntry> states)
    {
        DependencyContractIdentity.Validate(evaluatorHostVersion, nameof(evaluatorHostVersion));
        DependencyContractIdentity.Validate(dependencyIndexVersion, nameof(dependencyIndexVersion));
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(states);
        FormalRq1PressureProfileManifestEntry[] snapshot = profiles.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(IsNullProfile))
        {
            throw new ArgumentException("A formal RQ1 Pressure manifest requires at least one non-null profile.", nameof(profiles));
        }

        Array.Sort(snapshot, ProfileComparer.Instance);
        for (int index = 1; index < snapshot.Length; index++)
        {
            if (snapshot[index - 1].ProfileId == snapshot[index].ProfileId)
            {
                throw new ArgumentException("Pressure manifest profiles must be unique by ProfileId.", nameof(profiles));
            }
        }

        EvaluatorHostVersion = evaluatorHostVersion;
        DependencyIndexVersion = dependencyIndexVersion;
        DependencyIndexContentHash = L2PlanningContextCanonicalJson.ValidateSha256(
            dependencyIndexContentHash,
            nameof(dependencyIndexContentHash));
        _profiles = Array.AsReadOnly(snapshot);
        FormalRq1PressureStateManifestEntry[] stateSnapshot = states.ToArray();
        if (stateSnapshot.Length == 0 || stateSnapshot.Any(IsNullState))
        {
            throw new ArgumentException("A formal RQ1 Pressure manifest requires at least one non-null state.", nameof(states));
        }

        Array.Sort(stateSnapshot, StateComparer.Instance);
        var profileById = snapshot.ToDictionary(profile => profile.ProfileId);
        for (int index = 0; index < stateSnapshot.Length; index++)
        {
            FormalRq1PressureStateManifestEntry state = stateSnapshot[index];
            if (index != 0 && stateSnapshot[index - 1].PressureId == state.PressureId)
            {
                throw new ArgumentException("Pressure manifest states must be unique by PressureId.", nameof(states));
            }

            if (!profileById.TryGetValue(state.ProfileId, out FormalRq1PressureProfileManifestEntry? profile)
                || profile.ProfileVersion != state.ProfileVersion
                || !StringComparer.Ordinal.Equals(profile.EvaluatorContentHash, state.EvaluatorContentHash))
            {
                throw new ArgumentException("Every Pressure state must bind one exact compiled profile.", nameof(states));
            }
        }

        _states = Array.AsReadOnly(stateSnapshot);
        ConfigurationHash = Hash(Serialize());
    }

    public string EvaluatorHostVersion { get; }
    public string DependencyIndexVersion { get; }
    public string DependencyIndexContentHash { get; }
    public IReadOnlyList<FormalRq1PressureProfileManifestEntry> Profiles => _profiles;
    public IReadOnlyList<FormalRq1PressureStateManifestEntry> States => _states;
    public string ConfigurationHash { get; }

    private static bool IsNullProfile(FormalRq1PressureProfileManifestEntry profile)
    {
        return profile is null;
    }

    private static bool IsNullState(FormalRq1PressureStateManifestEntry state)
    {
        return state is null;
    }

    private byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", "formal-rq1-pressure-manifest-v1");
            writer.WriteString("evaluator_host_version", EvaluatorHostVersion);
            writer.WriteString("dependency_index_version", DependencyIndexVersion);
            writer.WriteString("dependency_index_content_hash", DependencyIndexContentHash);
            writer.WritePropertyName("profiles");
            writer.WriteStartArray();
            foreach (FormalRq1PressureProfileManifestEntry profile in Profiles)
            {
                writer.WriteStartObject();
                writer.WriteString("profile_id", profile.ProfileId.Value);
                writer.WriteNumber("profile_version", profile.ProfileVersion);
                writer.WriteString("evaluator_content_hash", profile.EvaluatorContentHash);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("states");
            writer.WriteStartArray();
            foreach (FormalRq1PressureStateManifestEntry state in States)
            {
                writer.WriteStartObject();
                writer.WriteString("pressure_id", state.PressureId.Value);
                writer.WriteString("profile_id", state.ProfileId.Value);
                writer.WriteNumber("profile_version", state.ProfileVersion);
                writer.WriteString("evaluator_content_hash", state.EvaluatorContentHash);
                writer.WriteString("initial_state_hash", state.InitialStateHash);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static string Hash(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private sealed class ProfileComparer : IComparer<FormalRq1PressureProfileManifestEntry>
    {
        public static ProfileComparer Instance { get; } = new();

        public int Compare(
            FormalRq1PressureProfileManifestEntry? left,
            FormalRq1PressureProfileManifestEntry? right)
        {
            return StringComparer.Ordinal.Compare(left!.ProfileId.Value, right!.ProfileId.Value);
        }
    }

    private sealed class StateComparer : IComparer<FormalRq1PressureStateManifestEntry>
    {
        public static StateComparer Instance { get; } = new();

        public int Compare(FormalRq1PressureStateManifestEntry? left, FormalRq1PressureStateManifestEntry? right)
        {
            return StringComparer.Ordinal.Compare(left!.PressureId.Value, right!.PressureId.Value);
        }
    }
}

public sealed record FormalRq1RequestProtocolManifestEntry
{
    public FormalRq1RequestProtocolManifestEntry(
        RemotePlannerRequestKind requestKind,
        string protocolVersion,
        string actorVisibleContextBuilderVersion)
    {
        if (!Enum.IsDefined(requestKind)) throw new ArgumentOutOfRangeException(nameof(requestKind));
        DependencyContractIdentity.Validate(protocolVersion, nameof(protocolVersion));
        DependencyContractIdentity.Validate(actorVisibleContextBuilderVersion, nameof(actorVisibleContextBuilderVersion));
        RequestKind = requestKind;
        ProtocolVersion = protocolVersion;
        ActorVisibleContextBuilderVersion = actorVisibleContextBuilderVersion;
    }

    public RemotePlannerRequestKind RequestKind { get; }
    public string ProtocolVersion { get; }
    public string ActorVisibleContextBuilderVersion { get; }
}

public enum FormalRq1RunPurpose
{
    EngineeringEvidence,
    FormalCollection
}

/// <summary>Canonical fail-closed condition configuration. It contains no default research values.</summary>
public sealed class FormalRq1ConditionManifest
{
    public const string CurrentProtocolVersion = "formal-rq1-condition-manifest-v1";
    private readonly byte[] _canonicalBytes;
    private readonly byte[] _sharedCanonicalBytes;
    private readonly ReadOnlyCollection<FormalRq1RequestProtocolManifestEntry> _requestProtocols;

    public FormalRq1ConditionManifest(
        string manifestId,
        string preregistrationArtifactVersion,
        FormalRq1Treatment treatment,
        string runtimeVersion,
        string modelProfileId,
        IEnumerable<FormalRq1RequestProtocolManifestEntry> requestProtocols,
        string authorityProjectionBindingHash,
        string opportunityLedgerId,
        FormalRq1DispatchConfiguration dispatchConfiguration,
        FormalRq1PressureManifest pressureManifest)
    {
        DependencyContractIdentity.Validate(manifestId, nameof(manifestId));
        DependencyContractIdentity.Validate(preregistrationArtifactVersion, nameof(preregistrationArtifactVersion));
        if (!Enum.IsDefined(treatment))
        {
            throw new ArgumentOutOfRangeException(nameof(treatment));
        }

        DependencyContractIdentity.Validate(runtimeVersion, nameof(runtimeVersion));
        DependencyContractIdentity.Validate(modelProfileId, nameof(modelProfileId));
        ArgumentNullException.ThrowIfNull(requestProtocols);
        ArgumentNullException.ThrowIfNull(dispatchConfiguration);
        ArgumentNullException.ThrowIfNull(pressureManifest);
        ManifestId = manifestId;
        PreregistrationArtifactVersion = preregistrationArtifactVersion;
        Treatment = treatment;
        RuntimeVersion = runtimeVersion;
        ModelProfileId = modelProfileId;
        FormalRq1RequestProtocolManifestEntry[] protocolSnapshot = requestProtocols.ToArray();
        if (protocolSnapshot.Length == 0 || protocolSnapshot.Any(protocol => protocol is null))
        {
            throw new ArgumentException("At least one non-null request protocol is required.", nameof(requestProtocols));
        }

        Array.Sort(protocolSnapshot, RequestProtocolComparer.Instance);
        for (int index = 1; index < protocolSnapshot.Length; index++)
        {
            if (protocolSnapshot[index - 1].RequestKind == protocolSnapshot[index].RequestKind)
            {
                throw new ArgumentException("Request protocols must be unique by request kind.", nameof(requestProtocols));
            }
        }

        _requestProtocols = Array.AsReadOnly(protocolSnapshot);
        AuthorityProjectionBindingHash = L2PlanningContextCanonicalJson.ValidateSha256(
            authorityProjectionBindingHash,
            nameof(authorityProjectionBindingHash));
        DependencyContractIdentity.Validate(opportunityLedgerId, nameof(opportunityLedgerId));
        OpportunityLedgerId = opportunityLedgerId;
        DispatchConfiguration = dispatchConfiguration;
        PressureManifest = pressureManifest;
        _canonicalBytes = Serialize(includeConditionIdentity: true);
        _sharedCanonicalBytes = Serialize(includeConditionIdentity: false);
        ManifestHash = Hash(_canonicalBytes);
        SharedConfigurationHash = Hash(_sharedCanonicalBytes);
    }

    public string ManifestId { get; }
    public string PreregistrationArtifactVersion { get; }
    public FormalRq1Treatment Treatment { get; }
    public string RuntimeVersion { get; }
    public string ModelProfileId { get; }
    public IReadOnlyList<FormalRq1RequestProtocolManifestEntry> RequestProtocols => _requestProtocols;
    public string AuthorityProjectionBindingHash { get; }
    public string OpportunityLedgerId { get; }
    public FormalRq1DispatchConfiguration DispatchConfiguration { get; }
    public FormalRq1PressureManifest PressureManifest { get; }
    public string ManifestHash { get; }
    public string SharedConfigurationHash { get; }

    public byte[] GetCanonicalBytes()
    {
        return _canonicalBytes.ToArray();
    }

    public void ValidateRunPurpose(
        FormalRq1RunPurpose purpose,
        FormalCollectionAuthorization? authorization = null)
    {
        if (!Enum.IsDefined(purpose))
        {
            throw new ArgumentOutOfRangeException(nameof(purpose));
        }

        if (purpose == FormalRq1RunPurpose.FormalCollection)
        {
            if (authorization is null)
            {
                throw new InvalidOperationException(
                    "Formal RQ1 collection requires an external frozen authorization.");
            }

            IReadOnlyList<string> blockers = authorization.GetBlockers(
                FormalExperimentRq.Rq1,
                PreregistrationArtifactVersion);
            if (blockers.Count != 0)
            {
                throw new InvalidOperationException(
                    $"Formal RQ1 collection is blocked: {string.Join(",", blockers)}.");
            }
        }
    }

    private byte[] Serialize(bool includeConditionIdentity)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", CurrentProtocolVersion);
            if (includeConditionIdentity)
            {
                writer.WriteString("manifest_id", ManifestId);
                writer.WriteString("treatment", TreatmentToken(Treatment));
            }

            writer.WriteString("preregistration_artifact_version", PreregistrationArtifactVersion);
            writer.WriteString("runtime_version", RuntimeVersion);
            writer.WriteString("model_profile_id", ModelProfileId);
            writer.WriteString("authority_projection_binding_hash", AuthorityProjectionBindingHash);
            writer.WriteString("opportunity_ledger_id", OpportunityLedgerId);
            writer.WritePropertyName("request_protocols");
            writer.WriteStartArray();
            foreach (FormalRq1RequestProtocolManifestEntry protocol in RequestProtocols)
            {
                writer.WriteStartObject();
                writer.WriteString("request_kind", RequestKindToken(protocol.RequestKind));
                writer.WriteString("protocol_version", protocol.ProtocolVersion);
                writer.WriteString("actor_visible_context_builder_version", protocol.ActorVisibleContextBuilderVersion);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("dispatch");
            WriteDispatch(writer);
            writer.WritePropertyName("pressure");
            WritePressure(writer);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private void WriteDispatch(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("configuration_id", DispatchConfiguration.ConfigurationId);
        writer.WriteNumber("starvation_age_ticks", DispatchConfiguration.StarvationAgeTicks);
        writer.WriteNumber("logical_session_budget", DispatchConfiguration.LogicalSessionBudget);
        writer.WriteNumber("max_provider_in_flight", DispatchConfiguration.MaxProviderInFlight);
        writer.WritePropertyName("retry_backoff_ticks");
        writer.WriteStartArray();
        foreach (TimeSpan backoff in DispatchConfiguration.RetryBackoffs)
        {
            writer.WriteNumberValue(backoff.Ticks);
        }

        writer.WriteEndArray();
        writer.WriteString("retry_policy_id", DispatchConfiguration.RetryClassificationPolicy.PolicyId);
        writer.WriteString("retry_policy_content_hash", DispatchConfiguration.RetryClassificationPolicy.ContentHash);
        writer.WritePropertyName("retryable_failure_codes");
        writer.WriteStartArray();
        foreach (FormalRq1TransportFailureCode code in DispatchConfiguration.RetryClassificationPolicy.RetryableFailureCodes)
        {
            writer.WriteStringValue(code.Value);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private void WritePressure(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("evaluator_host_version", PressureManifest.EvaluatorHostVersion);
        writer.WriteString("dependency_index_version", PressureManifest.DependencyIndexVersion);
        writer.WriteString("dependency_index_content_hash", PressureManifest.DependencyIndexContentHash);
        writer.WriteString("configuration_hash", PressureManifest.ConfigurationHash);
        writer.WritePropertyName("profiles");
        writer.WriteStartArray();
        foreach (FormalRq1PressureProfileManifestEntry profile in PressureManifest.Profiles)
        {
            writer.WriteStartObject();
            writer.WriteString("profile_id", profile.ProfileId.Value);
            writer.WriteNumber("profile_version", profile.ProfileVersion);
            writer.WriteString("evaluator_content_hash", profile.EvaluatorContentHash);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("states");
        writer.WriteStartArray();
        foreach (FormalRq1PressureStateManifestEntry state in PressureManifest.States)
        {
            writer.WriteStartObject();
            writer.WriteString("pressure_id", state.PressureId.Value);
            writer.WriteString("profile_id", state.ProfileId.Value);
            writer.WriteNumber("profile_version", state.ProfileVersion);
            writer.WriteString("evaluator_content_hash", state.EvaluatorContentHash);
            writer.WriteString("initial_state_hash", state.InitialStateHash);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static string TreatmentToken(FormalRq1Treatment treatment)
    {
        return treatment switch
        {
            FormalRq1Treatment.AgentCentric => "agent_centric",
            FormalRq1Treatment.EventCentric => "event_centric",
            _ => throw new ArgumentOutOfRangeException(nameof(treatment))
        };
    }

    private static string RequestKindToken(RemotePlannerRequestKind requestKind)
    {
        return requestKind switch
        {
            RemotePlannerRequestKind.Planning => "planning",
            RemotePlannerRequestKind.PlanlessStrategic => "planless_strategic",
            RemotePlannerRequestKind.InviteResponse => "invite_response",
            _ => throw new ArgumentOutOfRangeException(nameof(requestKind))
        };
    }

    private static string Hash(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private sealed class RequestProtocolComparer : IComparer<FormalRq1RequestProtocolManifestEntry>
    {
        public static RequestProtocolComparer Instance { get; } = new();

        public int Compare(FormalRq1RequestProtocolManifestEntry? left, FormalRq1RequestProtocolManifestEntry? right)
        {
            return left!.RequestKind.CompareTo(right!.RequestKind);
        }
    }
}

/// <summary>Two isolated conditions that differ only in treatment identity.</summary>
public sealed class FormalRq1MatchedPairManifest
{
    private readonly byte[] _canonicalBytes;

    public FormalRq1MatchedPairManifest(
        FormalRq1ConditionManifest agentCentric,
        FormalRq1ConditionManifest eventCentric)
    {
        ArgumentNullException.ThrowIfNull(agentCentric);
        ArgumentNullException.ThrowIfNull(eventCentric);
        if (agentCentric.Treatment != FormalRq1Treatment.AgentCentric
            || eventCentric.Treatment != FormalRq1Treatment.EventCentric)
        {
            throw new ArgumentException("A matched pair requires one AgentCentric and one EventCentric manifest.");
        }

        if (StringComparer.Ordinal.Equals(agentCentric.ManifestId, eventCentric.ManifestId))
        {
            throw new ArgumentException("Matched condition manifests require distinct identities.");
        }

        if (!StringComparer.Ordinal.Equals(
            agentCentric.SharedConfigurationHash,
            eventCentric.SharedConfigurationHash))
        {
            throw new ArgumentException("Matched RQ1 conditions must have byte-identical shared configuration.");
        }

        AgentCentric = agentCentric;
        EventCentric = eventCentric;
        _canonicalBytes = Serialize();
        PairManifestHash = Hash(_canonicalBytes);
    }

    public FormalRq1ConditionManifest AgentCentric { get; }
    public FormalRq1ConditionManifest EventCentric { get; }
    public string PairManifestHash { get; }

    public byte[] GetCanonicalBytes()
    {
        return _canonicalBytes.ToArray();
    }

    private byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("protocol_version", "formal-rq1-matched-pair-manifest-v1");
            writer.WriteString("shared_configuration_hash", AgentCentric.SharedConfigurationHash);
            writer.WriteString("agent_centric_manifest_hash", AgentCentric.ManifestHash);
            writer.WriteString("event_centric_manifest_hash", EventCentric.ManifestHash);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static string Hash(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
