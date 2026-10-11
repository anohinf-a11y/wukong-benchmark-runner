using System.Text;
using System.Text.Json;
using WukongBenchmarkRunner;

namespace WukongBench;

public static class Program
{
    private static readonly JsonSerializerOptions JsonRead = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static async Task<int> Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            return await Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("\nОШИБКА: " + ex.Message);
            return 1;
        }
    }

    private static async Task<int> Run()
    {
        var cfgPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        var cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(cfgPath), JsonRead)
                  ?? throw new Exception("Не удалось прочитать config.json");

        if (Runner.IsRunning(cfg.ProcessPrefix))
            throw new Exception("Бенчмарк уже запущен. Закройте его и запустите инструмент снова.");

        // --- Поиск установки и конфига ---
        var installDir = SteamHelper.FindInstallDir(cfg.AppId);
        var exe = installDir is null
            ? null
            : Directory.EnumerateFiles(installDir, "b1-Win64-Shipping.exe", SearchOption.AllDirectories).FirstOrDefault();

        var localB1 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "b1");
        var iniPath = IniEditor.FindIni(localB1, installDir)
            ?? throw new Exception("GameUserSettings.ini бенчмарка не найден. Запустите бенчмарк вручную один раз и закройте его.");

        if (exe is null)
            Console.WriteLine("! exe бенчмарка не найден - принудительный выбор дискретной видеокарты пропущен.");

        var system = SystemInfo.Collect();
        if (system.TryGetValue("Питание", out var power) && power.StartsWith("ОТ БАТАРЕИ"))
            Console.WriteLine("! Ноутбук работает от батареи. Результаты будут занижены.");

        // --- Бэкап исходных настроек (восстанавливаем даже при ошибке) ---
        var backup = iniPath + ".wukongbench.bak";
        if (File.Exists(backup)) File.Copy(backup, iniPath, true);
        else File.Copy(iniPath, backup);

        var runs = new List<RunResult>();
        using var gpuPref = exe is null ? null : new GpuPreference(exe);

        try
        {
            foreach (var profile in cfg.Profiles)
            {
                Console.WriteLine($"\n=== {profile.Name} ===");
                IniEditor.Apply(iniPath, profile);
                runs.Add(await Runner.RunOnce(cfg, profile));
            }
        }
        finally
        {
            File.Copy(backup, iniPath, true);
            File.Delete(backup);
            Console.WriteLine("\nИсходные настройки бенчмарка восстановлены.");
        }

        Reporter.Print(system, runs);
        return 0;
    }
}