namespace MediFiler3.Application.Folders;

public readonly record struct FolderRuleColor(byte Red, byte Green, byte Blue);
public readonly record struct FolderRuleActions(FolderRuleColor? TextColor = null, bool? ExpandedByDefault = null, bool Hidden = false);
public enum FolderRuleKind { Name, FileCount, AllFolders }

public sealed record FolderNameRule(string Contains, FolderRuleColor? TextColor = null,
    bool? ExpandedByDefault = null, bool Enabled = true)
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public FolderRuleKind Kind { get; init; }
    public bool ApplyToRootFolders { get; init; }
    public bool HideFolder { get; init; }
    public long? MinimumFiles { get; init; }
    public long? MaximumFiles { get; init; }
    public static readonly Guid EmptyFolderRuleId = new("216ed389-5a8c-49c5-b33c-224bc4f2a7f0");
    public static FolderNameRule EmptyFolderDefault() => new("", new(96, 96, 96))
    {
        Id = EmptyFolderRuleId, Kind = FolderRuleKind.FileCount, MinimumFiles = 0, MaximumFiles = 0
    };

    public static readonly Guid CollapseFoldersRuleId = new("ae165b7c-69db-4f70-9be9-2d9edf581a98");
    public static FolderNameRule CollapseFoldersDefault() => new("", ExpandedByDefault: false, Enabled: false)
    {
        Id = CollapseFoldersRuleId, Kind = FolderRuleKind.AllFolders
    };

    public void Validate()
    {
        if (!Enum.IsDefined(Kind)) throw new ArgumentException("Choose a supported rule type.");
        if (Kind == FolderRuleKind.Name && (string.IsNullOrWhiteSpace(Contains) || Contains.Length > 256))
            throw new ArgumentException("Enter folder-name text (1–256 characters).", nameof(Contains));
        if (Kind == FolderRuleKind.FileCount && (MinimumFiles is null && MaximumFiles is null ||
            MinimumFiles < 0 || MaximumFiles < 0 || MinimumFiles is { } min && MaximumFiles is { } max && min > max))
            throw new ArgumentException("Enter a nonnegative file-count range with minimum no greater than maximum.");
        if (HideFolder && ExpandedByDefault is not null)
            throw new ArgumentException("Choose either hiding or an expansion default.");
        if (TextColor is null && ExpandedByDefault is null && !HideFolder)
            throw new ArgumentException("Choose a text color or an expansion action.");
        if (Id == Guid.Empty) throw new ArgumentException("A rule must have an identity.");
    }

    public static void ValidateAll(IReadOnlyList<FolderNameRule>? rules)
    {
        if (rules is null) return;
        if (rules.Count > 256) throw new ArgumentException("At most 256 folder rules are supported.");
        foreach (var rule in rules) { if (rule is null) throw new ArgumentException("Invalid folder rule."); rule.Validate(); }
        if (rules.Select(rule => rule.Id).Distinct().Count() != rules.Count) throw new ArgumentException("Duplicate folder rule identity.");
    }

    public static FolderRuleActions ResolveActions(string name, long? directFileCount, bool isRoot,
        IReadOnlyList<FolderNameRule> rules, CancellationToken token = default)
    {
        FolderRuleColor? color = null;
        bool? expanded = null;
        var structureFound = false;
        var hidden = false;
        foreach (var rule in rules)
        {
            token.ThrowIfCancellationRequested();
            if (!Matches(rule, name, directFileCount)) continue;
            color ??= rule.TextColor;
            if (!structureFound && (!isRoot || rule.ApplyToRootFolders) && (rule.ExpandedByDefault.HasValue || rule.HideFolder))
            {
                expanded = rule.ExpandedByDefault; hidden = rule.HideFolder; structureFound = true;
            }
            if (color.HasValue && structureFound) break;
        }
        return new(color, expanded, hidden);
    }

    private static bool Matches(FolderNameRule rule, string name, long? directFileCount) => rule.Enabled &&
        (rule.Kind == FolderRuleKind.AllFolders || rule.Kind == FolderRuleKind.Name && name.Contains(rule.Contains, StringComparison.OrdinalIgnoreCase) ||
         rule.Kind == FolderRuleKind.FileCount && directFileCount is { } count &&
         (rule.MinimumFiles is null || count >= rule.MinimumFiles) && (rule.MaximumFiles is null || count <= rule.MaximumFiles));

    public static FolderNameRule? FirstMatch(string name, IReadOnlyList<FolderNameRule> rules, CancellationToken token = default)
        => FirstMatch(name, null, rules, token);

    public static FolderNameRule? FirstMatch(string name, long? directFileCount, IReadOnlyList<FolderNameRule> rules, CancellationToken token = default)
    {
        foreach (var rule in rules)
        {
            token.ThrowIfCancellationRequested();
            if (Matches(rule, name, directFileCount)) return rule;
        }
        return null;
    }
}
