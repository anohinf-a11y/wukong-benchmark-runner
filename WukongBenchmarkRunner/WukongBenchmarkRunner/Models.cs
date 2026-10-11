namespace WukongBenchmarkRunner;

/// Нажатие клавиши: подождать DelayBeforeMs, затем нажать Key (Repeat раз с паузой RepeatDelayMs).
public record KeyStep(string Key, int DelayBeforeMs, int Repeat = 1, int RepeatDelayMs = 1500,
                      double X = 0, double Y = 0);

public record Config(
    string AppId,
    string ProcessPrefix,
    int LaunchTimeoutSec,
    int BenchmarkTimeoutSec,
    bool GracefulExit,
    int ExitTimeoutSec,
    List<KeyStep> StartSteps,
    List<KeyStep> ExitSteps,
    List<Profile> Profiles);

/// Ui    - пары ("Ключ", "Значение") внутри строки UISettingData
/// Plain - обычные строки key=value (в т.ч. sg.*). null = удалить строку.
public record Profile(
    string Id,
    string Name,
    string Rationale,
    Dictionary<string, string> Ui,
    Dictionary<string, string?> Plain);

/// Результат одного прохода, прочитанный из файла %TEMP%\b1\BenchMarkHistory\Tool\&lt;timestamp&gt;.
public class BenchResult
{
    public double? FpsAvg { get; set; }
    public double? FpsMax { get; set; }
    public double? FpsMin { get; set; }
    public double? Fps95 { get; set; }          // "Low 5th" на экране результатов
    public double? CpuUsageAvg { get; set; }    // %
    public double? GpuUsageAvg { get; set; }    // %
    public double? VideoMemGb { get; set; }

    // Считаются по массиву Records
    public int Samples { get; set; }
    public double? AvgCpuFrameTimeMs { get; set; }
    public double? AvgGpuFrameTimeMs { get; set; }

    /// Железо, как его увидел бенчмарк (в т.ч. какая видеокарта реально использовалась).
    public Dictionary<string, string> Machine { get; set; } = new();

    /// Настройки графики в том виде, в каком их записал бенчмарк.
    public Dictionary<string, string> Settings { get; set; } = new();

    public string SourceFile { get; set; } = "";

    /// Что ограничивало производительность: сравнение среднего времени кадра CPU и GPU.
    public string? Bottleneck =>
        AvgCpuFrameTimeMs is null || AvgGpuFrameTimeMs is null
            ? null
            : AvgGpuFrameTimeMs > AvgCpuFrameTimeMs ? "GPU" : "CPU";
}

public record RunResult(Profile Profile, BenchResult Parsed);