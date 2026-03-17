using System.Diagnostics;
using System.Text.Json;

namespace TeamsRelay.Tests;

public sealed class LauncherScriptTests
{
    [Theory]
    [InlineData("stop")]
    [InlineData("start")]
    public async Task DryRunPrefersExistingExe(string command)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateTemporaryRepository();
        var sourcePath = Path.Combine(root, "src", "TeamsRelay.App", "Program.cs");
        var exePath = Path.Combine(root, "src", "TeamsRelay.App", "bin", "Debug", "net9.0-windows", "TeamsRelay.exe");
        await File.WriteAllTextAsync(sourcePath, "internal static class Program { }");
        Directory.CreateDirectory(Path.GetDirectoryName(exePath)!);
        await File.WriteAllTextAsync(exePath, string.Empty);
        SetTimestampUtc(sourcePath, new DateTime(2026, 03, 07, 12, 00, 00, DateTimeKind.Utc));
        SetTimestampUtc(exePath, new DateTime(2026, 03, 07, 12, 05, 00, DateTimeKind.Utc));

        var result = await InvokeDryRunAsync(root, [command]);

        Assert.False(result.BuildRequired.GetBoolean());
        Assert.False(result.StaleBuildDetected.GetBoolean());
        Assert.False(result.StaleBuildApplies.GetBoolean());
        Assert.Equal("exe", result.LaunchKind.GetString());
        Assert.Equal(exePath, result.LaunchPath.GetString());
        Assert.Equal(exePath, result.LaunchFile.GetString());
        Assert.Equal(exePath, result.FreshnessMarkerPath.GetString());
        Assert.Equal(command, result.RelayArguments[0].GetString());
    }

    [Fact]
    public async Task DryRunFallsBackToDotnetWhenOnlyDllExists()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateTemporaryRepository();
        var dllPath = Path.Combine(root, "src", "TeamsRelay.App", "bin", "Debug", "net9.0-windows", "TeamsRelay.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(dllPath)!);
        await File.WriteAllTextAsync(dllPath, string.Empty);

        var result = await InvokeDryRunAsync(root, ["status"]);

        Assert.False(result.BuildRequired.GetBoolean());
        Assert.False(result.StaleBuildDetected.GetBoolean());
        Assert.False(result.StaleBuildApplies.GetBoolean());
        Assert.Equal("dotnet", result.LaunchKind.GetString());
        Assert.Equal(dllPath, result.LaunchPath.GetString());
        Assert.Equal("dotnet", result.LaunchFile.GetString());
        Assert.Equal(dllPath, result.FreshnessMarkerPath.GetString());
        Assert.Equal(dllPath, result.LaunchArguments[0].GetString());
        Assert.Equal("status", result.LaunchArguments[1].GetString());
    }

    [Fact]
    public async Task DryRunRequestsBuildWhenNoDebugOutputExists()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateTemporaryRepository();

        var result = await InvokeDryRunAsync(root, ["--version"]);

        var expectedProjectPath = Path.Combine(root, "src", "TeamsRelay.App", "TeamsRelay.App.csproj");
        var expectedLaunchPath = Path.Combine(root, "src", "TeamsRelay.App", "bin", "Debug", "net9.0-windows", "TeamsRelay.exe");

        Assert.True(result.BuildRequired.GetBoolean());
        Assert.False(result.StaleBuildDetected.GetBoolean());
        Assert.False(result.StaleBuildApplies.GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.FreshnessMarkerPath.ValueKind);
        Assert.Equal(JsonValueKind.Null, result.FreshnessMarkerTimestampUtc.ValueKind);
        Assert.Equal("exe", result.LaunchKind.GetString());
        Assert.Equal(expectedLaunchPath, result.LaunchPath.GetString());
        Assert.Equal("dotnet", result.BuildCommand[0].GetString());
        Assert.Equal("build", result.BuildCommand[1].GetString());
        Assert.Equal(expectedProjectPath, result.BuildCommand[2].GetString());
    }

    [Theory]
    [InlineData("run")]
    [InlineData("start")]
    public async Task DryRunFlagsStaleBuildForRelayLaunchCommands(string command)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateTemporaryRepository();
        var sourcePath = Path.Combine(root, "src", "TeamsRelay.App", "CliApplication.cs");
        var exePath = Path.Combine(root, "src", "TeamsRelay.App", "bin", "Debug", "net9.0-windows", "TeamsRelay.exe");
        await File.WriteAllTextAsync(sourcePath, "internal sealed class CliApplication { }");
        Directory.CreateDirectory(Path.GetDirectoryName(exePath)!);
        await File.WriteAllTextAsync(exePath, string.Empty);
        SetTimestampUtc(exePath, new DateTime(2026, 03, 07, 12, 00, 00, DateTimeKind.Utc));
        SetTimestampUtc(sourcePath, new DateTime(2026, 03, 07, 12, 10, 00, DateTimeKind.Utc));

        var result = await InvokeDryRunAsync(root, [command]);

        Assert.False(result.BuildRequired.GetBoolean());
        Assert.True(result.StaleBuildDetected.GetBoolean());
        Assert.True(result.StaleBuildApplies.GetBoolean());
        Assert.Equal(exePath, result.FreshnessMarkerPath.GetString());
        Assert.Equal(sourcePath, result.LatestInputPath.GetString());
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("status")]
    public async Task DryRunDoesNotApplyStaleBuildWarningToNonLaunchCommands(string command)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateTemporaryRepository();
        var sourcePath = Path.Combine(root, "src", "TeamsRelay.App", "CliApplication.cs");
        var exePath = Path.Combine(root, "src", "TeamsRelay.App", "bin", "Debug", "net9.0-windows", "TeamsRelay.exe");
        await File.WriteAllTextAsync(sourcePath, "internal sealed class CliApplication { }");
        Directory.CreateDirectory(Path.GetDirectoryName(exePath)!);
        await File.WriteAllTextAsync(exePath, string.Empty);
        SetTimestampUtc(exePath, new DateTime(2026, 03, 07, 12, 00, 00, DateTimeKind.Utc));
        SetTimestampUtc(sourcePath, new DateTime(2026, 03, 07, 12, 10, 00, DateTimeKind.Utc));

        var result = await InvokeDryRunAsync(root, [command]);

        Assert.False(result.BuildRequired.GetBoolean());
        Assert.True(result.StaleBuildDetected.GetBoolean());
        Assert.False(result.StaleBuildApplies.GetBoolean());
    }

    [Fact]
    public async Task DryRunUsesSiblingDllAsFreshnessMarkerForExeLaunch()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateTemporaryRepository();
        var sourcePath = Path.Combine(root, "src", "TeamsRelay.App", "Program.cs");
        var outputDirectory = Path.Combine(root, "src", "TeamsRelay.App", "bin", "Debug", "net9.0-windows");
        var exePath = Path.Combine(outputDirectory, "TeamsRelay.exe");
        var dllPath = Path.Combine(outputDirectory, "TeamsRelay.dll");

        await File.WriteAllTextAsync(sourcePath, "internal static class Program { }");
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(exePath, string.Empty);
        await File.WriteAllTextAsync(dllPath, string.Empty);
        SetTimestampUtc(exePath, new DateTime(2026, 03, 07, 12, 00, 00, DateTimeKind.Utc));
        SetTimestampUtc(sourcePath, new DateTime(2026, 03, 07, 12, 05, 00, DateTimeKind.Utc));
        SetTimestampUtc(dllPath, new DateTime(2026, 03, 07, 12, 10, 00, DateTimeKind.Utc));

        var result = await InvokeDryRunAsync(root, ["start"]);

        Assert.Equal("exe", result.LaunchKind.GetString());
        Assert.Equal(dllPath, result.FreshnessMarkerPath.GetString());
        Assert.False(result.StaleBuildDetected.GetBoolean());
        Assert.False(result.StaleBuildApplies.GetBoolean());
    }

    [Fact]
    public async Task DryRunUsesNewestOutputDllAsFreshnessMarker()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateTemporaryRepository();
        var sourcePath = Path.Combine(root, "src", "TeamsRelay.Core", "RelayPipeline.cs");
        var outputDirectory = Path.Combine(root, "src", "TeamsRelay.App", "bin", "Debug", "net9.0-windows");
        var exePath = Path.Combine(outputDirectory, "TeamsRelay.exe");
        var appDllPath = Path.Combine(outputDirectory, "TeamsRelay.dll");
        var coreDllPath = Path.Combine(outputDirectory, "TeamsRelay.Core.dll");

        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(sourcePath, "internal sealed class RelayPipeline { }");
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(exePath, string.Empty);
        await File.WriteAllTextAsync(appDllPath, string.Empty);
        await File.WriteAllTextAsync(coreDllPath, string.Empty);
        SetTimestampUtc(exePath, new DateTime(2026, 03, 07, 12, 00, 00, DateTimeKind.Utc));
        SetTimestampUtc(appDllPath, new DateTime(2026, 03, 07, 12, 00, 00, DateTimeKind.Utc));
        SetTimestampUtc(sourcePath, new DateTime(2026, 03, 07, 12, 05, 00, DateTimeKind.Utc));
        SetTimestampUtc(coreDllPath, new DateTime(2026, 03, 07, 12, 10, 00, DateTimeKind.Utc));

        var result = await InvokeDryRunAsync(root, ["start"]);

        Assert.Equal("exe", result.LaunchKind.GetString());
        Assert.Equal(coreDllPath, result.FreshnessMarkerPath.GetString());
        Assert.False(result.StaleBuildDetected.GetBoolean());
        Assert.False(result.StaleBuildApplies.GetBoolean());
    }

    private static async Task<LauncherDryRunResult> InvokeDryRunAsync(string launcherRoot, IReadOnlyList<string> arguments)
    {
        var repositoryRoot = FindRepositoryRoot();
        var launcherScriptPath = Path.Combine(repositoryRoot, "scripts", "Invoke-TeamsRelay.ps1");

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(launcherScriptPath);

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["TEAMSRELAY_LAUNCHER_DRY_RUN"] = "1";
        startInfo.Environment["TEAMSRELAY_LAUNCHER_ROOT"] = launcherRoot;

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start launcher script.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(process.ExitCode == 0, $"Launcher dry-run failed.{Environment.NewLine}STDOUT:{Environment.NewLine}{stdout}{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}");

        using var document = JsonDocument.Parse(stdout);
        return new LauncherDryRunResult(document.RootElement.Clone());
    }

    private static string CreateTemporaryRepository()
    {
        var root = Path.Combine(Path.GetTempPath(), "teamsrelay-launcher-tests", Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(root, "src", "TeamsRelay.App");
        var projectPath = Path.Combine(projectDirectory, "TeamsRelay.App.csproj");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0-windows</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        SetTimestampUtc(projectPath, new DateTime(2026, 03, 07, 11, 50, 00, DateTimeKind.Utc));
        return root;
    }

    private static void SetTimestampUtc(string path, DateTime timestampUtc)
    {
        File.SetLastWriteTimeUtc(path, timestampUtc);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TeamsRelay.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed record LauncherDryRunResult(JsonElement Root)
    {
        public JsonElement BuildRequired => Root.GetProperty("buildRequired");

        public JsonElement StaleBuildDetected => Root.GetProperty("staleBuildDetected");

        public JsonElement StaleBuildApplies => Root.GetProperty("staleBuildApplies");

        public JsonElement FreshnessMarkerPath => Root.GetProperty("freshnessMarkerPath");

        public JsonElement FreshnessMarkerTimestampUtc => Root.GetProperty("freshnessMarkerTimestampUtc");

        public JsonElement LatestInputPath => Root.GetProperty("latestInputPath");

        public JsonElement LatestInputTimestampUtc => Root.GetProperty("latestInputTimestampUtc");

        public JsonElement LaunchKind => Root.GetProperty("launchKind");

        public JsonElement LaunchPath => Root.GetProperty("launchPath");

        public JsonElement LaunchFile => Root.GetProperty("launchFile");

        public JsonElement LaunchArguments => Root.GetProperty("launchArguments");

        public JsonElement RelayArguments => Root.GetProperty("relayArguments");

        public JsonElement BuildCommand => Root.GetProperty("buildCommand");
    }
}
