using System.Runtime.InteropServices;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Windows.System;
using Windows.UI.Core;

namespace MediFiler3.Interop;

/// <summary>Route island keys before native controls consume navigation input.</summary>
internal static class WinUiViewKeyboard
{
    public static bool FocusControl(nint island, Microsoft.UI.Xaml.Controls.Control? control)
    {
        if (island == 0 || control is not { IsLoaded: true }) return false;
        SetFocus(island);
        return control.Focus(FocusState.Programmatic);
    }

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint window);

    public static bool HasTextInputFocus(nint island, XamlRoot? root)
    {
        var focused = GetFocus();
        return island != 0 && (focused == island || IsChild(island, focused)) && root is not null &&
            Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root) is
                Microsoft.UI.Xaml.Controls.TextBox or Microsoft.UI.Xaml.Controls.PasswordBox or Microsoft.UI.Xaml.Controls.RichEditBox;
    }

    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsChild(nint parent, nint child);

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
