using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ZapretGui.Services;

public class AppConfig
{
    public string CurrentVersion { get; set; } = string.Empty;
    public string Theme { get; set; } = "System";
    public string Language { get; set; } = "RU";
    public string LastSelectedPreset { get; set; } = string.Empty;
    public bool StartWithWindows { get; set; } = false;
    public bool AutoStartBypass { get; set; } = false;
    public int BypassModeIndex { get; set; } = 0;
}

public class ConfigService
{
    private static string ConfigPath => AppPaths.Config;

    public AppConfig LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                if (cfg != null)
                {
                    Debug.WriteLine($"[Config] Loaded from {ConfigPath}: version='{cfg.CurrentVersion}'");
                    return cfg;
                }
            }
            else
            {
                Debug.WriteLine($"[Config] Not found at {ConfigPath}, using defaults");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Config] Load failed: {ex.Message}");
        }

        return new AppConfig();
    }

    public void SaveConfig(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
            Debug.WriteLine($"[Config] Saved to {ConfigPath}: version='{config.CurrentVersion}'");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Config] Save failed: {ex.Message}");
        }
    }
}
