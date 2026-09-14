#Requires -Version 7.4
[CmdletBinding()]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidGlobalVars', '', Justification = 'Native mocks share state with child scripts; the named test state is removed in finally.')]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'Release.Common.ps1')

function Assert-Condition {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Failure {
    param([scriptblock] $Action, [string] $Message)
    $failed = $false
    try { & $Action } catch { $failed = $true }
    Assert-Condition $failed $Message
}

foreach ($tag in 'v0.0.0', 'v1.2.3', 'v1.2.3-rc.1', 'v1.2.3-0.alpha-2', 'v65534.65534.65534') {
    $null = Get-ReleaseVersion $tag
}
foreach ($tag in '1.2.3', 'v01.2.3', 'v1.2.3-01', 'v1.2.3+meta', 'v1.2.3;bad', "v1.2.3`n", 'v65535.0.0', 'v1.2', 'v1.2.3-') {
    Assert-Failure { Get-ReleaseVersion $tag } "Invalid version accepted: $tag"
}

$fixture = Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))) ".release-tests-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $fixture
$originalGitHubHost = $env:GH_HOST
$originalPromptDisabled = $env:GH_PROMPT_DISABLED
$global:TinyTranscriberReleaseTestMock = @{
    Commit = '0123456789abcdef0123456789abcdef01234567'
    FailCommand = ''
    Calls = [Collections.Generic.List[string]]::new()
    Release = $null
    RemoteAssets = Join-Path $fixture 'remote'
    CorruptDownload = $false
    MoveTag = $false
    TagReads = 0
}

# Child scripts resolve these functions, never real native tools or network APIs.
function git {
    $global:LASTEXITCODE = 0
    $global:TinyTranscriberReleaseTestMock.Calls.Add("git $($args -join ' ')")
    if ($global:TinyTranscriberReleaseTestMock.FailCommand -eq 'git') { $global:LASTEXITCODE = 9; return }
    if ($args[0] -eq 'rev-parse') { return $global:TinyTranscriberReleaseTestMock.Commit }
}

function dotnet {
    $global:LASTEXITCODE = 0
    $global:TinyTranscriberReleaseTestMock.Calls.Add("dotnet $($args -join ' ')")
    if ($global:TinyTranscriberReleaseTestMock.FailCommand -eq "dotnet $($args[0])") { $global:LASTEXITCODE = 9; return }
    if ($args[0] -eq '--version') { return '10.0.401' }
    if ($args[0] -eq 'publish') {
        $output = $args[[array]::IndexOf($args, '--output') + 1]
        $null = New-Item -ItemType Directory -Path $output -Force
        [IO.File]::WriteAllBytes((Join-Path $output 'TinyTranscriber.exe'), [byte[]](77, 90, 1, 2, 3))
    }
}

function az {
    $global:LASTEXITCODE = 0
    $global:TinyTranscriberReleaseTestMock.Calls.Add("az $($args -join ' ')")
    if ($global:TinyTranscriberReleaseTestMock.FailCommand -eq 'az') { $global:LASTEXITCODE = 9; return }
    '{}' | Set-Content -LiteralPath $args[[array]::IndexOf($args, '--outfile') + 1]
}

