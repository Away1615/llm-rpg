namespace Alice.ModelRuntime;

public sealed class RemotePlannerResponse
{
    private RemotePlannerResponse(RemotePlannerRequestBinding binding, RemotePlannerDecision decision)
    {
        Binding = binding;
        Decision = decision;
    }

    public RemotePlannerRequestBinding Binding { get; }
    public RemotePlannerDecision Decision { get; }

    public static RemotePlannerResponse FromToolCalls(RemotePlannerRequest request, IEnumerable<RemotePlannerToolCall>? calls)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new RemotePlannerResponse(request.Binding, RemotePlannerDecisionDecoder.Decode(request, calls));
    }

    public static RemotePlannerResponse InvocationFailed(RemotePlannerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new RemotePlannerResponse(request.Binding, new RemotePlannerFailure(RemotePlannerFailureKind.InvocationFailed));
    }
}
