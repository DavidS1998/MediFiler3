using MediFiler3.Application.Folders;

namespace MediFiler3.Application.Files;

public sealed record FileNameColorRule(string Contains, FolderRuleColor Color, bool Enabled = true)
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Contains) || Contains.Length > 256)
            throw new ArgumentException("Enter filename text (1-256 characters).", nameof(Contains));
        if (Id == Guid.Empty) throw new ArgumentException("A rule must have an identity.");
    }
    public static void ValidateAll(IReadOnlyList<FileNameColorRule>? rules)
    {
        if (rules is null) return;
        if (rules.Count > 256) throw new ArgumentException("At most 256 file rules are supported.");
        foreach (var rule in rules) { if (rule is null) throw new ArgumentException("Invalid file rule."); rule.Validate(); }
        if (rules.Select(rule => rule.Id).Distinct().Count() != rules.Count) throw new ArgumentException("Duplicate file rule identity.");
    }
    public static FolderRuleColor? Match(string name, IReadOnlyList<FileNameColorRule> rules, CancellationToken token = default)
    {
        foreach (var rule in rules)
        {
            token.ThrowIfCancellationRequested();
            if (rule.Enabled && name.Contains(rule.Contains, StringComparison.OrdinalIgnoreCase)) return rule.Color;
        }
        return null;
    }
}
