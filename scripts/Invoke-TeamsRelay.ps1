param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $RelayArguments
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-RepositoryRoot {
    param(
        [string] $ScriptPath
    )

    $overrideRoot = [Environment]::GetEnvironmentVariable("TEAMSRELAY_LAUNCHER_ROOT")
    if (-not [string]::IsNullOrWhiteSpace($overrideRoot)) {
        return (Resolve-Path -Path $overrideRoot).Path
    }

    $scriptDirectory = Split-Path -Path $ScriptPath -Parent
    return (Resolve-Path -Path (Join-Path $scriptDirectory "..")).Path
}

function Get-ProjectTargetFramework {
    param(
        [string] $ProjectPath
    )

    if (-not (Test-Path -Path $ProjectPath -PathType Leaf)) {
        throw "TeamsRelay project file not found: $ProjectPath"
    }

    [xml] $project = Get-Content -Path $ProjectPath -Raw
    foreach ($propertyGroup in $project.Project.PropertyGroup) {
        if (-not [string]::IsNullOrWhiteSpace($propertyGroup.TargetFramework)) {
            return $propertyGroup.TargetFramework.Trim()
        }
    }

    throw "Could not determine TargetFramework from $ProjectPath"
}

function Select-LatestCandidate {
    param(
        [string] $SearchRoot,
        [string] $Filter
    )

    if (-not (Test-Path -Path $SearchRoot -PathType Container)) {
        return $null
    }

    return Get-ChildItem -Path $SearchRoot -Filter $Filter -File -Recurse -ErrorAction SilentlyContinue |
        Sort-Object -Property @(
            @{ Expression = "LastWriteTimeUtc"; Descending = $true },
            @{ Expression = "FullName"; Descending = $false }
        ) |
        Select-Object -First 1
}

function Resolve-DebugLaunchTarget {
    param(
        [string] $RepositoryRoot,
        [string] $TargetFramework
    )

    $debugOutputRoot = Join-Path $RepositoryRoot "src\TeamsRelay.App\bin\Debug"
    $exeCandidate = Select-LatestCandidate -SearchRoot $debugOutputRoot -Filter "TeamsRelay.exe"
    if ($null -ne $exeCandidate) {
        return @{
            Exists = $true
            Kind = "exe"
            Path = $exeCandidate.FullName
            LaunchFile = $exeCandidate.FullName
            LaunchArguments = @($RelayArguments)
        }
    }

    $dllCandidate = Select-LatestCandidate -SearchRoot $debugOutputRoot -Filter "TeamsRelay.dll"
    if ($null -ne $dllCandidate) {
        return @{
            Exists = $true
            Kind = "dotnet"
            Path = $dllCandidate.FullName
            LaunchFile = "dotnet"
            LaunchArguments = @($dllCandidate.FullName) + @($RelayArguments)
        }
    }

    $expectedExePath = Join-Path $debugOutputRoot (Join-Path $TargetFramework "TeamsRelay.exe")
    return @{
        Exists = $false
        Kind = "exe"
        Path = $expectedExePath
        LaunchFile = $expectedExePath
        LaunchArguments = @($RelayArguments)
    }
}

function Resolve-FreshnessMarker {
    param(
        [hashtable] $LaunchTarget
    )

    if (-not $LaunchTarget.Exists) {
        return $null
    }

    $launchDirectory = if ($LaunchTarget.Kind -eq "dotnet") {
        Split-Path -Path $LaunchTarget.Path -Parent
    } else {
        Split-Path -Path $LaunchTarget.Path -Parent
    }

    $newestDll = Get-ChildItem -Path $launchDirectory -Filter "*.dll" -File -ErrorAction SilentlyContinue |
        Sort-Object -Property LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if ($null -ne $newestDll) {
        return $newestDll
    }

    return Get-Item -Path $LaunchTarget.Path -ErrorAction SilentlyContinue
}

function Get-LatestRelevantInput {
    param(
        [string] $RepositoryRoot
    )

    $candidates = @()
    $srcRoot = Join-Path $RepositoryRoot "src"
    if (Test-Path -Path $srcRoot -PathType Container) {
        $candidates += Get-ChildItem -Path $srcRoot -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
    }

    foreach ($fileName in @("TeamsRelay.sln", "global.json", "Directory.Build.props", "Directory.Build.targets")) {
        $path = Join-Path $RepositoryRoot $fileName
        if (Test-Path -Path $path -PathType Leaf) {
            $candidates += Get-Item -Path $path
        }
    }

    if ($candidates.Count -eq 0) {
        return $null
    }

    return $candidates |
        Sort-Object -Property @(
            @{ Expression = "LastWriteTimeUtc"; Descending = $true },
            @{ Expression = "FullName"; Descending = $false }
        ) |
        Select-Object -First 1
}

