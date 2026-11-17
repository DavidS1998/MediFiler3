using System.Windows.Media;
using MediFiler3.Application.Files;
using MediFiler3.Application.Navigation;

namespace MediFiler3;

public partial class MainWindow
{
    private FileNameColorRule[] _fileNameRules = [];
    private bool _fileRuleBrowseSelected, _fileRuleCaptionApplied;
    private Brush _unruledCaptionBackground = Brushes.Black, _unruledCaptionForeground = Brushes.White;
    private void UpdateFileRuleCaption()
    {
        var color = _fileRuleBrowseSelected && _navigationState.ViewingMode == ViewingMode.Browse && _navigationState.CurrentFile is { } file
            ? FileNameColorRule.Match(file.Name, _fileNameRules) : null;
        if (color is { } rgb)
        {
            if (!_fileRuleCaptionApplied) { _unruledCaptionBackground = TitleBarBackground; _unruledCaptionForeground = TitleBarForeground; }
            _fileRuleCaptionApplied = true;
            var background = new SolidColorBrush(Color.FromRgb(rgb.Red, rgb.Green, rgb.Blue)); background.Freeze();
            TitleBarBackground = background;
            TitleBarForeground = 299 * rgb.Red + 587 * rgb.Green + 114 * rgb.Blue >= 150000 ? Brushes.Black : Brushes.White;
        }
        else if (_fileRuleCaptionApplied)
        {
            _fileRuleCaptionApplied = false;
            TitleBarBackground = _unruledCaptionBackground; TitleBarForeground = _unruledCaptionForeground;
        }
    }
}
