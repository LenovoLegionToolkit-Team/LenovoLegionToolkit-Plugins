# LenovoLegionToolkit Plugins

Modular, DLL-based plugins that extend the core functionality of Lenovo Legion Toolkit (LLT).

## Available Plugins

### CustomFanCurve
Provides advanced, algorithm-driven control over the cooling system on supported Legion devices via WMI. 

**Features:**
- CPU/GPU/System fan monitoring with custom UI curve adjustments.
- **Predictive Engine:** Calculus-based derivative lookahead ($dT/dt$) to proactively spool up fans before thermal saturation.
- **Acoustic Tuning:** Harmonic interference prevention that mathematically shifts fan RPMs to cancel out annoying beat frequencies.
- **Thermal Smoothing & Hysteresis:** Exponential Moving Average (EMA) and deadzone buffers to prevent rapid fan cycling and jitter.
- **Smart Auto Mode:** Fuzzy-logic powered auto control that smoothly scales fans using continuous thermal and power load gradients.
- **Asymmetric Step-Down:** Smooth fan deceleration curves to mitigate audible chopping and prevent physical bearing wear.

# Screenshots

### CustomFanCurve
<img src="assets/Dashboard.png" height="320" /> <img src="assets/Settings.png" height="320" />

## Developing a Plugin

To create a new plugin for LLT:

1. **Create a .NET Class Library**: Target the same .NET framework as the core LLT application (e.g., `net9.0-windows10.0.26100`).
2. **Add Project References**: Reference `LenovoLegionToolkit.Lib` in your `.csproj`. Set `<Private>false</Private>` and `<ExcludeAssets>runtime</ExcludeAssets>` to prevent bundling core dependencies into your plugin.
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
2. The core `LenovoLegionToolkit` repository must be cloned locally.
3. Set the `LLT_SOURCE` environment variable to point to the core repo root.

```bash
# System environment variable (recommended)
setx LLT_SOURCE "C:\Path\To\LenovoLegionToolkit"

# Or per-session
set LLT_SOURCE=C:\Path\To\LenovoLegionToolkit
```

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

Once compiled, deploy your single `PluginName.dll` file to the LLT plugins directory (all language resources are automatically embedded into the DLL):
`%LOCALAPPDATA%\LenovoLegionToolkit\Plugins\PluginName\`

## Localization

Translations are handled via standard .NET Satellite Assemblies.

1. Navigate to the `Resources` directory within the plugin project.
2. Duplicate `Resource.resx` and rename it for the target culture (e.g., `Resource.zh-hans.resx`).
3. Add the IETF language tag to the `<SatelliteResourceLanguages>` property in the plugin's `.csproj`.
4. Compile the plugin. The custom MSBuild target will automatically generate and embed the culture-specific satellite assemblies directly into your main `.dll`.

## Automated Releases

Releases are fully automated via GitHub Actions to ensure clean and consistent plugin packaging.

1. Navigate to the **Actions** tab on GitHub.
2. Select **Publish Release** on the left menu.
3. Click **Run workflow**, choose your target channel (`Stable` or `Beta`), and enter the tag you'd like to use (e.g. `latest` or `v1.2`).

The GitHub Action will automatically:
- Spin up a runner and pull the core application dependencies.
- Compile all plugins in the repository and embed their localizations.
- Zip the core `.dll` of each plugin independently.
- Detect which plugin folders were modified since the last release tag.
- Draft a new GitHub Release with the updated plugins automatically listed in the changelog, pending manual publication.
