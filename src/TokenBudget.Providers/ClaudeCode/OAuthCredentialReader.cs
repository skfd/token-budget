using System;
using System.IO;
using System.Text.Json;

namespace TokenBudget.Providers.ClaudeCode;

/// <summary>
/// Reads the Claude Code OAuth access token from ~/.claude/.credentials.json.
/// </summary>
public static class OAuthCredentialReader
{
    private static readonly string CredentialsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", ".credentials.json");

    /// <summary>
    /// Reads the OAuth access token. Returns null if the file doesn't exist
    /// or the token field is missing.
    /// </summary>
    public static string? ReadAccessToken()
    {
        try
        {
            if (!File.Exists(CredentialsPath))
                return null;

            var json = File.ReadAllText(CredentialsPath);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth) &&
                oauth.TryGetProperty("accessToken", out var token))
            {
                return token.GetString();
            }

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OAuthCredentialReader: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Whether the credentials file exists at all.
    /// </summary>
    public static bool CredentialsExist() => File.Exists(CredentialsPath);
}
