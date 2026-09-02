using Alice.Activities;
using Alice.Actors;
using Alice.Navigation;
using Alice.NpcExecution;
using Alice.PlayerControl;
using Alice.Presentation;
using Alice.ProductRuntime;
using Godot;

namespace Alice.LivingTown;

public sealed record TownContextEntry(string EntryId, string Label, bool Available, string? Reason);

public sealed record TownTargetProjection(
    string TargetId,
    string TargetKind,
    string DisplayLabel,
    string HoverText,
    string DetailText,
    IReadOnlyList<TownContextEntry> ContextEntries);

public sealed record TownGameplayContextProjection(
    IReadOnlyList<TownContextEntry> Entries,
    string? UnavailableText);

public enum TownNpcDebugSection
{
    Overview,
    Memories,
    Knowledge
}

public static class TownGameplayUiProjection
{
    public static TownGameplayContextProjection Create(
        IEnumerable<TownGameplayActionOffer> offers,
        bool chinese = false)
    {
        TownGameplayActionOffer[] snapshot = offers.ToArray();
        TownContextEntry[] entries = snapshot.Select(value => new TownContextEntry(
                value.EntryId,
                value.Validation.Available
                    ? TranslateAction(value.Label, chinese)
                    : $"{TranslateAction(value.Label, chinese)} ({(chinese ? "不可用" : "unavailable")}: {value.Validation.Reason})",
                value.Validation.Available,
                value.Validation.Reason))
            .Append(new TownContextEntry("debug", chinese ? "调试" : "Debug", true, null)).ToArray();
        string? reason = snapshot.FirstOrDefault(value => !value.Validation.Available)?.Validation.Reason;
        return new TownGameplayContextProjection(
            entries,
            reason is null ? null : $"({(chinese ? "不可采集" : "unavailable")}: {reason})");
    }

    private static string TranslateAction(string label, bool chinese)
    {
        if (!chinese) return label;
        string[] mappings =
        [
            "Gather|采集", "Plant|种植", "Harvest|收获", "Growing|生长中", "Craft|制作",
            "Use|使用", "Buy|购买", "Sell|出售", "Import|进货", "Export|出货",
            "Eat|进食", "Rest|休息", "Stock|补充库存", "Restock|进货"
        ];
        foreach (string mapping in mappings)
        {
            string[] pair = mapping.Split('|');
            if (StringComparer.Ordinal.Equals(label, pair[0])) return pair[1];
            if (label.StartsWith(pair[0] + " ", StringComparison.Ordinal))
                return pair[1] + label[pair[0].Length..];
        }
        return label;
    }
}

/// <summary>Pure marker-slot projection used by the live shell and roster evidence.</summary>
public static class TownGeometricProjection
{
    public static IReadOnlyList<TownTargetProjection> CreateNpcSlots(IEnumerable<(string ActorId, string Name, string Activity)> actors) =>
        actors.Select(actor => new TownTargetProjection(
            actor.ActorId,
            "NPC",
            actor.Name,
            $"{actor.Name}\nNPC\nActivity: {actor.Activity}",
            $"{actor.Name}\nNPC\nActor ID: {actor.ActorId}",
            CreateNpcEntries())).ToArray();

    public static IReadOnlyList<TownContextEntry> CreateNpcEntries(bool chinese = false) =>
        [new TownContextEntry("talk", chinese ? "对话" : "Talk", true, null),
            new TownContextEntry("debug", chinese ? "调试" : "Debug", true, null)];
}

public static class TownGodotNavigationMap
{
    public const float NavigationCellSize = 4.0f;
    public const float ActorClearance = 0.45f;

    public static NavigationPolygon Create(TownSpatialMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        int columns = (int)Math.Ceiling(map.WorldWidth / NavigationCellSize);
        int rows = (int)Math.Ceiling(map.WorldHeight / NavigationCellSize);
        var vertices = new Vector2[(columns + 1) * (rows + 1)];
        for (int row = 0; row <= rows; row++)
        for (int column = 0; column <= columns; column++)
        {
            vertices[VertexIndex(column, row, columns)] = new Vector2(
                Math.Min(column * NavigationCellSize, (float)map.WorldWidth),
                Math.Min(row * NavigationCellSize, (float)map.WorldHeight));
        }

        var polygon = new NavigationPolygon
        {
            AgentRadius = ActorClearance
        };
        polygon.SetVertices(vertices);
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
        {
            var center = new WorldPosition(
                Math.Min((column + 0.5) * NavigationCellSize, map.WorldWidth),
                Math.Min((row + 0.5) * NavigationCellSize, map.WorldHeight));
            if (TownWaterPassability.IsBlocked(map, center, ActorClearance, NavigationCellSize / 2.0f)) continue;
            int topLeft = VertexIndex(column, row, columns);
            int topRight = VertexIndex(column + 1, row, columns);
            int bottomRight = VertexIndex(column + 1, row + 1, columns);
            int bottomLeft = VertexIndex(column, row + 1, columns);
            polygon.AddPolygon([topLeft, bottomLeft, bottomRight, topRight]);
        }
        return polygon;
    }

