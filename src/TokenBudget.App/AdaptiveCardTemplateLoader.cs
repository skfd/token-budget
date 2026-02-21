using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace TokenBudget.App;

public static class AdaptiveCardTemplateLoader
{
    private static readonly Dictionary<string, string> _templateCache = new();
    private static readonly object _lock = new();

    public static string LoadTemplate(string templateName)
    {
        lock (_lock)
        {
            if (_templateCache.TryGetValue(templateName, out var cached))
            {
                return cached;
            }

            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = $"TokenBudget.App.AdaptiveCards.{templateName}";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new FileNotFoundException($"Embedded resource not found: {resourceName}");
            }

            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();
            _templateCache[templateName] = content;
            return content;
        }
    }
}
