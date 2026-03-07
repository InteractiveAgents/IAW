# Release Checklist

## Pre-release

1. All tests pass (`dotnet test IAW.slnx`)
2. Documentation built and reviewed
3. CHANGELOG.md updated with release notes
4. Version bumped in `Core.csproj` (`<Version>` property)
5. Migration guide updated if breaking changes exist

## Release

6. Git tag created (`git tag v3.0.0-preview.1`)
7. Tag pushed to trigger NuGet workflow (`git push origin v3.0.0-preview.1`)
8. GitHub release created from tag with release notes
9. NuGet packages published (automated via `nuget.yml` workflow)

## Post-release

10. Website deployed with updated docs
11. Announcement posted (GitHub Discussions, social media)
12. Dependabot alerts reviewed for new version
