# Releasing Tiny Transcriber

## Current scope: private preparation only

The repository remains **private**. Preparing these files or running branch CI
does not authorize creating a tag, publishing a release, or changing repository
visibility. None of the scripts changes visibility. GitHub Releases and their
assets inherit repository access: a release in a private repository is not a
public download. Any future release or visibility change requires separate
maintainer authorization.

## What is shipped

Supported binary target: **Windows x64**, .NET 10, self-contained, single file.
Users do not need to install .NET. Native runtime components extract on launch
to the .NET runtime's per-user extraction location; single file does not mean
that execution never writes files.

Every release has exactly these assets (substitute the version):

```text
TinyTranscriber-1.2.3-win-x64.exe
TinyTranscriber-1.2.3-win-x64.zip
LICENSE
THIRD-PARTY-NOTICES.md
release-metadata.json
SHA256SUMS
```

The ZIP is the recommended complete distribution. Its root contains
`TinyTranscriber.exe`, `README.md`, `LICENSE`, `THIRD-PARTY-NOTICES.md`,
`SECURITY.md`, and `release-metadata.json`. It also contains:

- `docs\`: checked-in Markdown documentation, including Azure setup instructions.
- `tools\Deploy-Speech.ps1` and `infra\main.bicep` (plus any Bicep modules).
- `licenses\<package-id>\<version>\`: license/notice files supplied by restored
  NuGet packages, including the .NET and Windows Desktop runtime packs.

The .NET 10.0.12 core runtime pack supplies `LICENSE.TXT` and
`THIRD-PARTY-NOTICES.TXT`; the Windows Desktop pack supplies `LICENSE` but
no separate third-party notice file. Packaging requires both runtime licenses
and the core runtime's third-party notice, copies every supplied notice, and
records their presence in release metadata.

The standalone EXE has no embedded credentials. Azure setup uses the user's
own Azure CLI sign-in and Microsoft Entra identity, not shared keys.
Provisioning is an explicit user action and may incur Azure charges; see
[Azure setup](azure-setup.md). Neither CI nor release publishing signs in to
Azure or deploys resources.

The EXE is currently **unsigned**. Windows SmartScreen, endpoint protection, or
organizational policy may flag or block an unknown publisher. Do not bypass
those controls. Verify the official download source and consult your
administrator if blocked. SHA-256 checksums verify bytes, not publisher identity,
and do not replace code signing. There is no claim of signed provenance or
bit-for-bit reproducible ZIPs.

## Build and validate locally

Requirements: Windows x64, PowerShell 7.4+, Git, the SDK pinned in `global.json`,
and Azure CLI with its Bicep compiler. Restoring requires access to nuget.org
and its vulnerability metadata. `az bicep build` requires no Azure login;
Azure CLI may obtain the Bicep compiler if it is not installed.

From the repository root:

```powershell
.\tools\Test-ReleasePipeline.ps1
.\tools\Publish-Release.ps1 -Version '1.2.3' -OutputDirectory '.\artifacts'
```

The first command is an offline regression suite: it mocks Git, .NET, Azure
CLI, and GitHub CLI inside a fresh owned fixture directory. It checks version
validation, package contents, license/checksum enforcement, native failures,
overwrite protection, draft verification, moved-tag rejection, and prerelease
handling without building the application or contacting any service.

Despite its name, this script only **builds local release assets**. It does not
push tags, invoke GitHub, sign in to Azure, or publish/deploy anything.
It checks PowerShell syntax, compiles the Bicep template, performs locked
solution restore with full NuGet auditing, runs tests, then restores and
publishes the app with the single-file/self-contained settings. Warnings
NU1900–NU1905 are errors, including unavailable audit sources; a missing source,
lockfile drift, failed command, missing documentation, or missing runtime
notices aborts the build. It does not suppress or ignore audit failures.

Before any restore, the script validates the checked-in package/audit source
configuration, checks the **effective merged** NuGet source list for enabled
public nuget.org, and evaluates the actual MSBuild audit properties. A user-level
configuration disabling `nuget.org` therefore fails explicitly rather than
silently auditing against zero sources. The script never enables sources,
changes user configuration, or falls back to a mirror. Both restores explicitly
select public nuget.org. TLS or vulnerability-service failures remain errors;
local builds using a mirror are not evidence that the public-feed audit passed.

Output is `artifacts\TinyTranscriber-1.2.3-win-x64\` with the six assets above.
Existing output for that version is never overwritten. Use a different output
root to repeat a local build. Staging happens in a fresh, uniquely named
subdirectory under the output root; only that owned staging directory is
removed. No PDBs or loose runtime DLLs are included. Release metadata records
the version, commit, SDK, and runtime pack versions, not local paths.

The solution and app publish restores must both agree with checked-in
`packages.lock.json` files. When intentionally updating dependencies or SDKs,
regenerate the app lock with `--runtime win-x64 -p:SelfContained=true
-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` as well
as the solution restore, and verify both locked restores before submitting
the change. Review the lockfile and notices together. Do not turn off auditing
to get a release out.

To verify a downloaded EXE:

```powershell
Get-FileHash -LiteralPath '.\TinyTranscriber-1.2.3-win-x64.exe' -Algorithm SHA256
```

Compare its entire hash to the exact filename's line in `SHA256SUMS` downloaded
from the same official release.

## CI and release trust boundary

`CI` runs on pull requests and branch pushes on `windows-2025`. It uses a
read-only repository token, the pinned SDK, and immutable action commit pins.
It builds the same complete package with an explicitly non-release
`0.0.0-ci.<run>.<attempt>` version. Preview artifacts expire after seven days
and are **not trusted release inputs**.

`Release` runs only on pushed `v*` tags in `msucharda/transcriber`, then
strictly validates `vMAJOR.MINOR.PATCH[-prerelease]`. Examples: `v1.2.3`,
`v1.2.3-rc.1`. Leading zeroes in numeric identifiers, build metadata (`+...`),
and tags longer than 128 characters are rejected. Core numbers must be at
most 65534 for Windows assembly version compatibility.

The read-only build job checks out complete Git history and verifies that the
tag, workflow event commit, and checkout agree and that the commit is on
`origin/main`. It reruns audit, tests, Bicep compilation, and packaging.
Only the dependent publishing job receives `contents: write`, and its token
is exposed only to the publishing step. That job downloads the named artifact
from **this same workflow run**, never a PR run or caller-selected run.
All checkouts disable credential persistence. No Azure secrets or credentials
are configured in either workflow.

Publishing uses GitHub CLI, with the repository and github.com host fixed in
`tools\Publish-GitHubRelease.ps1`. It checks the live remote tag and main
ancestry again, refuses any existing release for the tag (drafts included),
creates a draft, uploads all six assets without replacement, and checks the
exact uploaded asset set. It then downloads every asset and verifies local and
remote SHA-256 hashes, including `SHA256SUMS` itself, before publishing.
Prerelease tags produce prereleases and are not marked latest.

## Maintainer procedure

Before the first release, an administrator must:

1. Keep the repository private during preparation. If public distribution is
   separately authorized, complete the privacy/security review before an
   administrator changes visibility; the release workflow never does this.
2. Protect `main` with required reviews and the `Test and package Windows x64`
   CI check. Do not treat unavailable protection APIs on a private/free
   repository as proof that protection is enabled.
3. Restrict creation/update/deletion of release tags to trusted maintainers
   using repository rulesets. Main-ancestry checks are not a substitute:
   someone able to push an arbitrary tag can also tag altered workflow code.
4. Configure the `github-release` environment with appropriate reviewers and
   tag deployment restrictions where the GitHub plan supports them. The
   workflow references this environment; merely naming it does **not** create
   approval rules. Review tagged source and checks before approving it.
5. Confirm Actions can run on `windows-2025`, immutable action pins are
   allowed, and the publishing job can receive its narrowly scoped write token.

For each release:

1. Review dependency updates, runtime patch level, license notices, docs, and
   known security findings. Dependabot proposes weekly NuGet/action updates;
   SDK and bundled runtime updates still need explicit review of `global.json`.
2. Merge the reviewed change through normal protected-main policy and wait
   for CI. Do not waive required checks or publish an arbitrary PR artifact.
3. From a clean checkout of the exact reviewed main commit, create and push
   the intended version tag. This push triggers publication after any
   configured environment approval, so do it only when release is authorized.
4. Review the release run, approve its environment gate if configured, and
   inspect the final release URL and all six downloadable assets.
5. On a clean Windows x64 machine without .NET installed, verify checksums,
   extract the ZIP, check license/setup contents, and smoke-test launch,
   microphone permissions, tray/hotkey behavior, and identity-authenticated
   transcription. Azure provisioning/transcription requires separately
   authorized real Azure access; CI deliberately does not exercise it.

`tools\Publish-GitHubRelease.ps1` is the **mutating** publisher and should
normally be invoked only by the workflow. It requires PowerShell 7.4+,
authenticated GitHub CLI, a validated tag/full commit SHA, and the exact
asset directory from that tag's trusted build:

```powershell
# Publishes a real GitHub release. Run only with explicit authorization.
.\tools\Publish-GitHubRelease.ps1 -Tag 'v1.2.3' `
  -ExpectedCommit '<full-40-character-commit-sha>' `
  -AssetDirectory '.\artifacts\TinyTranscriber-1.2.3-win-x64'
```

If uploading or verification fails, the script deliberately leaves the draft
unpublished. Inspect the error and remote state before further action.
Reruns fail rather than overwrite or automatically resume a draft. A maintainer
must explicitly review and remove an abandoned draft before a clean retry;
the scripts never delete releases, tags, assets, or arbitrary output trees.
Never replace already published bits for a version; fix forward with a new
version. A failure after the final publish request requires readback of the
actual state, not a blind retry.
