using System.Windows.Input;
using Microsoft.UI.Xaml;
using Windows.System;
using Windows.UI.Core;

namespace MediFiler3.Interop;

/// <summary>Route island keys before native controls consume navigation input.</summary>
internal static class WinUiViewKeyboard
{
    public static void Attach(UIElement root, Func<Key, ModifierKeys, bool, bool> handle)
    {
        root.PreviewKeyDown += (_, args) =>
        {
            if (args.Handled) return;
            ModifierKeys modifiers = ModifierKeys.None;
            bool Down(VirtualKey key) => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;
            if (Down(VirtualKey.Control)) modifiers |= ModifierKeys.Control;
            if (Down(VirtualKey.Shift)) modifiers |= ModifierKeys.Shift;
            if (Down(VirtualKey.Menu)) modifiers |= ModifierKeys.Alt;
            if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) modifiers |= ModifierKeys.Windows;
            args.Handled = handle(KeyInterop.KeyFromVirtualKey((int)args.Key), modifiers, args.KeyStatus.WasKeyDown);
        };
    }
}