function gh {
    $global:LASTEXITCODE = 0
    $global:TinyTranscriberReleaseTestMock.Calls.Add("gh $($args -join ' ')")
    if ($args[0] -eq 'api') {
        $endpoint = $args[-1]
        if ($endpoint -match '/git/ref/tags/') {
            $global:TinyTranscriberReleaseTestMock.TagReads++
            $sha = if ($global:TinyTranscriberReleaseTestMock.MoveTag -and $global:TinyTranscriberReleaseTestMock.TagReads -gt 1) { 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' } else { $global:TinyTranscriberReleaseTestMock.Commit }
            return @{ object = @{ type = 'commit'; sha = $sha } } | ConvertTo-Json -Compress
        }
        if ($endpoint -match '/compare/') {
            return @{ status = 'ahead'; merge_base_commit = @{ sha = $global:TinyTranscriberReleaseTestMock.Commit } } | ConvertTo-Json -Compress
        }
        if ($endpoint -match '/assets\?') {
            $assets = @(Get-ChildItem -LiteralPath $global:TinyTranscriberReleaseTestMock.RemoteAssets -File |
                ForEach-Object { @{ name = $_.Name; state = 'uploaded' } })
            return ConvertTo-Json -InputObject $assets -Compress
        }
        if ($endpoint -match '/releases\?') {
            if ($null -eq $global:TinyTranscriberReleaseTestMock.Release) { return '[[]]' }
            return '[[],' + (ConvertTo-Json -InputObject @($global:TinyTranscriberReleaseTestMock.Release) -Compress) + ']'
        }
        throw "Unexpected mocked GitHub API: $endpoint"
    }
    switch ($args[1]) {
        'create' {
            Assert-Condition ($args -contains '--draft') 'Publisher must create a draft.'
            $global:TinyTranscriberReleaseTestMock.Release = @{
                id = 17; tag_name = $args[2]; draft = $true
                prerelease = $args -contains '--prerelease'
                html_url = "https://github.com/msucharda/transcriber/releases/tag/$($args[2])"
            }
        }
        'upload' {
            Assert-Condition ([bool] $global:TinyTranscriberReleaseTestMock.Release.draft) 'Uploads require a draft.'
            if ($global:TinyTranscriberReleaseTestMock.FailCommand -eq 'gh upload') { $global:LASTEXITCODE = 9; return }
            $null = New-Item -ItemType Directory -Path $global:TinyTranscriberReleaseTestMock.RemoteAssets -Force
            foreach ($path in $args[5..($args.Count - 1)]) { Copy-Item -LiteralPath $path -Destination $global:TinyTranscriberReleaseTestMock.RemoteAssets }
        }
        'download' {
            $directory = $args[[array]::IndexOf($args, '--dir') + 1]
            foreach ($file in Get-ChildItem -LiteralPath $global:TinyTranscriberReleaseTestMock.RemoteAssets -File) {
                Copy-Item -LiteralPath $file.FullName -Destination $directory
            }
            if ($global:TinyTranscriberReleaseTestMock.CorruptDownload) { 'tampered' | Add-Content -LiteralPath (Join-Path $directory 'LICENSE') }
        }
        'edit' {
            Assert-Condition ($args -contains '--draft=false') 'Only final publication should edit the draft.'
            $global:TinyTranscriberReleaseTestMock.Release.draft = $false
        }
        default { throw "Unexpected mocked GitHub command: $($args -join ' ')" }
    }
}

try {
    foreach ($directory in 'tools', 'docs', 'infra', 'src\TinyTranscriber\obj', 'tests\TinyTranscriber.Tests', 'cache') {
        $null = New-Item -ItemType Directory -Path (Join-Path $fixture $directory) -Force
    }
    foreach ($name in 'Release.Common.ps1', 'Publish-Release.ps1', 'Publish-GitHubRelease.ps1') {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $fixture 'tools')
    }
    foreach ($name in 'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'SECURITY.md', 'docs\azure-setup.md', 'docs\releasing.md',
        'src\TinyTranscriber\packages.lock.json', 'tests\TinyTranscriber.Tests\packages.lock.json',
        'tools\Deploy-Speech.ps1', 'infra\main.bicep') {
        'fixture' | Set-Content -LiteralPath (Join-Path $fixture $name)
    }
    '{"sdk":{"version":"10.0.401"}}' | Set-Content -LiteralPath (Join-Path $fixture 'global.json')
    $cache = Join-Path $fixture 'cache'
    $dependencies = @()
    foreach ($id in 'microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64') {
        $packageDirectory = Join-Path $cache "$id\10.0.12"
        $null = New-Item -ItemType Directory -Path $packageDirectory -Force
        'runtime license' | Set-Content -LiteralPath (Join-Path $packageDirectory 'LICENSE.TXT')
        if ($id -eq 'microsoft.netcore.app.runtime.win-x64') {
            'runtime notices' | Set-Content -LiteralPath (Join-Path $packageDirectory 'THIRD-PARTY-NOTICES.TXT')
        }
        $dependencies += @{ name = $id; version = '[10.0.12, 10.0.12]' }
    }
    @{
        libraries = @{}
        packageFolders = @{ $cache = @{} }
        project = @{ frameworks = @{ 'net10.0-windows7.0' = @{ downloadDependencies = $dependencies } } }
    } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $fixture 'src\TinyTranscriber\obj\project.assets.json')
    $build = Join-Path $fixture 'tools\Publish-Release.ps1'
    $publish = Join-Path $fixture 'tools\Publish-GitHubRelease.ps1'
    $output = Join-Path $fixture 'artifacts'
    $null = New-Item -ItemType Directory -Path $output
    $sentinel = Join-Path $output 'keep-me.txt'
    'unrelated user output' | Set-Content -LiteralPath $sentinel
    & $build -Version '1.2.3' -OutputDirectory $output
    $assetDirectory = Join-Path $output 'TinyTranscriber-1.2.3-win-x64'
    Assert-ReleaseAssetSet -Directory $assetDirectory -Version '1.2.3' -ExpectedCommit $global:TinyTranscriberReleaseTestMock.Commit
    $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $assetDirectory 'TinyTranscriber-1.2.3-win-x64.zip'))
    try {
        $entries = @($zip.Entries.FullName | ForEach-Object { $_.Replace('\', '/') })
        foreach ($entry in 'TinyTranscriber.exe', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'docs/azure-setup.md',
            'tools/Deploy-Speech.ps1', 'infra/main.bicep',
            'licenses/microsoft.netcore.app.runtime.win-x64/10.0.12/LICENSE.TXT',
            'licenses/microsoft.netcore.app.runtime.win-x64/10.0.12/THIRD-PARTY-NOTICES.TXT',
            'licenses/microsoft.windowsdesktop.app.runtime.win-x64/10.0.12/LICENSE.TXT') {
            Assert-Condition ($entry -cin $entries) "Missing ZIP content: $entry"
        }
        Assert-Condition (-not @($entries | Where-Object { $_ -match '\.(pdb|dll)$' }).Count) 'ZIP contains loose binaries.'
    }
    finally { $zip.Dispose() }
    $restoreCalls = @($global:TinyTranscriberReleaseTestMock.Calls | Where-Object { $_.StartsWith('dotnet restore ') })
    Assert-Condition ($restoreCalls.Count -eq 2) 'Expected solution and publish-specific restore.'
    foreach ($call in $restoreCalls) {
        Assert-Condition ($call.Contains('--locked-mode') -and $call.Contains('--no-http-cache') -and
            $call.Contains('NuGetAuditMode=all') -and $call.Contains('NU1900,NU1901,NU1902,NU1903,NU1904,NU1905')) 'Restore is not fail-closed.'
    }
    Assert-Failure { & $build -Version '1.2.3' -OutputDirectory $output } 'Existing output was overwritten.'
    $global:TinyTranscriberReleaseTestMock.FailCommand = 'dotnet restore'
    Assert-Failure { & $build -Version '1.2.4' -OutputDirectory $output } 'Restore/audit failure did not stop packaging.'
    $global:TinyTranscriberReleaseTestMock.FailCommand = 'dotnet test'
    Assert-Failure { & $build -Version '1.2.4' -OutputDirectory $output } 'Native test failure did not stop packaging.'
    $global:TinyTranscriberReleaseTestMock.FailCommand = ''
    $setupDoc = Join-Path $fixture 'docs\azure-setup.md'
    Remove-Item -LiteralPath $setupDoc
    Assert-Failure { & $build -Version '1.2.4' -OutputDirectory $output } 'Missing Azure setup was silently skipped.'
    'fixture' | Set-Content -LiteralPath $setupDoc
    $notice = Join-Path $cache 'microsoft.netcore.app.runtime.win-x64\10.0.12\LICENSE.TXT'
    Remove-Item -LiteralPath $notice
    Assert-Failure { & $build -Version '1.2.4' -OutputDirectory $output } 'Missing runtime license was silently skipped.'
    'runtime license' | Set-Content -LiteralPath $notice
    Assert-Condition (Test-Path -LiteralPath $sentinel) 'Packaging removed unrelated output.'
    Assert-Condition (@(Get-ChildItem -LiteralPath $output -Directory -Filter '.release-*').Count -eq 0) 'Owned staging was not cleaned.'
    $global:TinyTranscriberReleaseTestMock.FailCommand = 'git'
    Assert-Failure {
        Assert-ReleaseSource -Tag 'v1.2.3' -ExpectedCommit $global:TinyTranscriberReleaseTestMock.Commit
    } 'Failed Git ancestry command was ignored.'
    $global:TinyTranscriberReleaseTestMock.FailCommand = ''

    & $publish -Tag 'v1.2.3' -ExpectedCommit $global:TinyTranscriberReleaseTestMock.Commit -AssetDirectory $assetDirectory
    Assert-Condition (-not $global:TinyTranscriberReleaseTestMock.Release.draft) 'Verified release was not published.'
    Assert-Failure { & $publish -Tag 'v1.2.3' -ExpectedCommit $global:TinyTranscriberReleaseTestMock.Commit -AssetDirectory $assetDirectory } 'Existing published version was overwritten.'
    $global:TinyTranscriberReleaseTestMock.Release.draft = $true
    Assert-Failure { & $publish -Tag 'v1.2.3' -ExpectedCommit $global:TinyTranscriberReleaseTestMock.Commit -AssetDirectory $assetDirectory } 'Existing draft was overwritten.'
    $global:TinyTranscriberReleaseTestMock.Release = $null
    $global:TinyTranscriberReleaseTestMock.FailCommand = 'gh upload'
    Assert-Failure { & $publish -Tag 'v1.2.3' -ExpectedCommit $global:TinyTranscriberReleaseTestMock.Commit -AssetDirectory $assetDirectory } 'Failed upload was published.'
    Assert-Condition ([bool] $global:TinyTranscriberReleaseTestMock.Release.draft) 'Upload failure did not retain draft.'
    $global:TinyTranscriberReleaseTestMock.FailCommand = ''
    $global:TinyTranscriberReleaseTestMock.Release = $null
    $global:TinyTranscriberReleaseTestMock.CorruptDownload = $true
    Assert-Failure { & $publish -Tag 'v1.2.3' -ExpectedCommit $global:TinyTranscriberReleaseTestMock.Commit -AssetDirectory $assetDirectory } 'Corrupted download was published.'
    Assert-Condition ([bool] $global:TinyTranscriberReleaseTestMock.Release.draft) 'Verification failure did not retain draft.'
    $global:TinyTranscriberReleaseTestMock.CorruptDownload = $false
    $global:TinyTranscriberReleaseTestMock.Release = $null
    $global:TinyTranscriberReleaseTestMock.MoveTag = $true
    $global:TinyTranscriberReleaseTestMock.TagReads = 0
    Assert-Failure { & $publish -Tag 'v1.2.3' -ExpectedCommit $global:TinyTranscriberReleaseTestMock.Commit -AssetDirectory $assetDirectory } 'Moved tag was published.'
    Assert-Condition ([bool] $global:TinyTranscriberReleaseTestMock.Release.draft) 'Tag movement did not retain draft.'
    $global:TinyTranscriberReleaseTestMock.MoveTag = $false
    $global:TinyTranscriberReleaseTestMock.Release = $null
    & $build -Version '1.2.4-rc.1' -OutputDirectory $output
    Remove-Item -LiteralPath $global:TinyTranscriberReleaseTestMock.RemoteAssets -Recurse -Force
    & $publish -Tag 'v1.2.4-rc.1' -ExpectedCommit $global:TinyTranscriberReleaseTestMock.Commit `
        -AssetDirectory (Join-Path $output 'TinyTranscriber-1.2.4-rc.1-win-x64')
    Assert-Condition ([bool] $global:TinyTranscriberReleaseTestMock.Release.prerelease) 'Prerelease tag was published as stable.'
    Assert-Condition ($global:TinyTranscriberReleaseTestMock.Calls[-2].Contains('--latest=false')) 'Prerelease could become latest.'
    Assert-Condition (Test-Path -LiteralPath $sentinel) 'Publishing removed unrelated output.'
    Write-Output 'PASS: versions, package layout/notices/checksums, locked audit flags, native failures, overwrite guards, draft upload/download verification, and moved-tag rejection (offline mocks).'
}
finally {
    $env:GH_HOST = $originalGitHubHost
    $env:GH_PROMPT_DISABLED = $originalPromptDisabled
    Remove-Variable -Name TinyTranscriberReleaseTestMock -Scope Global
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
