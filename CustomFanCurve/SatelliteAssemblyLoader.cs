using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace LenovoLegionToolkit.Plugin.CustomFanCurve;

internal static class SatelliteAssemblyLoader
{
    private static readonly string _prefix = $"{typeof(SatelliteAssemblyLoader).Namespace}.satellites.";

    [ModuleInitializer]
    public static void Initialize()
    {
        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
    }

    private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        var an = new AssemblyName(args.Name);
        if (an.CultureInfo is null || an.CultureInfo.Equals(CultureInfo.InvariantCulture))
            return null;

        var asm = Assembly.GetExecutingAssembly();
        var resourceName = $"{_prefix}{an.CultureInfo.Name}.{an.Name}.dll";

        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null) return null;

        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return Assembly.Load(bytes);
    }
}
