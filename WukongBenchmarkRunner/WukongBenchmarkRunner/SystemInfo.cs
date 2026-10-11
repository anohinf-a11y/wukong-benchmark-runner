using System.Management;

namespace WukongBenchmarkRunner;

public static class SystemInfo
{
    private static List<Dictionary<string, object?>> Query(string cls, params string[] props)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {string.Join(",", props)} FROM {cls}");
            return searcher.Get().Cast<ManagementObject>()
                .Select(o => props.ToDictionary(p => p, p => (object?)o[p]))
                .ToList();
        }
        catch
        {
            return new List<Dictionary<string, object?>>();
        }
    }

    public static Dictionary<string, string> Collect()
    {
        var d = new Dictionary<string, string>();

        var computer = Query("Win32_ComputerSystem", "Manufacturer", "Model").FirstOrDefault();
        if (computer is not null)
            d["Компьютер"] = $"{computer["Manufacturer"]} {computer["Model"]}".Trim();

        foreach (var c in Query("Win32_Processor", "Name", "NumberOfCores", "NumberOfLogicalProcessors", "MaxClockSpeed"))
            d["CPU"] = $"{c["Name"]?.ToString()?.Trim()} ({c["NumberOfCores"]} ядер / {c["NumberOfLogicalProcessors"]} потоков, до {c["MaxClockSpeed"]} МГц)";

        // На ноутбуках будет две видеокарты: встроенная и дискретная. Какая использовалась - видно в данных самого бенчмарка.
        int i = 1;
        foreach (var g in Query("Win32_VideoController", "Name", "DriverVersion"))
            d[$"GPU {i++}"] = $"{g["Name"]} (драйвер {g["DriverVersion"]})";

        var modules = Query("Win32_PhysicalMemory", "Capacity", "Speed");
        if (modules.Count > 0)
        {
            long totalBytes = modules.Sum(m => Convert.ToInt64(m["Capacity"] ?? 0L));
            d["RAM"] = $"{totalBytes / (1024L * 1024 * 1024)} ГБ, модулей: {modules.Count}, {modules[0]["Speed"]} МТ/с";
        }

        var os = Query("Win32_OperatingSystem", "Caption", "Version").FirstOrDefault();
        if (os is not null)
            d["ОС"] = $"{os["Caption"]} ({os["Version"]})";

        // Для ноутбуков важно: от сети или от батареи шёл замер (на батарее частоты режутся)
        var battery = Query("Win32_Battery", "BatteryStatus").FirstOrDefault();
        if (battery is not null)
        {
            int status = Convert.ToInt32(battery["BatteryStatus"] ?? 0);
            d["Питание"] = status == 2 ? "от сети" : "ОТ БАТАРЕИ (результаты могут быть занижены)";
        }

        return d;
    }
}