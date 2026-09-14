Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-ReleaseNative {
    param(
        [Parameter(Mandatory)][string] $Command,
        [Parameter(Mandatory)][string[]] $Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command failed with exit code $LASTEXITCODE."
    }
}

function Get-ReleaseVersion {
    param([Parameter(Mandatory)][string] $Tag)

    $identifier = '(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)'
    $pattern = '\Av(?<version>(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:-' +
        $identifier + '(?:\.' + $identifier + ')*)?)\z'
    $match = [regex]::Match($Tag, $pattern)
    if (-not $match.Success -or $Tag.Length -gt 128) {
        throw 'Expected vMAJOR.MINOR.PATCH[-prerelease], with no leading zeroes in numeric identifiers or build metadata.'
    }
    foreach ($part in 'major', 'minor', 'patch') {
        $number = 0
        if (-not [int]::TryParse($match.Groups[$part].Value, [ref] $number) -or $number -gt 65534) {
            throw 'Version components must fit Windows assembly versions (0 through 65534).'
        }
    }
    return $match.Groups['version'].Value
}

function Assert-ReleaseSource {
    param(
        [Parameter(Mandatory)][string] $Tag,
        [Parameter(Mandatory)][string] $ExpectedCommit
    )

    $null = Get-ReleaseVersion -Tag $Tag
    if ($ExpectedCommit -cnotmatch '\A[0-9a-f]{40}\z') {
        throw 'Expected a full lowercase Git commit SHA.'
    }
    $headCommit = (Invoke-ReleaseNative git @('rev-parse', '--verify', 'HEAD')).Trim()
    $tagCommit = (Invoke-ReleaseNative git @('rev-parse', '--verify', "refs/tags/$Tag^{commit}")).Trim()
    if ($headCommit -cne $ExpectedCommit -or $tagCommit -cne $ExpectedCommit) {
        throw 'The checkout, event commit, and tag must identify the same commit.'
    }
    $null = Invoke-ReleaseNative git @('rev-parse', '--verify', 'refs/remotes/origin/main')
    $null = Invoke-ReleaseNative git @('merge-base', '--is-ancestor', $ExpectedCommit, 'refs/remotes/origin/main')
}

function Get-ReleaseAssetName {
    param([Parameter(Mandatory)][string] $Version)

    return @(
        "TinyTranscriber-$Version-win-x64.exe"
        "TinyTranscriber-$Version-win-x64.zip"
        'LICENSE'
        'THIRD-PARTY-NOTICES.md'
        'release-metadata.json'
        'SHA256SUMS'
    )
}

function Assert-ReleaseAssetSet {
    param(
        [Parameter(Mandatory)][string] $Directory,
        [Parameter(Mandatory)][string] $Version,
        [Parameter(Mandatory)][string] $ExpectedCommit
    )

    $null = Get-ReleaseVersion -Tag "v$Version"
    $expected = @(Get-ReleaseAssetName -Version $Version | Sort-Object)
    $actual = @(Get-ChildItem -LiteralPath $Directory -Force)
    if (@($actual | Where-Object { $_.PSIsContainer -or $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -ne 0 -or
        @(Compare-Object $expected @($actual.Name | Sort-Object) -CaseSensitive).Count -ne 0) {
        throw 'Release asset directory must contain exactly the expected six regular files.'
    }
    $metadata = Get-Content -LiteralPath (Join-Path $Directory 'release-metadata.json') -Raw | ConvertFrom-Json
    if ($metadata.version -cne $Version -or $metadata.commit -cne $ExpectedCommit -or
        $metadata.runtimeIdentifier -cne 'win-x64' -or $metadata.application -cne 'Tiny Transcriber') {
        throw 'Release metadata does not match this version, commit, and runtime.'
    }

    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($line in Get-Content -LiteralPath (Join-Path $Directory 'SHA256SUMS')) {
        if ($line -cnotmatch '\A([0-9a-f]{64})  ([A-Za-z0-9._-]+)\z') {
            throw 'Malformed SHA256SUMS entry.'
        }
        $hash = $Matches[1]
        $name = $Matches[2]
        if ($name -ceq 'SHA256SUMS' -or $name -cnotin $expected -or -not $seen.Add($name)) {
            throw 'Unexpected or duplicate SHA256SUMS entry.'
        }
        if ((Get-FileHash -LiteralPath (Join-Path $Directory $name) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $hash) {
            throw "SHA256 mismatch: $name"
        }
    }
    if ($seen.Count -ne $expected.Count - 1) {
        throw 'SHA256SUMS does not cover every release asset.'
    }
}
