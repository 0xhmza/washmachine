# Classic Windows UI preview

Standalone demonstration using Windows Forms and native Windows controls. It is not connected to the application or its services. The Preview button updates sample status only; it does not read or write files.

On Windows with the .NET 8 SDK, run:

```powershell
dotnet run --project prototypes/ClassicUi/ClassicUi.csproj
```

The layout supports resizing, system fonts and DPI scaling. Use Alt+N and Alt+P for fields, Enter to preview, and Escape to close.
