# Optional developer convenience tasks.
set shell := ["pwsh", "-NoProfile", "-ExecutionPolicy", "Bypass", "-CommandWithArgs"]
set positional-arguments

solution := "TeamsRelay.sln"
app := "src/TeamsRelay.App/TeamsRelay.App.csproj"
tests := "tests/TeamsRelay.Tests/TeamsRelay.Tests.csproj"
publish_dir := "artifacts/publish"
publish_self_contained_dir := "artifacts/publish-self-contained"

default:
    @just --list

help:
    @just --list
    @Write-Host ''
    @Write-Host 'CLI commands:'
    @.\tr.cmd --help

version:
    @.\tr.cmd --version

build:
    @dotnet build {{solution}} -nodeReuse:false

test:
    @dotnet test {{tests}} -nodeReuse:false

publish:
    @dotnet publish {{app}} -c Release -r win-x64 --self-contained false -o {{publish_dir}}

publish-self-contained:
    @dotnet publish {{app}} -c Release -r win-x64 --self-contained true -o {{publish_self_contained_dir}}

run *args:
    @$forwarded = if ($args.Count -gt 1) { $args[1..($args.Count - 1)] } else { @() }; & .\tr.cmd run @forwarded

start *args:
    @$forwarded = if ($args.Count -gt 1) { $args[1..($args.Count - 1)] } else { @() }; & .\tr.cmd start @forwarded

stop *args:
    @$forwarded = if ($args.Count -gt 1) { $args[1..($args.Count - 1)] } else { @() }; & .\tr.cmd stop @forwarded

status *args:
    @$forwarded = if ($args.Count -gt 1) { $args[1..($args.Count - 1)] } else { @() }; & .\tr.cmd status @forwarded

devices *args:
    @$forwarded = if ($args.Count -gt 1) { $args[1..($args.Count - 1)] } else { @() }; & .\tr.cmd devices @forwarded

doctor *args:
    @$forwarded = if ($args.Count -gt 1) { $args[1..($args.Count - 1)] } else { @() }; & .\tr.cmd doctor @forwarded

logs *args:
    @$forwarded = if ($args.Count -gt 1) { $args[1..($args.Count - 1)] } else { @() }; & .\tr.cmd logs @forwarded

logs-follow:
    @.\tr.cmd logs --follow

ci: build test