function Should-WarnAboutStaleBuild {
    param(
        [string[]] $Arguments
    )

    if ($Arguments.Count -eq 0) {
        return $false
    }

    $command = $Arguments[0].Trim().ToLowerInvariant()
    return $command -in @("run", "start")
}

function Write-StaleBuildWarning {
    param(
        [string] $MarkerPath,
        [datetime] $MarkerTimestampUtc,
        [string] $LatestInputPath,
        [datetime] $LatestInputTimestampUtc
    )

    [Console]::Error.WriteLine("WARNING: TeamsRelay is using a stale Debug build.")
    [Console]::Error.WriteLine("  build marker: $MarkerPath")
    [Console]::Error.WriteLine("  build time: $($MarkerTimestampUtc.ToString('O'))")
    [Console]::Error.WriteLine("  newest input: $LatestInputPath")
    [Console]::Error.WriteLine("  input time: $($LatestInputTimestampUtc.ToString('O'))")
    [Console]::Error.WriteLine("  refresh: just build")
    [Console]::Error.WriteLine("           dotnet build TeamsRelay.sln -nodeReuse:false")
    [Console]::Error.WriteLine("Continuing with the existing Debug build.")
}

$repositoryRoot = Resolve-RepositoryRoot -ScriptPath $PSCommandPath
$projectPath = Join-Path $repositoryRoot "src\TeamsRelay.App\TeamsRelay.App.csproj"
$targetFramework = Get-ProjectTargetFramework -ProjectPath $projectPath
$launchTarget = Resolve-DebugLaunchTarget -RepositoryRoot $repositoryRoot -TargetFramework $targetFramework
$buildRequired = -not $launchTarget.Exists
$freshnessMarker = Resolve-FreshnessMarker -LaunchTarget $launchTarget
$latestRelevantInput = if (-not $buildRequired) { Get-LatestRelevantInput -RepositoryRoot $repositoryRoot } else { $null }
$staleBuildDetected = $false
if ($null -ne $freshnessMarker -and $null -ne $latestRelevantInput) {
    $staleBuildDetected = $latestRelevantInput.LastWriteTimeUtc -gt $freshnessMarker.LastWriteTimeUtc
}
$staleBuildApplies = $staleBuildDetected -and (Should-WarnAboutStaleBuild -Arguments $RelayArguments)
$buildCommand = @(
    "dotnet",
    "build",
    $projectPath,
    "-c",
    "Debug",
    "-nodeReuse:false"
)

$dryRun = [Environment]::GetEnvironmentVariable("TEAMSRELAY_LAUNCHER_DRY_RUN")
if ($dryRun -eq "1") {
    $freshnessMarkerPath = if ($null -ne $freshnessMarker) { $freshnessMarker.FullName } else { $null }
    $latestInputPath = if ($null -ne $latestRelevantInput) { $latestRelevantInput.FullName } else { $null }

    [ordered]@{
        repoRoot = $repositoryRoot
        buildRequired = $buildRequired
        staleBuildDetected = $staleBuildDetected
        staleBuildApplies = $staleBuildApplies
        freshnessMarkerPath = $freshnessMarkerPath
        freshnessMarkerTimestampUtc = if ($null -ne $freshnessMarker) { $freshnessMarker.LastWriteTimeUtc.ToString("O") } else { $null }
        latestInputPath = $latestInputPath
        latestInputTimestampUtc = if ($null -ne $latestRelevantInput) { $latestRelevantInput.LastWriteTimeUtc.ToString("O") } else { $null }
        buildCommand = $buildCommand
        launchKind = $launchTarget.Kind
        launchPath = $launchTarget.Path
        launchFile = $launchTarget.LaunchFile
        launchArguments = $launchTarget.LaunchArguments
        relayArguments = @($RelayArguments)
        environment = @{
            TEAMSRELAY_ROOT = $repositoryRoot
        }
    } | ConvertTo-Json -Depth 5 -Compress
    exit 0
}

Push-Location -Path $repositoryRoot
try {
    if ($buildRequired) {
        & dotnet build $projectPath -c Debug -nodeReuse:false
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }

        $launchTarget = Resolve-DebugLaunchTarget -RepositoryRoot $repositoryRoot -TargetFramework $targetFramework
        if (-not $launchTarget.Exists) {
            throw "Failed to resolve TeamsRelay debug output after build."
        }
    }

    if ($staleBuildApplies) {
        Write-StaleBuildWarning `
            -MarkerPath $freshnessMarker.FullName `
            -MarkerTimestampUtc $freshnessMarker.LastWriteTimeUtc `
            -LatestInputPath $latestRelevantInput.FullName `
            -LatestInputTimestampUtc $latestRelevantInput.LastWriteTimeUtc
    }

    $env:TEAMSRELAY_ROOT = $repositoryRoot
    if ($launchTarget.Kind -eq "dotnet") {
        & dotnet $launchTarget.Path @RelayArguments
    }
    else {
        & $launchTarget.Path @RelayArguments
    }

    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
