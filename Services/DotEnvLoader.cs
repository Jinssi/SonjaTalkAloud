using System.IO;

namespace Sonja.ReadAloud.Services;

public static class DotEnvLoader
{
    public static IReadOnlyDictionary<string, string> Load()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var path = FindDotEnv();
        if (path is not null)
        {
            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
                {
                    line = line[7..].TrimStart();
                }

                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var name = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim();
                if (value.Length >= 2 &&
                    ((value[0] == '"' && value[^1] == '"') ||
                     (value[0] == '\'' && value[^1] == '\'')))
                {
                    value = value[1..^1];
                }

                values[name] = value;
            }
        }

        foreach (System.Collections.DictionaryEntry variable in Environment.GetEnvironmentVariables())
        {
            if (variable.Key is string name && variable.Value is string value)
            {
                values[name] = value;
            }
        }

        return values;
    }

    private static string? FindDotEnv()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddWithParents(candidates, Directory.GetCurrentDirectory());
        AddWithParents(candidates, AppContext.BaseDirectory);

        return candidates.FirstOrDefault(File.Exists);
    }

    private static void AddWithParents(ISet<string> candidates, string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        for (var depth = 0; directory is not null && depth < 6; depth++, directory = directory.Parent)
        {
            candidates.Add(Path.Combine(directory.FullName, ".env"));
        }
    }
}