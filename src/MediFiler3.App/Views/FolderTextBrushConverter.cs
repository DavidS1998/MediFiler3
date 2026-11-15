using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MediFiler3.Application.Folders;

namespace MediFiler3.Views;

public sealed class FolderTextBrushConverter : IMultiValueConverter
{
    private readonly Dictionary<FolderRuleColor, Brush> _customBrushes = [];
    private static readonly IReadOnlyDictionary<byte, Brush> BrushesByTone = Enumerable.Range(0, 6)
        .Select(depth => FolderTextColorPolicy.GetGray(depth)).Distinct()
        .ToDictionary(tone => tone, tone =>
        {
            var brush = new SolidColorBrush(Color.FromRgb(tone, tone, tone)); brush.Freeze(); return (Brush)brush;
        });
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length > 2 && values[2] is FolderRuleColor color)
        {
            if (!_customBrushes.TryGetValue(color, out var brush))
            {
                if (_customBrushes.Count >= 256) _customBrushes.Clear();
                var created = new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue)); created.Freeze();
                _customBrushes[color] = brush = created;
            }
            return brush;
        }
        return BrushesByTone[FolderTextColorPolicy.GetGray(values[0] is int depth ? depth : 0, values[1] is not false)];
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        targetTypes.Select(_ => DependencyProperty.UnsetValue).ToArray();
}
