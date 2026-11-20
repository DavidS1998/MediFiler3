using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using MediFiler3.Application.Folders;

namespace MediFiler3.Views;

public sealed class TreeGroupEndConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values[0] is FolderTreeEntry node && (ReferenceEquals(node, values[1]) || ReferenceEquals(node, values[2]));
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class TreeFooterMarginConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 3 && values[0] is FolderTreeEntry node && ReferenceEquals(node, values[1]) && values[2] is double height
            ? new Thickness(0, 0, 0, height) : new Thickness(0);
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public partial class BrowseView
{
    public static readonly DependencyProperty TreeFooterRootProperty = DependencyProperty.Register(nameof(TreeFooterRoot), typeof(FolderTreeEntry), typeof(BrowseView));
    public FolderTreeEntry? TreeFooterRoot { get => (FolderTreeEntry?)GetValue(TreeFooterRootProperty); private set => SetValue(TreeFooterRootProperty, value); }
    public static readonly DependencyProperty TreeScrollClearanceProperty = DependencyProperty.Register(nameof(TreeScrollClearance), typeof(double), typeof(BrowseView), new PropertyMetadata(48d));
    public double TreeScrollClearance { get => (double)GetValue(TreeScrollClearanceProperty); private set => SetValue(TreeScrollClearanceProperty, value); }
    private readonly HashSet<FolderTreeEntry> _footerRoots = [];
    private void InitializeTreeFooter()
    {
        _navigationState.Roots.CollectionChanged += OnFooterRootsChanged; RefreshTreeFooterRoots();
    }
    private void OnFooterRootsChanged(object? sender, NotifyCollectionChangedEventArgs args) => RefreshTreeFooterRoots();
    private void RefreshTreeFooterRoots()
    {
        foreach (var node in _footerRoots) node.PropertyChanged -= OnFooterRootChanged;
        _footerRoots.Clear();
        foreach (var node in _navigationState.Roots) { _footerRoots.Add(node); node.PropertyChanged += OnFooterRootChanged; }
        UpdateTreeFooterRoot();
    }
    private void OnFooterRootChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName == nameof(FolderTreeEntry.IsHiddenByRule)) UpdateTreeFooterRoot(); }
    private void UpdateTreeFooterRoot() => TreeFooterRoot = _navigationState.Roots.LastOrDefault(root => !root.IsHiddenByRule);
    private void StopTreeFooter()
    {
        _navigationState.Roots.CollectionChanged -= OnFooterRootsChanged;
        foreach (var node in _footerRoots) node.PropertyChanged -= OnFooterRootChanged;
        _footerRoots.Clear();
    }
}
