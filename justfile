# Optional developer convenience tasks.
set shell := ["pwsh", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command"]

solution := "TeamsRelay.sln"
app := "src/TeamsRelay.App/TeamsRelay.App.csproj"
tests := "tests/TeamsRelay.Tests/TeamsRelay.Tests.csproj"
publish_dir := "artifacts/publish"
publish_self_contained_dir := "artifacts/publish-self-contained"

default:
    @just --list

help:
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
    @.\tr.cmd run {{args}}

start *args:
    @.\tr.cmd start {{args}}

stop *args:
    @.\tr.cmd stop {{args}}

status *args:
    @.\tr.cmd status {{args}}

devices *args:
    @.\tr.cmd devices {{args}}

doctor *args:
    @.\tr.cmd doctor {{args}}

logs *args:
    @.\tr.cmd logs {{args}}

logs-follow:
    @.\tr.cmd logs --follow

config-init *args:
    @.\tr.cmd config init {{args}}

cli *args:
    @.\tr.cmd {{args}}

ci: build test
