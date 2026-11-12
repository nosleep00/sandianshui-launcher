namespace SandianshuiLauncher.Core.Utils;

/// <summary>
/// Maven 坐标解析：group:artifact:version[:classifier][@extension]
/// </summary>
public sealed record MavenCoordinate
{
    public required string Group { get; init; }
    public required string Artifact { get; init; }
    public required string Version { get; init; }
    public string? Classifier { get; init; }
    public string Extension { get; init; } = "jar";

    /// <summary>该构件在 Maven 仓库中的相对路径。</summary>
    public string ToRepositoryPath()
    {
        string groupPath = Group.Replace('.', '/');
        string file = $"{Artifact}-{Version}";
        if (!string.IsNullOrEmpty(Classifier))
        {
            file += $"-{Classifier}";
        }
        file += $".{Extension}";
        return $"{groupPath}/{Artifact}/{Version}/{file}";
    }

    /// <summary>去掉 classifier 后的主体坐标（用于库去重）。</summary>
    public string BaseKey => $"{Group}:{Artifact}:{Version}";
}

/// <summary>Maven 坐标解析工具。</summary>
public static class MavenName
{
    public static MavenCoordinate Parse(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string extension = "jar";
        string coordinate = name;

        int at = name.IndexOf('@');
        if (at >= 0)
        {
            extension = name[(at + 1)..];
            coordinate = name[..at];
        }

        string[] parts = coordinate.Split(':');
        if (parts.Length is < 3 or > 5)
        {
            throw new FormatException($"无法识别的 Maven 坐标：{name}");
        }

        string? classifier = parts.Length >= 4 && !string.IsNullOrEmpty(parts[3]) ? parts[3] : null;

        return new MavenCoordinate
        {
            Group = parts[0],
            Artifact = parts[1],
            Version = parts[2],
            Classifier = classifier,
            Extension = extension
        };
    }

    /// <summary>classifier 是否为原生库（如 natives-windows、natives-windows-x86_64）。</summary>
    public static bool IsNatives(string? classifier) =>
        classifier is not null && classifier.StartsWith("natives-", StringComparison.OrdinalIgnoreCase);
}
