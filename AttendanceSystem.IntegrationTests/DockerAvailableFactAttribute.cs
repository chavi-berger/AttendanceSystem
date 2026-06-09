using System.Diagnostics;
using Xunit;

namespace AttendanceSystem.IntegrationTests;

/// <summary>
/// A [Fact] that is automatically skipped when Docker is not available on the host
/// (xUnit 2.5 has no Assert.Skip, so we set Skip at discovery time).
/// </summary>
public sealed class DockerAvailableFactAttribute : FactAttribute
{
    public DockerAvailableFactAttribute()
    {
        if (!DockerEnvironment.IsAvailable)
            Skip = "Docker is not available on this host — skipping container-based integration test.";
    }
}

internal static class DockerEnvironment
{
    public static readonly bool IsAvailable = Detect();

    private static bool Detect()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null) return false;
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(); } catch { /* ignore */ }
                return false;
            }
            return process.ExitCode == 0;
        }
        catch
        {
            return false; // docker CLI not found / not runnable
        }
    }
}
