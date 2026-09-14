#Requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Tag,
    [Parameter(Mandatory)][string] $ExpectedCommit,
    [Parameter(Mandatory)][string] $AssetDirectory
)

. (Join-Path $PSScriptRoot 'Release.Common.ps1')
$version = Get-ReleaseVersion -Tag $Tag
if ($ExpectedCommit -cnotmatch '\A[0-9a-f]{40}\z') { throw 'Expected a full lowercase Git commit SHA.' }
$AssetDirectory = (Resolve-Path -LiteralPath $AssetDirectory).Path
Assert-ReleaseAssetSet -Directory $AssetDirectory -Version $version -ExpectedCommit $ExpectedCommit
$null = Get-Command gh -ErrorAction Stop
$ownerRepo = 'msucharda/transcriber'
$repository = "github.com/$ownerRepo"
$env:GH_HOST = 'github.com'
$env:GH_PROMPT_DISABLED = '1'

function Invoke-ReleaseGh {
    param([Parameter(Mandatory)][string[]] $Arguments)
    Invoke-ReleaseNative gh $Arguments
}

function Get-RemoteRelease {
    $pages = Invoke-ReleaseGh @(
        'api', '--hostname', 'github.com', '--paginate', '--slurp',
        "repos/$ownerRepo/releases?per_page=100"
    ) | ConvertFrom-Json
    $releases = @($pages | ForEach-Object { $_ } | Where-Object tag_name -CEQ $Tag)
    if ($releases.Count -gt 1) { throw 'More than one release matched this tag.' }
    if ($releases.Count -eq 1) { return $releases[0] }
    return $null
}

function Assert-RemoteSource {
    $reference = Invoke-ReleaseGh @(
        'api', '--hostname', 'github.com', "repos/$ownerRepo/git/ref/tags/$Tag"
    ) | ConvertFrom-Json
    $object = $reference.object
    for ($depth = 0; $object.type -eq 'tag' -and $depth -lt 8; $depth++) {
        $annotatedTag = Invoke-ReleaseGh @(
            'api', '--hostname', 'github.com', "repos/$ownerRepo/git/tags/$($object.sha)"
        ) | ConvertFrom-Json
        $object = $annotatedTag.object
    }
    if ($object.type -cne 'commit' -or $object.sha -cne $ExpectedCommit) {
        throw 'The remote tag no longer identifies the built commit.'
    }
    $comparison = Invoke-ReleaseGh @(
        'api', '--hostname', 'github.com', "repos/$ownerRepo/compare/$ExpectedCommit...main"
    ) | ConvertFrom-Json
    if ($comparison.status -cnotin 'ahead', 'identical' -or $comparison.merge_base_commit.sha -cne $ExpectedCommit) {
        throw 'The tagged commit is not reachable from the current main branch.'
    }
}

Assert-RemoteSource
if ($null -ne (Get-RemoteRelease)) {
    throw "A release already exists for $Tag (including drafts). Refusing to overwrite or resume it."
}

$workingDirectory = Join-Path (Split-Path $AssetDirectory -Parent) ".publish-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $workingDirectory
try {
    $notesPath = Join-Path $workingDirectory 'notes.md'
    @"
Tiny Transcriber $version for Windows x64.

Download the ZIP for the app, MIT license, third-party and .NET runtime notices,
documentation, and Azure CLI + Bicep setup (tools\Deploy-Speech.ps1 and infra\main.bicep).
The standalone EXE is also available; no separate .NET runtime installation is needed.
Azure use requires your own subscription and Microsoft Entra identity; no API keys or
Azure credentials are included. See docs\azure-setup.md in the ZIP before provisioning.

The executable is unsigned. Windows may display publisher/reputation warnings.
Do not bypass your organization's security policy; verify the download source and SHA256SUMS.
Checksums detect changed bytes but are not a publisher signature.

Source commit: $ExpectedCommit
"@ | Set-Content -LiteralPath $notesPath -Encoding utf8NoBOM
    $createArguments = @(
        'release', 'create', $Tag, '--repo', $repository, '--verify-tag', '--target', $ExpectedCommit,
        '--title', "Tiny Transcriber $version", '--notes-file', $notesPath, '--draft'
    )
    if ($version.Contains('-')) { $createArguments += '--prerelease' }
    Invoke-ReleaseGh $createArguments
    $release = Get-RemoteRelease
    if ($null -eq $release -or -not $release.draft) { throw 'Expected a newly created draft release.' }
    $releaseId = $release.id
    $assetNames = @(Get-ReleaseAssetName -Version $version)
    $assetPaths = @($assetNames | ForEach-Object { Join-Path $AssetDirectory $_ })
    Invoke-ReleaseGh (@('release', 'upload', $Tag, '--repo', $repository) + $assetPaths)

    $uploaded = @(Invoke-ReleaseGh @(
        'api', '--hostname', 'github.com', "repos/$ownerRepo/releases/$releaseId/assets?per_page=100"
    ) | ConvertFrom-Json | ForEach-Object { $_ })
    if (@(Compare-Object @($assetNames | Sort-Object) @($uploaded.name | Sort-Object) -CaseSensitive).Count -ne 0 -or
        @($uploaded | Where-Object state -CNE 'uploaded').Count -ne 0) {
        throw 'The draft does not contain exactly the complete uploaded asset set.'
    }
    $verificationDirectory = Join-Path $workingDirectory 'download'
    $null = New-Item -ItemType Directory -Path $verificationDirectory
    Invoke-ReleaseGh @('release', 'download', $Tag, '--repo', $repository, '--dir', $verificationDirectory)
    Assert-ReleaseAssetSet -Directory $verificationDirectory -Version $version -ExpectedCommit $ExpectedCommit
    foreach ($name in $assetNames) {
        $localHash = (Get-FileHash -LiteralPath (Join-Path $AssetDirectory $name) -Algorithm SHA256).Hash
        $remoteHash = (Get-FileHash -LiteralPath (Join-Path $verificationDirectory $name) -Algorithm SHA256).Hash
        if ($localHash -cne $remoteHash) { throw "Uploaded asset differs from the trusted build: $name" }
    }
    Assert-RemoteSource
    $release = Get-RemoteRelease
    if ($null -eq $release -or $release.id -ne $releaseId -or -not $release.draft) {
        throw 'The release changed before publication.'
    }
    $latest = if ($version.Contains('-')) { '--latest=false' } else { '--latest' }
    Invoke-ReleaseGh @('release', 'edit', $Tag, '--repo', $repository, '--draft=false', $latest)
    $published = Get-RemoteRelease
    if ($null -eq $published -or $published.id -ne $releaseId -or $published.draft -or
        [bool] $published.prerelease -ne $version.Contains('-')) {
        throw 'Publication readback did not match the expected release.'
    }
    Write-Output "Published and verified: $($published.html_url)"
}
catch {
    Write-Warning "Publication stopped. Inspect $Tag on GitHub before taking any further action; an incomplete draft is intentionally retained."
    throw
}
finally {
    Remove-Item -LiteralPath $workingDirectory -Recurse -Force
}
