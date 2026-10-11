using System.Diagnostics;
using System.Text.Json;

namespace WukongBenchmarkRunner;

/// Бенчмарк сам сохраняет результаты в JSON-файл без расширения (имя = unix-время):
/// %TEMP%\b1\BenchMarkHistory\Tool\1791583618
/// Поэтому для получения результатов не нужен ни OCR, ни скриншоты.
public static class BenchFile
{
    public static string HistoryDir =>
        Path.Combine(Path.GetTempPath(), "b1", "BenchMarkHistory", "Tool");

    private static string[] List() =>
        Directory.Exists(HistoryDir) ? Directory.GetFiles(HistoryDir) : Array.Empty<string>();

    /// Файлы, которые уже лежали в папке до запуска (чтобы потом найти именно новый).
    public static HashSet<string> Snapshot() =>
        new(List(), StringComparer.OrdinalIgnoreCase);

    public static bool HasNew(HashSet<string> before) =>
        List().Any(f => !before.Contains(f));

    public static async Task<BenchResult> WaitForNewAsync(HashSet<string> before, Process proc, int timeoutSec)
    {
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed.TotalSeconds < timeoutSec)
        {
            var created = List().Where(f => !before.Contains(f)).OrderBy(f => f).ToList();

            foreach (var file in created)
            {
                // ждём, пока игра дозапишет файл (размер перестаёт меняться)
                long size = new FileInfo(file).Length;
                await Task.Delay(1500);
                if (new FileInfo(file).Length != size) continue;

                var result = TryParse(file);
                if (result is { Samples: > 0 }) return result;
            }

            if (created.Count == 0 && proc.HasExited)
                throw new Exception("Файла с результатами нет.");

            await Task.Delay(2000);
        }

        throw new Exception("Превышено время ожидания результатов бенчмарка.");
    }

    public static BenchResult? TryParse(string path)
    {
        string text;
        try { text = File.ReadAllText(path); }
        catch (IOException) { return null; }

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("FPSAvg", out _))
                return null;

            var r = new BenchResult { SourceFile = path };

            foreach (var p in root.EnumerateObject())
            {
                switch (p.Name)
                {
                    case "FPSAvg": r.FpsAvg = Num(p.Value); break;
                    case "FPSMax": r.FpsMax = Num(p.Value); break;
                    case "FPSMin": r.FpsMin = Num(p.Value); break;
                    case "FPS95": r.Fps95 = Num(p.Value); break;
                    case "CPUAvg": r.CpuUsageAvg = Num(p.Value); break;
                    case "GPUAvg": r.GpuUsageAvg = Num(p.Value); break;
                    case "VideoMem": r.VideoMemGb = Num(p.Value); break;

                    case "GameVer": r.Machine["Версия игры"] = Str(p.Value); break;
                    case "SysVer": r.Machine["Версия ОС"] = Str(p.Value); break;
                    case "CPUModel": r.Machine["CPU"] = Str(p.Value); break;
                    case "GPUModel": r.Machine["GPU (использовалась в тесте)"] = Str(p.Value); break;
                    case "GpuDriverVer": r.Machine["Драйвер GPU"] = Str(p.Value); break;
                    case "VideoMemSize": r.Machine["VRAM"] = Str(p.Value); break;
                    case "SysMem": r.Machine["RAM"] = Str(p.Value); break;

                    case "Records": ReadRecords(p.Value, r); break;

                    case "TimeStamp":
                    case "BenckMarkVersion":
                    case "GPULeak":
                        break;

                    default:
                        // всё остальное - настройки графики (ScreenResolution, ShadowQuality, Dlss, ...)
                        if (p.Value.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object))
                            r.Settings[p.Name] = Str(p.Value);
                        break;
                }
            }
            return r;
        }
        catch (JsonException)
        {
            return null; // файл ещё дописывается
        }
    }

    private static void ReadRecords(JsonElement records, BenchResult r)
    {
        if (records.ValueKind != JsonValueKind.Array) return;

        double cpu = 0, gpu = 0;
        int n = 0;
        foreach (var rec in records.EnumerateArray())
        {
            if (rec.TryGetProperty("CPUFrameTime", out var c) && rec.TryGetProperty("GPUFrameTime", out var g)
                && c.ValueKind == JsonValueKind.Number && g.ValueKind == JsonValueKind.Number)
            {
                cpu += c.GetDouble();
                gpu += g.GetDouble();
                n++;
            }
        }

        r.Samples = records.GetArrayLength();
        if (n > 0)
        {
            r.AvgCpuFrameTimeMs = cpu / n;
            r.AvgGpuFrameTimeMs = gpu / n;
        }
    }

    private static double? Num(JsonElement e) =>
        e.ValueKind == JsonValueKind.Number ? e.GetDouble() : null;

    private static string Str(JsonElement e) =>
        e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.GetRawText();
}