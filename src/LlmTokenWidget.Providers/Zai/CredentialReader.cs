using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlmTokenWidget.Providers.Zai;

public static class CredentialReader
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static string? GetApiKey()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var authPath = Path.Combine(userProfile, ".local", "share", "opencode", "auth.json");

        if (!File.Exists(authPath))
            return null;

        try
        {
            var json = File.ReadAllText(authPath);
            var auth = JsonSerializer.Deserialize<AuthFile>(json, s_jsonOptions);
            
            if (auth?.ZaiCodingPlan?.Key != null)
                return auth.ZaiCodingPlan.Key;
            
            if (auth?.Zai?.Key != null)
                return auth.Zai.Key;

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to read auth.json: {ex.Message}");
            return null;
        }
    }

    public static bool CredentialsExist()
    {
        return GetApiKey() != null;
    }
}

public sealed class AuthFile
{
    [JsonPropertyName("zai")]
    public AuthEntry? Zai { get; set; }

    [JsonPropertyName("zai-coding-plan")]
    public AuthEntry? ZaiCodingPlan { get; set; }
}

public sealed class AuthEntry
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("key")]
    public string? Key { get; set; }
}
