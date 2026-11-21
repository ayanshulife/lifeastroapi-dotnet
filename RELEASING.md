# Releasing to nuget.org

The repository owner publishes. Two packages go up together:

| Package | Project |
|---------|---------|
| `LifeAstroApi` | `src/LifeAstroApi` |
| `LifeAstroApi.Extensions.DependencyInjection` | `src/LifeAstroApi.Extensions.DependencyInjection` (depends on `LifeAstroApi` of the same version) |

Each comes with a symbol package (`.snupkg`) for debugging, and SourceLink points the debugger at the
GitHub sources of the commit it was built from.

## Recommended: Trusted Publishing (GitHub Actions, no API key)

`.github/workflows/publish.yml` builds, tests, packs and pushes both packages when a tag `v<version>` is
pushed. nuget.org Trusted Publishing gives the workflow a one-hour API key through GitHub's OIDC token, so
no key is stored on a laptop or in GitHub secrets.

**One-time:** nuget.org → your name → *Trusted Publishing* → *Create*:

| Field | Value |
|-------|-------|
| Policy Name | `lifeastroapi-dotnet` (any name) |
| Package Owner | `lifeastroapi` |
| CI/CD Provider | GitHub Actions |
| Repository Owner | `ayanshulife` |
| Repository | `lifeastroapi-dotnet` |
| Workflow File | `publish.yml` (file name only, not the path) |
| Environment | leave empty |
| Scopes | **Push** → *Push new packages and package versions* |
| Glob Patterns and Packages | `LifeAstroApi*` |

A new policy can start "temporarily active" for 7 days (nuget.org does this mostly for private repositories);
the first successful publish makes it permanent. If the window passes unused, restart it on the same page.

**Each release:** set the version (step 1 below), update the CHANGELOG, commit and push with GitHub Desktop,
then create the tag: on github.com → *Releases* → *Draft a new release* → *Choose a tag* → type `v0.1.0` →
*Create new tag on publish*, target `main`, paste the CHANGELOG entry, *Publish release*. The workflow
(*Actions* tab → *Publish to nuget.org*) refuses a tag that does not match `<Version>` in
`Directory.Build.props`. Then wait for validation and check (steps 7 and 8 below).

The manual route below (an API key on your machine) still works, for example if GitHub Actions is down.

## One-time setup (manual route)

1. **nuget.org account.** Go to https://www.nuget.org, click *Sign in* and use a Microsoft account (a
   personal Outlook/Hotmail account or a work account). Choose the user name shown as the package owner
   (for example `ayanshulife`). Optionally create an *organization* (`Ayanshu Life`) under your account
   and publish as the organization, so more people can be owners later.

2. **API key.** Signed in: your name → *API Keys* → *Create*:
   - *Key Name*: `lifeastroapi-dotnet`
   - *Expires In*: 365 days (the longest nuget.org allows; renew it when it expires)
   - *Package Owner*: your account (or the organization)
   - *Select Scopes*: **Push** → *Push new packages and package versions*
   - *Select Packages* → *Glob Pattern*: `LifeAstroApi*`

   Click *Create* and copy the key: it is shown only once. Keep it in your password manager. Never put it in
   this repository, a script or a screenshot.

3. **Reserve the ID prefix (optional, recommended).** A reserved prefix stops anyone else from publishing
   packages named `LifeAstroApi.*` and shows the verified check mark next to the packages. After the first
   push (so the account owns a `LifeAstroApi` package), email **account@nuget.org** from the account's
   address:

   > Subject: ID prefix reservation request: LifeAstroApi
   >
   > Please reserve the package ID prefix `LifeAstroApi` (and `LifeAstroApi.*`) for the nuget.org account
   > `<your account or organization>`. The packages are the official .NET SDK of LifeAstroAPI
   > (https://lifeastroapi.com), a product of Ayanshu Life, which owns the domain lifeastroapi.com. Source:
   > https://github.com/ayanshulife/lifeastroapi-dotnet. Packages: LifeAstroApi,
   > LifeAstroApi.Extensions.DependencyInjection.

   The NuGet team answers by email, usually within a few working days.

## Each release

1. **Version.** Set it in `Directory.Build.props` (`<Version>`) and in `LifeAstroClient.Version`
   (`src/LifeAstroApi/LifeAstroClient.cs`); `ConfigurationTests.VersionMatchesThePackageVersion` checks they
   match. Add the CHANGELOG entry.

2. **Regenerate if the Node SDK's manifest changed** (new endpoints, new parameters):

   ```bash
   node tool/generate.mjs
   node tool/node-goldens.mjs
   ```

3. **Check everything** (the .NET 10 SDK; warnings are errors):

   ```bash
   dotnet build -c Release
   dotnet test -c Release
   ```

4. **Commit and push** with GitHub Desktop. SourceLink records the commit; the debugger can fetch the sources
   only once that commit is on GitHub.

5. **Pack** from the pushed commit, as a CI build (deterministic paths for SourceLink):

   ```bash
   rm -rf artifacts
   dotnet pack -c Release -o artifacts -p:ContinuousIntegrationBuild=true
   ```

   This writes four files:

   ```
   artifacts/LifeAstroApi.0.1.0.nupkg
   artifacts/LifeAstroApi.0.1.0.snupkg
   artifacts/LifeAstroApi.Extensions.DependencyInjection.0.1.0.nupkg
   artifacts/LifeAstroApi.Extensions.DependencyInjection.0.1.0.snupkg
   ```

   Optional check: `unzip -l artifacts/LifeAstroApi.0.1.0.nupkg` lists `lib/net8.0/` and
   `lib/netstandard2.0/` (each with `LifeAstroApi.dll` and `LifeAstroApi.xml`) and `README.md`.

6. **Push**, the core package first. Paste the key when you run the commands (or set it in the terminal
   session only, as below); `dotnet nuget push` also uploads the `.snupkg` that sits next to each `.nupkg`:

   ```bash
   export NUGET_API_KEY='...'      # this terminal only

   dotnet nuget push artifacts/LifeAstroApi.0.1.0.nupkg \
     --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json

   dotnet nuget push artifacts/LifeAstroApi.Extensions.DependencyInjection.0.1.0.nupkg \
     --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json
   ```

   If a symbol package did not go up with its package, push it on its own:

   ```bash
   dotnet nuget push artifacts/LifeAstroApi.0.1.0.snupkg \
     --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json
   ```

   (`--skip-duplicate` makes a repeated push of the same version a no-op instead of an error.)

7. **Wait for validation.** nuget.org scans and signs every package before it is listed. The package page
   (https://www.nuget.org/packages/LifeAstroApi) says *"This package has not been published yet"* or *"has
   not been indexed yet"* until then: usually 5 to 15 minutes, sometimes up to an hour; search can lag a
   little longer. The symbol package is validated separately. You get an email when each package is
   published (or if validation fails).

8. **Check** from a clean folder:

   ```bash
   dotnet new console -o /tmp/lifeastro-check && cd /tmp/lifeastro-check
   dotnet add package LifeAstroApi --version 0.1.0
   dotnet add package LifeAstroApi.Extensions.DependencyInjection --version 0.1.0
   dotnet build
   ```

9. **Tag** `v0.1.0` and make the GitHub release from the tag (GitHub Desktop or github.com), with the
   CHANGELOG entry as the notes.

A published version cannot be deleted or replaced on nuget.org; it can only be *unlisted* (hidden from search,
still installable by exact version). Fix mistakes with a new patch version.
