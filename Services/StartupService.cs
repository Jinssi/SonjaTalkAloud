using System.IO;
using System.Reflection;
using Microsoft.Win32;
using Sonja.ReadAloud.Models;

namespace Sonja.ReadAloud.Services;

public sealed class StartupService
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Sonja.ReadAloud";

    public OperationResult Configure(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true);

            if (enabled)
            {
                key.SetValue(ValueName, BuildStartupCommand(), RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Windows startup could not be updated: {ex.Message}");
        }
    }

    private static string BuildStartupCommand()
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The application path is unavailable.");
        var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;

        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(entryAssemblyPath))
        {
            return $"\"{processPath}\" \"{entryAssemblyPath}\"";
        }

        return $"\"{processPath}\"";
    }
}