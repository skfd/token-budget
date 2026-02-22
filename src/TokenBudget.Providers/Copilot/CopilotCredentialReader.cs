using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace TokenBudget.Providers.Copilot;

/// <summary>
/// Reads the GitHub token from gh CLI (gh auth token) or fallback config.
/// </summary>
public static class CopilotCredentialReader
{
    private static readonly string FallbackConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "token-budget", "copilot.json");

    private static string? _cachedToken;
    private static bool _triedGh;

    public static string? ReadToken()
    {
        // Try gh CLI first (cached)
        if (!_triedGh)
        {
            _triedGh = true;
            _cachedToken = ReadFromGhCli();
            if (!string.IsNullOrEmpty(_cachedToken))
                return _cachedToken;
        }
        else if (!string.IsNullOrEmpty(_cachedToken))
        {
            return _cachedToken;
        }

        // Fallback to manual config
        return ReadFromFallback();
    }

    private static string? ReadFromGhCli()
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "gh",
                    Arguments = "auth token",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            proc.Start();
            
            // Timeout after 2 seconds to prevent hanging Store reviewer environments
            // if gh CLI acts unexpectedly or prompts for login.
            if (!proc.WaitForExit(2000))
            {
                proc.Kill();
                return null;
            }

            var token = proc.StandardOutput.ReadToEnd().Trim();

            return proc.ExitCode == 0 && !string.IsNullOrEmpty(token) ? token : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // gh CLI not installed or not in PATH, completely normal
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CopilotCredentialReader (gh): {ex.Message}");
            return null;
        }
    }

    private static string? ReadFromFallback()
    {
        try
        {
            if (!File.Exists(FallbackConfigPath))
                return null;

            var json = File.ReadAllText(FallbackConfigPath);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("token", out var token))
                return token.GetString();

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CopilotCredentialReader (fallback): {ex.Message}");
            return null;
        }
    }

    public static void SaveToken(string token)
    {
        var dir = Path.GetDirectoryName(FallbackConfigPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(new { token });
        File.WriteAllText(FallbackConfigPath, json);
    }

    public static bool CredentialsExist() => ReadToken() != null;
}
