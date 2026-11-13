Use C# and WPF/WinUI through XAML Islands.



Follow MVVM principles where appropriate.



Keep filesystem operations separate from UI code.



Use asynchronous operations for expensive tasks.



Avoid tightly coupled file handlers.



Write unit tests for business logic.



Always build the application after making changes.



Never silently overwrite or permanently delete files.



Do not implement features outside the approved specification without discussing them first.
Prefer suitable WinUI controls over WPF equivalents through XAML Islands for Windows-native behavior and consistent styling. Keep WPF as the application/window host and use WPF where its rendering or interop needs make it appropriate. Preserve focus, keyboard navigation, DPI handling, cancellation and clean island lifetimes.
