## Summary

- <!-- Summary -->

## Validation

- [ ] `dotnet format TeamsRelay.sln --verify-no-changes --verbosity minimal`
- [ ] `dotnet build TeamsRelay.sln -nodeReuse:false`
- [ ] `dotnet test tests/TeamsRelay.Tests/TeamsRelay.Tests.csproj -nodeReuse:false`
- [ ] `dotnet list TeamsRelay.sln package --vulnerable --include-transitive`
- [ ] `.\tr.cmd --help`
- [ ] `.\tr.cmd --version`
- [ ] `.\tr.cmd run --help`

## Notes

- <!-- Notes -->
