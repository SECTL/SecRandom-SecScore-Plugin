using System.Text.Json;

namespace SecScoreLauncher.Services;

/// <summary>
///     Plugin settings: the path to the SecScore executable used when the target is not running.
///     Persisted as <c>settings.json</c> under the plugin's config folder. When the path is empty, a
///     best-effort auto-detection is used at launch time.
/// </summary>
public sealed class SecScoreLauncherConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _configFilePath;

    public SecScoreLauncherConfig(string configFolder)
    {
        _configFilePath = System.IO.Path.Combine(configFolder, "settings.json");
        Load();
    }

    /// <summary>SecScore executable path set by the user. Empty means "auto-detect".</summary>
    public string ExePath { get; set; } = string.Empty;

    /// <summary>Tooltip label of the floating-window button.</summary>
    public string ButtonLabel { get; set; } = "积分";

    private void Load()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var parsed = JsonSerializer.Deserialize<SecScoreLauncherConfig>(File.ReadAllText(_configFilePath));
                if (parsed is not null)
                {
                    ExePath = parsed.ExePath ?? string.Empty;
                    ButtonLabel = string.IsNullOrWhiteSpace(parsed.ButtonLabel) ? "SecScore" : parsed.ButtonLabel!;
                }
            }
        }
        catch
        {
            // Fall back to defaults if the settings file is unreadable/corrupt.
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_configFilePath)!);
            File.WriteAllText(_configFilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Persisting settings is best-effort; never crash the plugin for a write failure.
        }
    }

    /// <summary>Resolves the SecScore executable to launch, preferring the user-set path.</summary>
    public string? ResolveExecutable()
    {
        var setPath = ExePath.Trim();
        if (setPath.Length > 0 && File.Exists(setPath))
            return setPath;

        return AutoDetectPath();
    }

    /// <summary>Returns the first existing auto-detected SecScore executable, or <c>null</c>.</summary>
    public static string? AutoDetectPath()
    {
        return AutoDetectCandidates().FirstOrDefault(File.Exists);
    }

    /// <summary>Candidate locations probed when the user has not configured a path.</summary>
    private static IEnumerable<string> AutoDetectCandidates()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var productDir = System.IO.Path.Combine(System.IO.Path.GetFullPath(AppContext.BaseDirectory), "..", "SecScore");

        yield return System.IO.Path.Combine(programFiles, "SecScore", "SecScore.exe");
        yield return System.IO.Path.Combine(programFilesX86, "SecScore", "SecScore.exe");
        yield return System.IO.Path.Combine(localAppData, "Programs", "SecScore", "SecScore.exe");
        // Portable layout: a sibling "SecScore" folder next to the running SecRandom.
        yield return System.IO.Path.Combine(productDir, "SecScore.exe");
        // Development builds on the machine that cloned SecScore.
        yield return @"D:\code\SecScore\src-tauri\target\release\SecScore.exe";
        yield return @"D:\code\SecScore\src-tauri\target\debug\SecScore.exe";
    }
}
