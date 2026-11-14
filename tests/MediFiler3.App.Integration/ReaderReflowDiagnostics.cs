using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using MediFiler3.Application.Navigation;
using MediFiler3.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

internal static partial class Program
{
    // Opt-in, test-only observation: no extra layout, scrolling, focus or settling waits.
    private sealed class ReaderReflowDiagnostics : IDisposable
    {
        private readonly WinUiReaderIsland _reader;
        private readonly ReaderSessionState _session;
        private readonly ScrollViewer _scroll;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Queue<object> _events = new();
        private readonly bool _enabled = Environment.GetEnvironmentVariable("MEDIFILER3_READER_REFLOW_DIAGNOSTICS") == "1";
        private string? _lastState;
        private int _dropped;

        private readonly string _name;

        public ReaderReflowDiagnostics(WinUiReaderIsland reader, ReaderSessionState session, string name = "reader-width-reflow")
        {
            _reader = reader; _session = session; _name = name; _scroll = (ScrollViewer)Field(reader, "_scroll")!;
            if (!_enabled) return;
            _scroll.ViewChanging += ViewChanging;
            _scroll.ViewChanged += ViewChanged;
            _scroll.SizeChanged += SizeChanged;
            _scroll.LayoutUpdated += LayoutUpdated;
            _scroll.GettingFocus += GettingFocus;
            _scroll.LosingFocus += LosingFocus;
            _scroll.GotFocus += GotFocus;
            _scroll.LostFocus += LostFocus;
            _reader.Dispatcher.Hooks.OperationStarted += OperationStarted;
            _reader.Dispatcher.Hooks.OperationCompleted += OperationCompleted;
            Mark("start");
        }

        public void Mark(string reason, string? detail = null, bool changesOnly = false)
        {
            if (!_enabled) return;
            var rows = ((System.Collections.IDictionary)Field(_reader, "_rows")!).Values.Cast<object>().Select(row =>
            {
                var root = (FrameworkElement)row.GetType().GetProperty("Root")!.GetValue(row)!;
                var image = (Image)row.GetType().GetProperty("Image")!.GetValue(row)!;
                return new { File = row.GetType().GetProperty("File")!.GetValue(row),
                    Top = Canvas.GetTop(root), root.Height, root.ActualHeight, HasSource = image.Source is not null };
            }).ToArray();
            var state = new
            {
                _scroll.VerticalOffset, _scroll.ScrollableHeight, _scroll.ExtentHeight,
                _scroll.ViewportWidth, _scroll.ViewportHeight,
                _session.Anchor, AnchorOffset = _session.Offset(_session.Anchor), _session.Extent, _session.Options,
                Restore = Field(_reader, "_restore"), Queued = Field(_reader, "_queued"), Updating = Field(_reader, "_updating"),
                ExplicitPage = Field(_reader, "_explicitPage"), ExplicitOffset = Field(_reader, "_explicitScrollOffset")?.ToString(),
                InteractionVersion = Field(_reader, "_interactionVersion"),
                Focus = Describe(FocusManager.GetFocusedElement(_scroll.XamlRoot)),
                Geometry = Enumerable.Range(0, Math.Min(6, _session.Pages.Count)).Select(index => new
                {
                    Path = _session.Pages[index].FullPath, Top = _session.Top(index), Height = _session.Height(index)
                }).ToArray(), Rows = rows
            };
            var serialized = JsonSerializer.Serialize(state);
            if (changesOnly && serialized == _lastState) return;
            _lastState = serialized;
            if (_events.Count == 8192) { _events.Dequeue(); _dropped++; }
            _events.Enqueue(new { Milliseconds = _clock.Elapsed.TotalMilliseconds, Event = reason, Detail = detail, State = state });
        }

        private static string? Describe(object? element) => element is FrameworkElement framework
            ? $"{framework.GetType().Name}:{framework.Name}:{Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(framework)}"
            : element?.GetType().Name;
        private void ViewChanging(object? sender, ScrollViewerViewChangingEventArgs args) => Mark("ViewChanging",
            $"next={args.NextView.VerticalOffset}; final={args.FinalView.VerticalOffset}; inertial={args.IsInertial}\n{new StackTrace(1, false)}");
        private void ViewChanged(object? sender, ScrollViewerViewChangedEventArgs args) => Mark("ViewChanged", $"intermediate={args.IsIntermediate}");
        private void SizeChanged(object sender, SizeChangedEventArgs args) => Mark("SizeChanged", $"{args.PreviousSize} -> {args.NewSize}");
        private void LayoutUpdated(object? sender, object args) => Mark("LayoutUpdated", changesOnly: true);
        private void GettingFocus(UIElement sender, GettingFocusEventArgs args) => Mark("GettingFocus", $"{Describe(args.OldFocusedElement)} -> {Describe(args.NewFocusedElement)}; state={args.FocusState}");
        private void LosingFocus(UIElement sender, LosingFocusEventArgs args) => Mark("LosingFocus", $"{Describe(args.OldFocusedElement)} -> {Describe(args.NewFocusedElement)}; state={args.FocusState}");
        private void GotFocus(object sender, RoutedEventArgs args) => Mark("GotFocus", Describe(args.OriginalSource));
        private void LostFocus(object sender, RoutedEventArgs args) => Mark("LostFocus", Describe(args.OriginalSource));
        private void OperationStarted(object? sender, DispatcherHookEventArgs args) => Mark("DispatcherStarted", args.Operation.Priority.ToString(), changesOnly: true);
        private void OperationCompleted(object? sender, DispatcherHookEventArgs args) => Mark("DispatcherCompleted", args.Operation.Priority.ToString(), changesOnly: true);

        public void Dispose()
        {
            if (!_enabled) return;
            Mark("end");
            _scroll.ViewChanging -= ViewChanging;
            _scroll.ViewChanged -= ViewChanged;
            _scroll.SizeChanged -= SizeChanged;
            _scroll.LayoutUpdated -= LayoutUpdated;
            _scroll.GettingFocus -= GettingFocus;
            _scroll.LosingFocus -= LosingFocus;
            _scroll.GotFocus -= GotFocus;
            _scroll.LostFocus -= LostFocus;
            _reader.Dispatcher.Hooks.OperationStarted -= OperationStarted;
            _reader.Dispatcher.Hooks.OperationCompleted -= OperationCompleted;
            var path = Path.Combine(Root, _name + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { DroppedEvents = _dropped, Events = _events }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("Reader width-reflow diagnostics: " + path);
        }
    }
}
