using Alice.LivingTown;
using Alice.ProductRuntime;

namespace Alice.CognitiveLodDialogueExperiment;

internal static class Program
{
    private const int DefaultRepeats = 5;

    public static int Main(string[] args)
    {
        try
        {
            return MainAsync(args).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static async Task<int> MainAsync(string[] args)
    {
        Arguments parsed = Arguments.Parse(args);
        StudyInputs inputs = StudyInputs.Load(parsed.CasesPath, parsed.ExpectedPath);
        inputs.Validate();
        TownWorldConfiguration world = TownWorldConfiguration.Load(parsed.WorldPath);
        DialogueSurfaceProfile surface = DialogueSurfaceProfile.LoadFile(parsed.SurfacePath);

        if (parsed.Mode == RunMode.Preflight)
        {
            await RunPreflightAsync(inputs, world, surface).ConfigureAwait(false);
            return 0;
        }

        string source = parsed.Mode is RunMode.DryWorkload or RunMode.LiveWorkload
            ? "workload"
            : "controlled";
        string runId = CreateRunId(parsed.Mode);
        using var artifacts = new StudyArtifactWriter(parsed.OutputPath!, runId, source);
        bool liveProvider = parsed.Mode is RunMode.LiveControlled or RunMode.LiveWorkload;
        artifacts.WriteManifest(
            inputs,
            parsed.Mode.ToString(),
            liveProvider,
            world.Population.Actors.Count,
            source == "controlled" ? parsed.Repeats : null,
            source == "workload" ? 60 : null,
            parsed.WorldPath,
            parsed.SurfacePath);
        if (parsed.Mode is RunMode.DryControlled or RunMode.LiveControlled)
        {
            bool live = parsed.Mode == RunMode.LiveControlled;
            IReadOnlyList<DialogueLifecycleRecord> records = await ControlledRunner.RunAsync(
                inputs,
                world,
                surface,
                parsed.Repeats,
                live,
                artifacts,
                CancellationToken.None).ConfigureAwait(false);
            artifacts.Complete(inputs, checked(inputs.Cases.Cases.Length * parsed.Repeats));
            Console.WriteLine(
                $"DIALOGUE LOD CONTROLLED {(live ? "LIVE" : "DRY")} COMPLETE " +
                $"records={records.Count} output={artifacts.OutputDirectory}");
            return 0;
        }

        bool workloadLive = parsed.Mode == RunMode.LiveWorkload;
        int opportunities = await WorkloadRunner.RunAsync(
            inputs,
            world,
            surface,
            workloadLive,
            artifacts,
            CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine(
            $"DIALOGUE LOD WORKLOAD {(workloadLive ? "LIVE" : "DRY")} COMPLETE " +
            $"days=60 opportunities={opportunities} output={artifacts.OutputDirectory}");
        return 0;
    }

    private static async Task RunPreflightAsync(
        StudyInputs inputs,
        TownWorldConfiguration world,
        DialogueSurfaceProfile surface)
    {
        IReadOnlyList<DialogueLifecycleRecord> records = await ControlledRunner.RunAsync(
            inputs,
            world,
            surface,
            1,
            false,
            null,
            CancellationToken.None).ConfigureAwait(false);
        Require(records.Count == 36, "Preflight did not execute all 36 controlled cases.");
        foreach (DialogueLifecycleRecord record in records)
        {
            Require(record.RouteMatch == true, $"Preflight route mismatch for {record.CaseId}.");
            if (record.ExpectedRoute is "L0" or "L1")
                Require(record.TerminalAcceptable == true, $"Preflight terminal mismatch for {record.CaseId}.");
            if (record.ExpectedRoute == "L2")
            {
                Require(record.EscalationRequested, $"Preflight did not request escalation for {record.CaseId}.");
                Require(record.HostAccepted == true, $"Host rejected the expected escalation for {record.CaseId}.");
                Require(!string.IsNullOrWhiteSpace(record.DecisionNeedId),
                    $"Preflight did not register a DecisionNeed for {record.CaseId}.");
            }
        }
        Console.WriteLine(
            $"DIALOGUE LOD PREFLIGHT PASS cases={records.Count} " +
            $"cases_sha256={inputs.CasesSha256} expected_sha256={inputs.ExpectedSha256}");
    }

    private static string CreateRunId(RunMode mode) =>
        $"dialogue-lod-{mode.ToString().ToLowerInvariant()}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}";

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private enum RunMode
    {
        Preflight,
        DryControlled,
        LiveControlled,
        DryWorkload,
        LiveWorkload
    }

    private sealed record Arguments(
        RunMode Mode,
        string CasesPath,
        string ExpectedPath,
        string WorldPath,
        string SurfacePath,
        string? OutputPath,
        int Repeats)
    {
        public static Arguments Parse(string[] args)
        {
            if (args.Length == 0) throw Usage("A mode is required.");
            string root = FindRepositoryRoot();
            RunMode mode = args[0] switch
            {
                "preflight" => RunMode.Preflight,
                "dry-controlled" => RunMode.DryControlled,
                "live-controlled" => RunMode.LiveControlled,
                "dry-workload" => RunMode.DryWorkload,
                "live-workload" => RunMode.LiveWorkload,
                _ => throw Usage($"Unknown mode {args[0]}.")
            };
            string casesPath = Path.Combine(root, "godot", "Data", "DialogueLod", "dialogue_lod_cases_v1.json");
            string expectedPath = Path.Combine(root, "godot", "Data", "DialogueLod", "dialogue_lod_expected_v1.json");
            string worldPath = Path.Combine(root, "godot", "Config", "town_world.json");
            string surfacePath = Path.Combine(root, "godot", "Data", "dialogue_surface_profile_v1.json");
            string? outputPath = null;
            int repeats = DefaultRepeats;
            for (int index = 1; index < args.Length; index++)
            {
                string option = args[index];
                string value = ReadOptionValue(args, ref index, option);
                switch (option)
                {
                    case "--cases": casesPath = Path.GetFullPath(value); break;
                    case "--expected": expectedPath = Path.GetFullPath(value); break;
                    case "--world": worldPath = Path.GetFullPath(value); break;
                    case "--surface": surfacePath = Path.GetFullPath(value); break;
                    case "--output": outputPath = Path.GetFullPath(value); break;
                    case "--repeats":
                        if (!int.TryParse(value, out repeats) || repeats <= 0)
                            throw Usage("--repeats must be a positive integer.");
                        break;
                    default: throw Usage($"Unknown option {option}.");
                }
            }
            bool needsOutput = mode != RunMode.Preflight;
            if (needsOutput && string.IsNullOrWhiteSpace(outputPath))
                throw Usage("Run modes require --output with an empty or new directory.");
            return new Arguments(mode, casesPath, expectedPath, worldPath, surfacePath, outputPath, repeats);
        }

        private static string ReadOptionValue(string[] args, ref int index, string option)
        {
            if (!option.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
                throw Usage($"Option {option} requires a value.");
            index++;
            return args[index];
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? current = new(AppContext.BaseDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "godot", "Alice.csproj")))
                    return current.FullName;
                current = current.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate the llm-rpg repository root.");
        }

        private static ArgumentException Usage(string message) => new(
            message + Environment.NewLine +
            "Usage: <preflight|dry-controlled|live-controlled|dry-workload|live-workload> " +
            "[--output PATH] [--repeats N] [--cases PATH] [--expected PATH] " +
            "[--world PATH] [--surface PATH]");
    }
}
