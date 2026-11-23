using System.Text.Json;
using System.Text.RegularExpressions;
using SupplyGuard.Infrastructure.Security;
using Xunit;

namespace SupplyGuard.IntegrationTests;

public sealed class PublicSecretsTests
{
    [Theory]
    [InlineData("<YOUR_JWT_SIGNING_KEY>")]
    [InlineData("Password=<your_db_password>")]
    [InlineData("_GcthTQLLqu66DxMGt9sY6lpu6fuHmlxbTBlBJPG9qkqQAyPo7b-7mKLqWkOUc3l")]
    [InlineData("development-only-signing-key-change-before-production-2026")]
    [InlineData("SuperSecretKeyForSupplyGuard_2026_TestEnvironment!")]
    [InlineData(" SupplyGuard_Admin2026! ")]
    [InlineData("Sg!9aA-oNizVpOKGc-sLvjZq77_FvO4RFj049N-")]
    [InlineData("ChangeThis!DevelopmentAdmin2026")]
    public void PlaceholdersAndRepositoryPublishedValues_AreUnsafe(string value) =>
        Assert.True(PublicSecrets.IsUnsafe(value));

    [Theory]
    [InlineData("q7Vt2mNcX9pL4sRz8KbW1yHdF6jA3eGu0oTiCxQv5nMw")]
    [InlineData(null)]
    public void FreshValues_AreNotRejected(string? value) => Assert.False(PublicSecrets.IsUnsafe(value));

    /// <summary>Regression guard: committed configuration must only hold placeholders for secrets.</summary>
    [Fact]
    public void CommittedConfiguration_ContainsNoRealSecrets()
    {
        var root = RepositoryRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "appsettings*.json", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                                    !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var (path, value) in Strings(document.RootElement, ""))
            {
                var secretKey = Regex.IsMatch(path, "(Password|SigningKey|Secret|ApiKey)$", RegexOptions.IgnoreCase);
                var connectionPassword = Regex.Match(value, "Password=([^;]*)", RegexOptions.IgnoreCase);
                if (secretKey && !IsPlaceholder(value))
                    offenders.Add($"{Path.GetFileName(file)}: {path}");
                if (connectionPassword.Success && !IsPlaceholder(connectionPassword.Groups[1].Value))
                    offenders.Add($"{Path.GetFileName(file)}: {path} (connection string password)");
            }
        }

        foreach (var line in File.ReadAllLines(Path.Combine(root, ".env.example")))
        {
            var match = Regex.Match(line, "^([A-Z_]*(PASSWORD|SIGNING_KEY|SECRET)[A-Z_]*)=(.*)$");
            if (match.Success && !IsPlaceholder(match.Groups[3].Value))
                offenders.Add($".env.example: {match.Groups[1].Value}");
        }

        Assert.True(offenders.Count == 0, "Real secret values are committed: " + string.Join(", ", offenders));
    }

    private static bool IsPlaceholder(string value) => value.Trim().StartsWith("<YOUR_", StringComparison.Ordinal);

    private static IEnumerable<(string Path, string Value)> Strings(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var childPath = path.Length == 0 ? property.Name : $"{path}:{property.Name}";
                    foreach (var item in Strings(property.Value, childPath))
                    {
                        yield return item;
                    }
                }
                break;
            case JsonValueKind.String:
                yield return (path, element.GetString()!);
                break;
        }
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SupplyGuard.sln"))) return directory.FullName;
        throw new InvalidOperationException("Repository root (SupplyGuard.sln) not found.");
    }
}
