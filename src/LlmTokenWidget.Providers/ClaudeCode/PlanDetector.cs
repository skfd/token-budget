using System.Text.Json;
using LlmTokenWidget.Core;

namespace LlmTokenWidget.Providers.ClaudeCode;

/// <summary>
/// Reads widget configuration from a JSON file.
/// Config location: %USERPROFILE%\.claude\llm-widget-config.json
///
/// Expected format:
/// {
///   "plan": "Max5"       // "Pro", "Max5", or "Max20"
/// }
///
/// If the file doesn't exist, creates it with a default of "Pro".
/// </summary>
public sealed class WidgetConfig
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude",
        "llm-widget-config.json");

    /// <summary>
    /// Load plan configuration. Creates a default config file if none exists.
    /// </summary>
    public DetectedPlan Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                CreateDefaultConfig();
                return new DetectedPlan(PlanTier.Pro, PlanLimits.Pro,
                    $"Created default config at {ConfigPath} — edit to change plan");
            }

            using var stream = new FileStream(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var doc = JsonDocument.Parse(stream);

            var planStr = "Pro";
            if (doc.RootElement.TryGetProperty("plan", out var planProp))
            {
                planStr = planProp.GetString() ?? "Pro";
            }

            var (tier, limit) = ParsePlan(planStr);
            return new DetectedPlan(tier, limit, $"Loaded from config: {planStr}");
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            System.Diagnostics.Debug.WriteLine($"WidgetConfig: Failed to read config: {ex.Message}");
            return new DetectedPlan(PlanTier.Pro, PlanLimits.Pro,
                $"Config read error, defaulting to Pro: {ex.Message}");
        }
    }

    private static (PlanTier tier, long limit) ParsePlan(string planStr)
    {
        return planStr.Trim().ToLowerInvariant() switch
        {
            "pro" => (PlanTier.Pro, PlanLimits.Pro),
            "max5" => (PlanTier.Max5, PlanLimits.Max5),
            "max20" => (PlanTier.Max20, PlanLimits.Max20),
            _ => (PlanTier.Pro, PlanLimits.Pro)
        };
    }

    private static void CreateDefaultConfig()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = """
                {
                  "plan": "Pro"
                }
                """;
            File.WriteAllText(ConfigPath, json);
            System.Diagnostics.Debug.WriteLine($"WidgetConfig: Created default config at {ConfigPath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WidgetConfig: Failed to create default config: {ex.Message}");
        }
    }
}

/// <summary>
/// Known subscription plan tiers.
/// </summary>
public enum PlanTier
{
    Pro,
    Max5,
    Max20
}

/// <summary>
/// Result of plan configuration loading.
/// </summary>
public record DetectedPlan(
    PlanTier Tier,
    long TokenLimit,
    string Reason);
