using System.Text.RegularExpressions;

namespace WukongBenchmarkRunner;

public static class IniEditor
{
    private const string MainSection = "[/Script/GSGameSettings.GSGameUserSettings]";

    /// Ищет GameUserSettings.ini, в котором есть строка UISettingData (т.е. именно конфиг бенчмарка).
    public static string? FindIni(params string?[] roots)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };

        foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)))
            foreach (var file in Directory.EnumerateFiles(root!, "GameUserSettings.ini", options))
            {
                try
                {
                    if (File.ReadLines(file).Any(l => l.StartsWith("UISettingData=")))
                        return file;
                }
                catch (IOException) { /* файл занят - пропускаем */ }
            }
        return null;
    }

    public static void Apply(string path, Profile profile)
    {
        var lines = File.ReadAllLines(path).ToList();

        // 1) Пары ("Ключ", "Значение") внутри одной длинной строки UISettingData=(...)
        int ui = lines.FindIndex(l => l.StartsWith("UISettingData="));
        if (ui < 0) throw new Exception("В ini нет строки UISettingData.");

        foreach (var (key, value) in profile.Ui)
        {
            var re = new Regex($"\\(\"{Regex.Escape(key)}\",\\s*\"[^\"]*\"\\)");
            if (!re.IsMatch(lines[ui]))
                throw new Exception($"Ключ \"{key}\" не найден в UISettingData.");
            lines[ui] = re.Replace(lines[ui], _ => $"(\"{key}\", \"{value}\")");
        }

        // 2) Обычные строки key=value. null = удалить строку целиком.
        foreach (var (key, value) in profile.Plain)
        {
            int i = lines.FindIndex(l => l.StartsWith(key + "=", StringComparison.Ordinal));

            if (value is null)
            {
                if (i >= 0) lines.RemoveAt(i);
            }
            else if (i >= 0)
            {
                lines[i] = $"{key}={value}";
            }
            else if (key.StartsWith("sg."))
            {
                throw new Exception($"Ключ \"{key}\" не найден в [ScalabilityGroups].");
            }
            else
            {
                // например DesiredScreenWidth, которого нет после GPU-профиля: вставляем в основную секцию
                int header = lines.FindIndex(l => l.Trim() == MainSection);
                lines.Insert(header >= 0 ? header + 1 : 0, $"{key}={value}");
            }
        }

        File.WriteAllLines(path, lines);
    }
}
