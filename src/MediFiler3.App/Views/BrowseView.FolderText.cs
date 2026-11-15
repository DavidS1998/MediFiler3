using System.Windows;
using MediFiler3.Application.Folders;

namespace MediFiler3.Views;

public partial class BrowseView
{
    private FolderRuleController _folderRuleController = null!;
    public void SetFolderNameRules(IReadOnlyList<FolderNameRule> rules, bool applyStructureImmediately = false) => _folderRuleController.SetRules(rules, applyStructureImmediately);
    public Task FolderRulesCompletion => _folderRuleController.Completion;
    public static readonly DependencyProperty FolderDepthShadingEnabledProperty = DependencyProperty.Register(
        nameof(FolderDepthShadingEnabled), typeof(bool), typeof(BrowseView), new PropertyMetadata(true));
    public bool FolderDepthShadingEnabled
    {
        get => (bool)GetValue(FolderDepthShadingEnabledProperty);
        set => SetValue(FolderDepthShadingEnabledProperty, value);
    }
}