    private static int VertexIndex(int column, int row, int columns) =>
        row * (columns + 1) + column;
}

/// <summary>One generic map renderer, target resolver and input adapter for the geometric Demo.</summary>
public sealed partial class TownMapInteractionShell : Node2D
{
    private readonly List<MapTargetGeometry> _mapTargets = [];
    private readonly List<MapTypeLabel> _mapTypeLabels = [];
    private readonly Dictionary<string, PlayerInteractionSelection> _contextSelections = new(StringComparer.Ordinal);
    private TownSpatialMap? _map;
    private Node? _npcContainer;
    private PlayerEntity? _player;
    private TownTargetProjection? _hovered;
    private TownTargetProjection? _contextTarget;
    private NpcEntity? _highlightedNpc;
    private RegionSocialGameplayRuntime? _gameplay;
    private ActorId? _playerActorId;
    private Func<SimTime>? _timeSource;
    private Func<string, TownNpcDebugSection, string>? _npcDebugSource;
    private TownTargetProjection? _debugTarget;

    public event Action<PlayerInteractionSelection>? PlayerActionSelected;
    public event Action<string>? NpcSelected;
    public event Action<string>? NpcDebugOpened;
    public event Action? NpcDebugClosed;

    [Export] public Label? TargetLabel { get; set; }
    [Export] public Control? DetailPanel { get; set; }
    [Export] public Label? DetailTitle { get; set; }
    [Export] public Label? DetailLabel { get; set; }
    [Export] public Button? DebugOverviewButton { get; set; }
    [Export] public Button? DebugMemoriesButton { get; set; }
    [Export] public Button? DebugKnowledgeButton { get; set; }
    [Export] public PopupMenu? ContextMenu { get; set; }

    public override void _Ready()
    {
        if (ContextMenu is not null) ContextMenu.IdPressed += OnContextEntryPressed;
    }

