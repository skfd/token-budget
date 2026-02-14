using System;
using System.IO;
using System.Text.Json;

namespace LlmTokenWidget.Providers.Copilot;

/// <summary>
/// Reads the GitHub PAT from ~/.config/llm-token-widget/copilot.json.
/// File format: { "token": "ghp_..." }
/// </summary>
public static class CopilotCredentialReader
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "llm-token-widget", "copilot.json");

    public static string? ReadToken()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return null;

            var json = File.ReadAllText(ConfigPath);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("token", out var token))
                return token.GetString();

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CopilotCredentialReader: {ex.Message}");
            return null;
        }
    }

    public static void SaveToken(string token)
    {
        var dir = Path.GetDirectoryName(ConfigPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(new { token });
        File.WriteAllText(ConfigPath, json);
    }

    public static bool CredentialsExist() => ReadToken() != null;
}
