# LenovoLegionToolkit Plugins

Modular, DLL-based plugins that extend the core functionality of Lenovo Legion Toolkit (LLT).

## Available Plugins

### CustomFanCurve
Provides fine-grained control over the cooling system on supported Legion devices via WMI. Features include CPU/GPU/System fan monitoring, custom curve adjustments, and native `.resx` localization.

## Developing a Plugin

To create a new plugin for LLT:

1. **Create a .NET Class Library**: Target the same .NET framework as the core LLT application (e.g., `net9.0-windows10.0.26100`).
2. **Add Project References**: Reference `LenovoLegionToolkit.Lib` and `LenovoLegionToolkit.WPF` in your `.csproj`. Set `<Private>false</Private>` and `<ExcludeAssets>runtime</ExcludeAssets>` to prevent bundling core dependencies into your plugin.
3. **Implement `IExtensionProvider`**: Create a public class that implements `LenovoLegionToolkit.Lib.Station.Core.IExtensionProvider`. The application discovers plugins by scanning for this interface at load time.
4. **Register Navigation**: In your `Initialize(IExtensionContext context)` method, register your UI pages via `context.Navigation.Register()`. Use `TitleGetter` to return localized strings from your `.resx` files.

Example `Provider.cs`:
```csharp
using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Station.Core;
using LenovoLegionToolkit.Lib.Station.Services;

namespace MyPlugin;

public sealed class MyPluginProvider : IExtensionProvider
{
    public void Initialize(IExtensionContext context)
    {
        context.Navigation.Register(new ExtensionNavigationItem
        {
            Id = "my-plugin-id",
            TitleGetter = () => "My Plugin Name",
            PageTag = "my_plugin_tag",
            PageType = typeof(MyPluginPage), // Must be a WPF Page/UserControl
            Icon = ExtensionIcon.Gauge
        });
    }

    public Task ExecuteAsync(string action, params object[] args) => Task.CompletedTask;
    
    public object? GetData(string key) => null;
    
    public void SetData(string key, object? value) { }
    
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

## Building

### Prerequisites
1. [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
2. The core `LenovoLegionToolkit` repository must be cloned alongside this repository.

### Build All Plugins
```bash
dotnet build LenovoLegionToolkit-Plugins.slnx -c Release
```

### Build a Single Plugin
```bash
dotnet build CustomFanCurve/CustomFanCurve.csproj -c Release
dotnet build AssemblyVersionPatcher/AssemblyVersionPatcher.csproj -c Release
```

### Publish a Plugin for Deployment
```bash
dotnet publish CustomFanCurve/CustomFanCurve.csproj -c Release -o out/Plugins/CustomFanCurve
```

Once compiled, deploy your `PluginName.dll` and its culture resource folders to the LLT plugins directory:
`%LOCALAPPDATA%\LenovoLegionToolkit\Plugins\PluginName\`

## Localization

Translations are handled via standard .NET Satellite Assemblies.

1. Navigate to the `Resources` directory within the plugin project.
2. Duplicate `Resource.resx` and rename it for the target culture (e.g., `Resource.zh-hans.resx`).
3. Add the IETF language tag to the `<SatelliteResourceLanguages>` property in the plugin's `.csproj`.
4. Compile the plugin to generate the culture-specific satellite assemblies.
