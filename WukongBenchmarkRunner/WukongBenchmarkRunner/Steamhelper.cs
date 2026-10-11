using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WukongBenchmarkRunner;

public static class SteamHelper
{
    /// Находит папку установки приложения по appmanifest_{appId}.acf во всех библиотеках Steam.
    public static string? FindInstallDir(string appId)
    {
        var steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (string.IsNullOrEmpty(steamPath)) return null;

        var libraries = new List<string> { steamPath.Replace('/', '\\') };

        var vdf = Path.Combine(libraries[0], "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                libraries.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
        }

        foreach (var lib in libraries.Distinct())
        {
            var manifest = Path.Combine(lib, "steamapps", $"appmanifest_{appId}.acf");
            if (!File.Exists(manifest)) continue;

            var m = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
            if (!m.Success) continue;

            var dir = Path.Combine(lib, "steamapps", "common", m.Groups[1].Value);
            if (Directory.Exists(dir)) return dir;
        }
        return null;
    }
}

/// На ноутбуках с двумя видеокартами Windows может запустить игру на встроенной Intel UHD.
/// Это тот же переключатель, что в "Параметры → Дисплей → Графика → Высокая производительность".
/// После работы возвращаем прежнее значение.
public sealed class GpuPreference : IDisposable
{
    private const string KeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private readonly string _exe;
    private readonly string? _previous;

    public GpuPreference(string exePath)
    {
        _exe = exePath;
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        _previous = key.GetValue(exePath) as string;
        key.SetValue(exePath, "GpuPreference=2;", RegistryValueKind.String); // 2 = высокая производительность
    }

    public void Dispose()
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        if (_previous is null) key.DeleteValue(_exe, false);
        else key.SetValue(_exe, _previous, RegistryValueKind.String);
    }
}
