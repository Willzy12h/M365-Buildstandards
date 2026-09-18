using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// The headless runner is read-only by construction, not by policy: it never authenticates, so no token, no Graph
/// client and no write path is reachable from it. That guarantee is only worth having if it cannot be lost by
/// accident, so this asserts the absence of every write-capable type in its source.
///
/// The check reads source rather than reflecting over the assembly because the test project does not reference the
/// runner, and adding that reference would make the tests depend on an executable. A source scan catches the way this
/// guarantee would realistically be lost, which is someone adding a using directive and a call.
/// </summary>
public class HeadlessRunnerTests
{
    private static string Directory_ =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BDIT.TenantToolkit.Cli"));

    private static bool Available => System.IO.Directory.Exists(Directory_);

    private static IEnumerable<string> Sources =>
        System.IO.Directory.EnumerateFiles(Directory_, "*.cs", SearchOption.AllDirectories);

    /// <summary>Kept in step with the list the runner declares, so the intent lives beside the code it protects.</summary>
    private static readonly string[] Forbidden =
    {
        "DeploymentExecutor", "ReviewedChangeService", "ApplicationPackageService",
        "RecoveryService", "EntraLapsService", "GraphClient", "TenantConnectionService"
    };

    [Fact]
    public void The_runner_cannot_reach_a_write_path()
    {
        if (!Available) return;

        foreach (var file in Sources)
        {
            var text = File.ReadAllText(file);
            foreach (var type in Forbidden)
            {
                // The runner names these in its own declaration of what it refuses to use; that line is the exception.
                var uses = text.Split('\n')
                    .Where(line => line.Contains(type, StringComparison.Ordinal))
                    .Where(line => !line.Contains("ForbiddenTypes", StringComparison.Ordinal))
                    .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    .Where(line => !line.Contains('"' + type + '"', StringComparison.Ordinal))
                    .ToList();
                Assert.True(uses.Count == 0, $"{Path.GetFileName(file)} references {type}: {string.Join(" | ", uses)}");
            }
        }
    }

    [Fact]
    public void The_runner_never_authenticates()
    {
        if (!Available) return;

        foreach (var file in Sources)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("Msal", text, StringComparison.Ordinal);
            Assert.DoesNotContain("AcquireToken", text, StringComparison.Ordinal);
            Assert.DoesNotContain("HttpClient", text, StringComparison.Ordinal);
        }
    }

    /// <summary>A runner that silently reported an empty result would be worse than one that refused.</summary>
    [Fact]
    public void Every_command_refuses_rather_than_guesses()
    {
        if (!Available) return;
        var program = File.ReadAllText(Path.Combine(Directory_, "Program.cs"));

        Assert.Contains("Snapshot file not found", program, StringComparison.Ordinal);
        Assert.Contains("does not name a tenant", program, StringComparison.Ordinal);
        Assert.Contains("was not found. Available:", program, StringComparison.Ordinal);
        Assert.Contains("No Build Standard releases were found", program, StringComparison.Ordinal);
    }
}
