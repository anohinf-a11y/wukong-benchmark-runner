using System.Globalization;
using WukongBenchmarkRunner;

namespace WukongBench;

/// Вывод результатов в консоль выровненными таблицами. Ничего не сохраняется на диск.
public static class Reporter
{
    // Уровни качества в меню игры: 1..5
    private static readonly string[] Levels = { "Низкое", "Среднее", "Высокое", "Ультра", "Реалистичное" };

    // Отдельные группы качества. Если все они равны выбранному набору, выводим только набор.
    private static readonly (string Key, string Name)[] Groups =
    {
        ("ViewDistance", "Детализация объектов вдали"),
        ("AntiAliasing", "Качество сглаживания"),
        ("PostProcessing", "Качество постобработки"),
        ("ShadowQuality", "Качество теней"),
        ("TextureQuality", "Качество текстур"),
        ("MaterialQuality", "Качество материалов"),
        ("VegetationQuality", "Качество растительности"),
    };

    private static string F(double? v) =>
        v?.ToString("0.##", CultureInfo.CurrentCulture) ?? "—";

    private static string Level(string? raw) =>
        int.TryParse(raw, out var n) && n is >= 1 and <= 5 ? Levels[n - 1] : raw ?? "—";

    private static string OnOff(string? raw) => raw switch
    {
        "0" => "Выкл",
        "1" => "Вкл",
        _ => raw ?? "—"
    };

    private static string MotionBlur(string? raw) => raw switch
    {
        "0" => "Выкл",
        "1" => "Малозаметные",
        "2" => "Очень заметные",
        _ => raw ?? "—"
    };

    // Известен только код TSR; остальные показываем как есть, пока не выяснено соответствие
    private static string Upscaler(string? raw) => raw switch
    {
        "3" => "TSR",
        null => "—",
        _ => $"код {raw}"
    };

    public static void Print(Dictionary<string, string> system, List<RunResult> runs)
    {
        Console.WriteLine();

        Section("Характеристики компьютера");
        Table(new[] { "Параметр", "Значение" }, system.Select(kv => new[] { kv.Key, kv.Value }));

        var machine = runs.FirstOrDefault()?.Parsed.Machine;
        if (machine is { Count: > 0 })
        {
            Section("Система по данным бенчмарка");
            Table(new[] { "Параметр", "Значение" }, machine.Select(kv => new[] { kv.Key, kv.Value }));
        }

        var head = new[] { "Метрика" }.Concat(runs.Select(r => r.Profile.Name)).ToArray();

        Section("Результаты");
        var metrics = new (string Name, Func<BenchResult, string> Get)[]
        {
            ("Средний FPS", r => F(r.FpsAvg)),
            ("Максимальный FPS", r => F(r.FpsMax)),
            ("Минимальный FPS", r => F(r.FpsMin)),
            ("FPS 95% (Low 5th)", r => F(r.Fps95)),
            ("Загрузка CPU по данным бенчмарка, %", r => F(r.CpuUsageAvg)),
            ("Загрузка GPU по данным бенчмарка, %", r => F(r.GpuUsageAvg)),
            ("Время кадра CPU, мс", r => F(r.AvgCpuFrameTimeMs)),
            ("Время кадра GPU, мс", r => F(r.AvgGpuFrameTimeMs)),
            ("Ограничивало", r => r.Bottleneck ?? "—"),
            ("VRAM, ГБ", r => F(r.VideoMemGb)),
        };
        Table(head, metrics.Select(m =>
            new[] { m.Name }.Concat(runs.Select(r => m.Get(r.Parsed))).ToArray()));

        Section("Использованные настройки");
        Table(head.Select((h, i) => i == 0 ? "Настройка" : h).ToArray(), BuildSettingsRows(runs));
    }

    private static List<string[]> BuildSettingsRows(List<RunResult> runs)
    {
        var rows = new List<string[]>();

        void Row(string name, string key, Func<string?, string> format)
        {
            var values = runs.Select(r => r.Parsed.Settings.GetValueOrDefault(key)).ToList();
            if (values.All(v => v is null)) return;
            rows.Add(new[] { name }.Concat(values.Select(format)).ToArray());
        }

        Row("Набор настроек графики", "QualityLevel", Level);

        // Отдельные группы качества выводим только если они отличаются от выбранного набора
        bool groupsMatchPreset = runs.All(r =>
        {
            var s = r.Parsed.Settings;
            var preset = s.GetValueOrDefault("QualityLevel");
            return Groups.All(g => !s.TryGetValue(g.Key, out var v) || v == preset);
        });

        if (!groupsMatchPreset)
            foreach (var (key, name) in Groups)
                Row(name, key, Level);

        Row("Степень избыточной выборки сглаживания", "ImageQuality", v => v is null ? "—" : v + "%");
        Row("Избыточная выборка сглаживания", "Dlss", Upscaler);
        Row("Генерация кадров", "InsertFrame", OnOff);
        Row("Полная трассировка лучей NVIDIA", "Rtx", OnOff);
        Row("Размытие при движении", "MotionBlur", MotionBlur);
        Row("Разрешение экрана", "ScreenResolution", v => v ?? "—");

        return rows;
    }

    private static void Section(string title)
    {
        Console.WriteLine($"=== {title} ===");
        Console.WriteLine();
    }

    private static void Table(string[] head, IEnumerable<string[]> rows)
    {
        var all = rows.ToList();
        var widths = head.Select((h, i) =>
            Math.Max(h.Length, all.Count == 0 ? 0 : all.Max(r => r[i].Length))).ToArray();

        string Line(string[] cells) =>
            string.Join(" | ", cells.Select((c, i) => c.PadRight(widths[i]))).TrimEnd();

        Console.WriteLine(Line(head));
        Console.WriteLine(string.Join("-+-", widths.Select(w => new string('-', w))));
        foreach (var r in all) Console.WriteLine(Line(r));
        Console.WriteLine();
    }
}