    public void Configure(
        TownSpatialMap map,
        Node npcContainer,
        PlayerEntity player,
        RegionSocialGameplayRuntime gameplay,
        ActorId playerActorId,
        Func<SimTime> timeSource,
        Func<string, TownNpcDebugSection, string> npcDebugSource)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
        _npcContainer = npcContainer ?? throw new ArgumentNullException(nameof(npcContainer));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _gameplay = gameplay ?? throw new ArgumentNullException(nameof(gameplay));
        _playerActorId = playerActorId;
        _timeSource = timeSource ?? throw new ArgumentNullException(nameof(timeSource));
        _npcDebugSource = npcDebugSource ?? throw new ArgumentNullException(nameof(npcDebugSource));
        BuildMapTargets(map);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (_map is null || _npcContainer is null) return;
        UpdateTypeLabels();
        TownTargetProjection? resolved = ResolveTarget(GetGlobalMousePosition());
        bool sameTarget = StringComparer.Ordinal.Equals(resolved?.TargetId, _hovered?.TargetId);
        UpdateTargetPanel(resolved);
        if (sameTarget)
        {
            _hovered = resolved;
            return;
        }
        _highlightedNpc?.SetActivityVisible(false);
        _highlightedNpc?.SetHighlighted(false);
        _highlightedNpc = resolved is not null && resolved.TargetKind == "NPC"
            ? FindNpc(resolved.TargetId)
            : null;
        _highlightedNpc?.SetHighlighted(true);
        _highlightedNpc?.SetActivityVisible(true);
        _hovered = resolved;
        QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } mouse) return;
        if (mouse.ButtonIndex == MouseButton.Left)
        {
            if (_hovered?.TargetKind == "NPC")
            {
                NpcSelected?.Invoke(_hovered.TargetId);
            }
            else if (_player is not null)
            {
                Vector2 point = GetGlobalMousePosition();
                _player.TryStartPointNavigation(new WorldPosition(point.X, point.Y));
            }
            GetViewport().SetInputAsHandled();
        }
        else if (mouse.ButtonIndex == MouseButton.Right && _hovered is not null)
        {
            OpenContextMenu(_hovered);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Draw()
    {
        foreach (MapTargetGeometry geometry in _mapTargets)
        {
            bool highlighted = geometry.HighlightOnHover
                && StringComparer.Ordinal.Equals(_hovered?.TargetId, geometry.Target.TargetId);
            Color color = highlighted ? geometry.Color.Lightened(0.3f) : geometry.Color;
            if (geometry.IsPolygon)
            {
                DrawColoredPolygon(geometry.Points, color);
            }
            else if (geometry.Points.Length > 1)
            {
                DrawPolyline(geometry.Points, color, geometry.Width, true);
            }
            else
            {
                DrawRect(geometry.Bounds, color, true);
            }
        }
    }

    private void BuildMapTargets(TownSpatialMap map)
    {
        _mapTargets.Clear();
        foreach (MapTypeLabel typeLabel in _mapTypeLabels) typeLabel.Label.QueueFree();
        _mapTypeLabels.Clear();
        _mapTargets.Add(RectTarget(
            new TownTargetProjection("terrain/brackenford", "Terrain", "Brackenford Region",
                "Brackenford Region\nTerrain",
                "Brackenford Region\nTerrain", DebugEntries()),
            new Rect2(0, 0, (float)map.WorldWidth, (float)map.WorldHeight), new Color("284b3a"), false));
        foreach (TownWaterBodyMapConfiguration waterBody in map.WaterBodies)
        {
            Vector2[] points = waterBody.Points
                .Select(point => new Vector2(point.X * map.CellSizeMeters, point.Y * map.CellSizeMeters))
                .ToArray();
            TownTargetProjection target = Target(waterBody.WaterBodyId, waterBody.Kind, Humanize(waterBody.WaterBodyId));
            _mapTargets.Add(waterBody.Shape == "Area"
                ? PolygonTarget(target, points, new Color("347da1", 0.92f), false)
                : LineTarget(target, points, waterBody.WidthCells * map.CellSizeMeters, new Color("347da1"), false));
        }
        foreach (TownRoadMapConfiguration road in map.Roads)
        {
            _mapTargets.Add(LineTarget(
                Target(road.RoadId, "Road", Humanize(road.RoadId)),
                road.Points.Select(point => new Vector2(point.X * map.CellSizeMeters, point.Y * map.CellSizeMeters)).ToArray(),
                road.WidthCells * map.CellSizeMeters,
                new Color("b89b72")));
        }
        foreach (TownResourceRegionMapConfiguration region in map.ResourceRegions)
        {
            Color color = region.ResourceType == "Fish"
                ? new Color("4c91a8", 0.42f)
                : new Color("4f8f62", 0.82f);
            Rect2 bounds = ToRect(region.Bounds, map.CellSizeMeters);
            _mapTargets.Add(RectTarget(
                Target(region.RegionId, $"Resource / {region.ResourceType}", Humanize(region.RegionId)),
                bounds, color));
            AddTypeLabel(bounds, TranslateKind(region.ResourceType));
        }
        foreach (TownBuildingMapConfiguration building in map.Buildings)
        {
            TownTargetProjection target = Target(building.BuildingId, building.Kind, Humanize(building.BuildingId));
            Rect2 bounds = ToRect(building.Bounds, map.CellSizeMeters);
            _mapTargets.Add(RectTarget(target, bounds, new Color("8c7b68", building.Kind == "House" ? 1.0f : 0.72f)));
            AddTypeLabel(bounds, TranslateKind(building.Kind));
        }
        foreach (TownBottleneckMapConfiguration bottleneck in map.Bottlenecks)
        {
            Rect2 bounds = ToRect(bottleneck.Bounds, map.CellSizeMeters);
            _mapTargets.Add(RectTarget(
                Target(bottleneck.BottleneckId, bottleneck.Kind, Humanize(bottleneck.BottleneckId)),
                bounds,
                new Color("c5a76d", 0.8f)));
            AddTypeLabel(bounds, TranslateKind(bottleneck.Kind));
        }
        UpdateTypeLabels();
    }

    private void AddTypeLabel(Rect2 bounds, string text)
    {
        var label = new Label
        {
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 1
        };
        label.AddThemeFontSizeOverride("font_size", 11);
        label.AddThemeColorOverride("font_color", new Color("f4f0dc"));
        AddChild(label);
        _mapTypeLabels.Add(new MapTypeLabel(label, new Vector2(bounds.Position.X, bounds.End.Y)));
    }

    private void UpdateTypeLabels()
    {
        float cameraScale = Math.Max(0.01f, GetCanvasTransform().X.Length());
        float inverseScale = 1.0f / cameraScale;
        foreach (MapTypeLabel typeLabel in _mapTypeLabels)
        {
            typeLabel.Label.Scale = Vector2.One * inverseScale;
            typeLabel.Label.Position = typeLabel.BottomLeft
                + new Vector2(2.0f * inverseScale, -14.0f * inverseScale);
        }
    }

    private TownTargetProjection? ResolveTarget(Vector2 point)
    {
        NpcEntity? npc = FindNpc(point);
        if (npc is not null)
        {
            string activity = npc.Marker?.ActivityLabel?.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(activity)) activity = "Idle";
            string detail = $"{npc.DisplayName}\nNPC\nActor ID: {npc.ActorIdentity}\nActivity: {activity}";
            return ProjectGameplay(new TownTargetProjection(
                npc.ActorIdentity,
                "NPC",
                npc.DisplayName,
                $"{npc.DisplayName}\nNPC\nActivity: {activity}",
                detail,
                TownGeometricProjection.CreateNpcEntries(false)));
        }
        for (int index = _mapTargets.Count - 1; index >= 0; index--)
        {
            MapTargetGeometry geometry = _mapTargets[index];
            if (geometry.Contains(point)) return ProjectGameplay(geometry.Target);
        }
        return null;
    }

    private NpcEntity? FindNpc(Vector2 point)
    {
        if (_npcContainer is null) return null;
        foreach (Node child in _npcContainer.GetChildren())
        {
            if (child is NpcEntity npc && npc.Visible
                && npc.GlobalPosition.DistanceTo(point) <= Math.Max(0.65f, PixelWidth(10.0f))) return npc;
        }
        return null;
    }

    private NpcEntity? FindNpc(string actorId)
    {
        if (_npcContainer is null) return null;
        foreach (Node child in _npcContainer.GetChildren())
        {
            if (child is NpcEntity npc && StringComparer.Ordinal.Equals(npc.ActorIdentity, actorId)) return npc;
        }
        return null;
    }

    private void OpenContextMenu(TownTargetProjection target)
    {
        if (ContextMenu is null) return;
        _contextTarget = target;
        _contextSelections.Clear();
        ContextMenu.Clear();
        for (int index = 0; index < target.ContextEntries.Count; index++)
        {
            TownContextEntry entry = target.ContextEntries[index];
            ContextMenu.AddItem(entry.Label, index);
            ContextMenu.SetItemDisabled(index, !entry.Available);
            ContextMenu.SetItemTooltip(index, entry.Reason ?? string.Empty);
            if (entry.Available && _gameplay is not null && _playerActorId is not null && _timeSource is not null)
            {
                TownGameplayActionOffer? offer = _gameplay.GetActionOffers(_playerActorId.Value, target.TargetId, _timeSource())
                    .FirstOrDefault(value => StringComparer.Ordinal.Equals(value.EntryId, entry.EntryId));
                if (offer is not null) _contextSelections[entry.EntryId] = offer.Selection;
            }
        }
        ContextMenu.Position = (Vector2I)GetViewport().GetMousePosition();
        ContextMenu.Popup();
    }

    private void OnContextEntryPressed(long index)
    {
        TownTargetProjection? target = _contextTarget;
        if (target is null || index < 0 || index >= target.ContextEntries.Count) return;
        TownContextEntry entry = target.ContextEntries[(int)index];
        if (entry.EntryId == "debug") ShowDebug(target);
        else if (entry.EntryId == "talk" && target.TargetKind == "NPC")
            NpcSelected?.Invoke(target.TargetId);
        else if (_contextSelections.TryGetValue(entry.EntryId, out PlayerInteractionSelection? selection))
            PlayerActionSelected?.Invoke(selection);
    }

    public void OnDebugClosePressed()
    {
        _debugTarget = null;
        if (DetailPanel is not null) DetailPanel.Visible = false;
        NpcDebugClosed?.Invoke();
    }

    public void OnDebugOverviewPressed() => ShowDebugSection(TownNpcDebugSection.Overview);
    public void OnDebugMemoriesPressed() => ShowDebugSection(TownNpcDebugSection.Memories);
    public void OnDebugKnowledgePressed() => ShowDebugSection(TownNpcDebugSection.Knowledge);

    private void ShowDebug(TownTargetProjection target)
    {
        _debugTarget = target;
        bool npc = target.TargetKind == "NPC";
        if (DetailPanel is not null) DetailPanel.Visible = true;
        if (DetailTitle is not null)
            DetailTitle.Text = $"Debug — {target.DisplayLabel}";
        if (DebugOverviewButton is not null) DebugOverviewButton.Visible = npc;
        if (DebugMemoriesButton is not null) DebugMemoriesButton.Visible = npc;
        if (DebugKnowledgeButton is not null) DebugKnowledgeButton.Visible = npc;
        ShowDebugSection(TownNpcDebugSection.Overview);
        if (npc) NpcDebugOpened?.Invoke(target.TargetId);
        else NpcDebugClosed?.Invoke();
    }

    private void ShowDebugSection(TownNpcDebugSection section)
    {
        TownTargetProjection? target = _debugTarget;
        if (target is null || DetailPanel?.Visible != true) return;
        if (DetailLabel is not null)
        {
            string detail = target.TargetKind == "NPC"
                ? _npcDebugSource?.Invoke(target.TargetId, section) ?? target.DetailText
                : target.DetailText;
            DetailLabel.Text = detail;
        }
    }

    private void UpdateTargetPanel(TownTargetProjection? target)
    {
        if (TargetLabel is not null) TargetLabel.Text = target?.HoverText ?? "No target";
    }

    private TownTargetProjection Target(string id, string kind, string label)
    {
        string displayKind = TranslateKind(kind);
        return new(id, kind, label, $"{label}\n{displayKind}",
            $"{label}\n{displayKind}\nTarget ID: {id}", DebugEntries());
    }

    private IReadOnlyList<TownContextEntry> DebugEntries() =>
        [new TownContextEntry("debug", "Debug", true, null)];

    private TownTargetProjection ProjectGameplay(TownTargetProjection target)
    {
        if (_gameplay is null || _playerActorId is null || _timeSource is null) return target;
        IReadOnlyList<TownGameplayActionOffer> offers = _gameplay.GetActionOffers(_playerActorId.Value, target.TargetId, _timeSource());
        if (offers.Count == 0) return target;
        TownGameplayContextProjection projection = TownGameplayUiProjection.Create(offers, false);
        string hover = projection.UnavailableText is null ? target.HoverText : $"{target.HoverText}\n{projection.UnavailableText}";
        IReadOnlyList<TownContextEntry> entries = target.TargetKind == "NPC"
            ? [new TownContextEntry("talk", "Talk", true, null), .. projection.Entries]
            : projection.Entries;
        return target with { HoverText = hover, ContextEntries = entries };
    }

    private static MapTargetGeometry RectTarget(
        TownTargetProjection target,
        Rect2 bounds,
        Color color,
        bool highlightOnHover = true) =>
        new(target, bounds, [], 0, color, false, highlightOnHover);

    private float PixelWidth(float pixels)
    {
        float cameraScale = GetCanvasTransform().X.Length();
        return pixels / cameraScale;
    }

    private static MapTargetGeometry LineTarget(
        TownTargetProjection target,
        Vector2[] points,
        float width,
        Color color,
        bool highlightOnHover = true) =>
        new(target, default, points, width, color, false, highlightOnHover);

    private static MapTargetGeometry PolygonTarget(
        TownTargetProjection target,
        Vector2[] points,
        Color color,
        bool highlightOnHover = true) =>
        new(target, default, points, 0, color, true, highlightOnHover);

    private static Rect2 ToRect(TownMapRect bounds, int cellSize) =>
        new(bounds.X * cellSize, bounds.Y * cellSize, bounds.Width * cellSize, bounds.Height * cellSize);

    private static string Humanize(string value)
    {
        var text = new System.Text.StringBuilder(value.Length + 4);
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (current == '-')
            {
                text.Append(' ');
                continue;
            }
            if (index > 0 && char.IsUpper(current) && char.IsLower(value[index - 1])) text.Append(' ');
            text.Append(current);
        }
        return text.ToString();
    }

    private static string TranslateKind(string kind) => Humanize(kind);

    private sealed record MapTypeLabel(Label Label, Vector2 BottomLeft);

    private sealed record MapTargetGeometry(
        TownTargetProjection Target,
        Rect2 Bounds,
        Vector2[] Points,
        float Width,
        Color Color,
        bool IsPolygon,
        bool HighlightOnHover)
    {
        public bool Contains(Vector2 point)
        {
            if (Points.Length == 0) return Bounds.HasPoint(point);
            if (IsPolygon) return Geometry2D.IsPointInPolygon(point, Points);
            for (int index = 1; index < Points.Length; index++)
                if (Geometry2D.GetClosestPointToSegment(point, Points[index - 1], Points[index]).DistanceTo(point) <= Width / 2.0f)
                    return true;
            return false;
        }
    }
}
