using System.Diagnostics;

namespace WukongBenchmarkRunner;

public static class Runner
{
    public static async Task<RunResult> RunOnce(Config cfg, Profile profile)
    {
        var before = BenchFile.Snapshot();

        // 1) запуск через Steam
        Process.Start(new ProcessStartInfo($"steam://rungameid/{cfg.AppId}") { UseShellExecute = true });
        var proc = await WaitForProcess(cfg.ProcessPrefix, cfg.LaunchTimeoutSec);

        // 2) "нажмите любую клавишу" -> главное меню -> первая кнопка (Benchmark)
        Console.WriteLine("Бенчмарк запущен, прохожу меню...");
        await Keyboard.RunStepsAsync(proc, cfg.StartSteps);

        // 3) ждём файл результатов, который бенчмарк сам пишет в %TEMP%
        Console.WriteLine("Идёт тест...");
        var parsed = await BenchFile.WaitForNewAsync(before, proc, cfg.BenchmarkTimeoutSec);

        Console.WriteLine("Тест завершён.");

        // 4) закрываем игру
        await CloseGame(cfg, proc);

        return new RunResult(profile, parsed);
    }

    private static Process? FindProcess(string prefix) =>
        Process.GetProcesses().FirstOrDefault(p =>
            p.ProcessName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    public static bool IsRunning(string prefix) => FindProcess(prefix) is not null;

    private static async Task<Process> WaitForProcess(string prefix, int timeoutSec)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < timeoutSec)
        {
            var p = FindProcess(prefix);
            if (p is not null) return p;
            await Task.Delay(1000);
        }
        throw new Exception("Бенчмарк не запустился. Проверь, что Steam запущен и бенчмарк установлен.");
    }

    /// Сначала корректный выход через меню (Esc -> подтверждение -> любая клавиша -> Exit -> подтверждение).
    /// Если игра не закрылась за ExitTimeoutSec (например, меню отличается), завершаем процесс принудительно.
    /// К этому моменту результаты уже сохранены в файл, так что ничего не теряется.
    private static async Task CloseGame(Config cfg, Process proc)
    {
        if (cfg.GracefulExit)
        {
            Console.WriteLine("Закрываю игру через меню...");
            try
            {
                await Keyboard.RunStepsAsync(proc, cfg.ExitSteps);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(cfg.ExitTimeoutSec));
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Игра не закрылась сама, завершаю принудительно.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Выход через меню не удался ({ex.Message}), завершаю принудительно.");
            }
        }

        try
        {
            if (!proc.HasExited)
            {
                proc.Kill(true);
                await proc.WaitForExitAsync();
            }
        }
        catch { /* уже закрылась */ }

        // дождаться, пока не останется процессов бенчмарка, и дать Steam прийти в себя
        for (int i = 0; i < 20 && IsRunning(cfg.ProcessPrefix); i++)
            await Task.Delay(500);
        await Task.Delay(4000);
    }
}