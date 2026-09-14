#Requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Version,
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts'),
    [string] $GitHubOutput
)

. (Join-Path $PSScriptRoot 'Release.Common.ps1')
$Version = Get-ReleaseVersion -Tag "v$Version"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$destination = Join-Path $OutputDirectory "TinyTranscriber-$Version-win-x64"
if (Test-Path -LiteralPath $destination) {
    throw "Refusing to overwrite an existing package: $destination"
}

foreach ($relativePath in @(
    'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'SECURITY.md', 'global.json', 'NuGet.config',
    'docs\azure-setup.md', 'docs\releasing.md', 'tools\Deploy-Speech.ps1', 'infra\main.bicep',
    'src\TinyTranscriber\packages.lock.json', 'tests\TinyTranscriber.Tests\packages.lock.json'
)) {
    if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $relativePath) -PathType Leaf)) {
        throw "Required release input is missing: $relativePath"
    }
}
foreach ($command in 'dotnet', 'git', 'az') {
    $null = Get-Command $command -ErrorAction Stop
}

$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$stage = Join-Path $OutputDirectory ".release-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $stage
Push-Location $repositoryRoot
try {
    $commit = (Invoke-ReleaseNative git @('rev-parse', '--verify', 'HEAD')).Trim()
    if ($commit -cnotmatch '\A[0-9a-f]{40}\z') { throw 'Cannot identify source commit.' }
    $sdk = (Invoke-ReleaseNative dotnet @('--version')).Trim()
    $expectedSdk = (Get-Content -LiteralPath '.\global.json' -Raw | ConvertFrom-Json).sdk.version
    if ($sdk -cne $expectedSdk) { throw "Expected SDK $expectedSdk, got $sdk." }

    $publicFeed = 'https://api.nuget.org/v3/index.json'
    $nugetConfig = [xml](Get-Content -LiteralPath '.\NuGet.config' -Raw)
    foreach ($sectionName in 'packageSources', 'auditSources') {
        $section = $nugetConfig.configuration.SelectSingleNode($sectionName)
        if ($null -eq $section -or $section.SelectNodes('clear').Count -ne 1 -or
            $section.SelectNodes('add').Count -ne 1 -or $section.add.value -cne $publicFeed) {
            throw "NuGet.config must clear $sectionName and specify only the official public NuGet feed."
        }
    }
    $effectiveSources = @(Invoke-ReleaseNative dotnet @('nuget', 'list', 'source', '--format', 'short') |
        ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($effectiveSources.Count -ne 1 -or $effectiveSources[0] -cne "E $publicFeed") {
        throw 'Public nuget.org must be enabled in the effective NuGet configuration. No sources were changed and no restore was attempted.'
    }
    $appProject = '.\src\TinyTranscriber\TinyTranscriber.csproj'
    $auditProperties = @('-p:NuGetAudit=true', '-p:NuGetAuditMode=all', '-p:NuGetAuditLevel=low')
    $auditSettings = Invoke-ReleaseNative dotnet (@(
        'msbuild', $appProject, '-nologo', '-getProperty:NuGetAudit,NuGetAuditMode,NuGetAuditLevel'
    ) + $auditProperties) | ConvertFrom-Json
    if ($auditSettings.Properties.NuGetAudit -ine 'true' -or
        $auditSettings.Properties.NuGetAuditMode -ine 'all' -or $auditSettings.Properties.NuGetAuditLevel -ine 'low') {
        throw 'Effective restore properties must enable all-dependency NuGet auditing at every severity.'
    }

    foreach ($script in Get-ChildItem -LiteralPath '.\tools' -Filter '*.ps1' -File) {
        $parseErrors = $null
        $null = [Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref] $null, [ref] $parseErrors)
        if ($parseErrors.Count -gt 0) { throw "PowerShell syntax errors in $($script.Name): $parseErrors" }
    }
    Invoke-ReleaseNative az @(
        'bicep', 'build', '--file', '.\infra\main.bicep',
        '--outfile', (Join-Path $stage 'main.json'), '--only-show-errors'
    )

    $auditArguments = @(
        '--locked-mode', '--no-http-cache', '--source', $publicFeed,
        '-warnaserror:NU1900,NU1901,NU1902,NU1903,NU1904,NU1905'
    ) + $auditProperties
    Invoke-ReleaseNative dotnet (@('restore', '.\TinyTranscriber.slnx') + $auditArguments)
    Invoke-ReleaseNative dotnet @(
        'test', '.\TinyTranscriber.slnx', '--configuration', 'Release', '--no-restore',
        '--logger', 'trx', '--results-directory', (Join-Path $stage 'test-results')
    )

    $publishProperties = @(
        '--runtime', 'win-x64', '-p:SelfContained=true', '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true'
    )
    Invoke-ReleaseNative dotnet (@('restore', $appProject) + $publishProperties + $auditArguments)
    $publishDirectory = Join-Path $stage 'publish'
    Invoke-ReleaseNative dotnet (@(
        'publish', $appProject, '--configuration', 'Release', '--no-restore',
        '--output', $publishDirectory, "-p:Version=$Version",
        "-p:InformationalVersion=$Version+$commit", '-p:IncludeSourceRevisionInInformationalVersion=false',
        '-p:DebugType=None', '-p:DebugSymbols=false', '-p:Deterministic=true',
        '-p:ContinuousIntegrationBuild=true', '-p:EmbedUntrackedSources=false',
        "-p:PathMap=$repositoryRoot=/_/"
    ) + $publishProperties)

    $executable = Join-Path $publishDirectory 'TinyTranscriber.exe'
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw 'Publish did not produce TinyTranscriber.exe.'
    }
    $looseBinaries = @(Get-ChildItem -LiteralPath $publishDirectory -Recurse -File |
        Where-Object { $_.Extension -in '.dll', '.pdb', '.exe' -and $_.FullName -cne $executable })
    if ($looseBinaries.Count -gt 0) { throw 'Publish produced loose binaries or symbols instead of a single executable.' }

    $bundle = Join-Path $stage 'bundle'
    $assets = Join-Path $stage 'assets'
    $null = New-Item -ItemType Directory -Path $bundle, $assets
    Copy-Item -LiteralPath $executable -Destination (Join-Path $bundle 'TinyTranscriber.exe')
    foreach ($name in 'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'SECURITY.md') {
        Copy-Item -LiteralPath (Join-Path $repositoryRoot $name) -Destination $bundle
    }
    foreach ($directory in 'docs', 'infra') {
        $source = Join-Path $repositoryRoot $directory
        $target = Join-Path $bundle $directory
        $null = New-Item -ItemType Directory -Path $target
        $extension = if ($directory -eq 'infra') { '.bicep' } else { '.md' }
        foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File | Where-Object Extension -EQ $extension) {
            if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing linked release input: $($file.Name)" }
            $relative = [IO.Path]::GetRelativePath($source, $file.FullName)
            $copyPath = Join-Path $target $relative
            $null = New-Item -ItemType Directory -Path (Split-Path $copyPath -Parent) -Force
            Copy-Item -LiteralPath $file.FullName -Destination $copyPath
        }
    }
    $null = New-Item -ItemType Directory -Path (Join-Path $bundle 'tools')
    Copy-Item -LiteralPath '.\tools\Deploy-Speech.ps1' -Destination (Join-Path $bundle 'tools')

    $restoreAssets = Get-Content -LiteralPath '.\src\TinyTranscriber\obj\project.assets.json' -Raw |
        ConvertFrom-Json -AsHashtable
    $packages = @{}
    foreach ($library in $restoreAssets.libraries.GetEnumerator()) {
        if ($library.Value.type -eq 'package') { $packages[$library.Key.ToLowerInvariant()] = $library.Value.path }
    }
    foreach ($framework in $restoreAssets.project.frameworks.Values) {
        if (-not $framework.ContainsKey('downloadDependencies')) { continue }
        foreach ($dependency in @($framework.downloadDependencies)) {
            $packageVersion = $dependency.version.Trim('[', ']').Split(',')[0].Trim()
            $packageKey = "$($dependency.name)/$packageVersion".ToLowerInvariant()
            $packages[$packageKey] = $packageKey
        }
    }
    $runtimePacks = @()
    foreach ($package in $packages.GetEnumerator() | Sort-Object Key) {
        $parts = $package.Key.Split('/')
        $packageDirectory = $null
        foreach ($cacheRoot in $restoreAssets.packageFolders.Keys) {
            $candidate = Join-Path $cacheRoot $package.Value
            if (Test-Path -LiteralPath $candidate -PathType Container) { $packageDirectory = $candidate; break }
        }
        if (-not $packageDirectory) { throw "Resolved package directory is missing: $($package.Key)" }
        $notices = @(Get-ChildItem -LiteralPath $packageDirectory -File |
            Where-Object Name -Match '^(LICENSE|LICENCE|NOTICE|THIRD[-_]?PARTY[-_]?NOTICES)([._-].*)?$')
        if ($parts[0] -in 'microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64') {
            $hasLicense = @($notices | Where-Object Name -Match '^LICEN[SC]E').Count -gt 0
            $hasThirdPartyNotice = @($notices | Where-Object Name -Match '^THIRD[-_]?PARTY[-_]?NOTICES').Count -gt 0
            if (-not $hasLicense -or ($parts[0] -eq 'microsoft.netcore.app.runtime.win-x64' -and -not $hasThirdPartyNotice)) {
                throw "Runtime license or third-party notice missing from $($package.Key)."
            }
            $runtimePacks += [ordered]@{
                id = $parts[0]
                version = $parts[1]
                licenseIncluded = $hasLicense
                thirdPartyNoticeIncluded = $hasThirdPartyNotice
            }
        }
        if ($notices.Count -gt 0) {
            $noticeDirectory = Join-Path (Join-Path $bundle 'licenses') $package.Value
            $null = New-Item -ItemType Directory -Path $noticeDirectory -Force
            foreach ($notice in $notices) {
                Copy-Item -LiteralPath $notice.FullName -Destination $noticeDirectory
            }
        }
    }
    if ($runtimePacks.Count -ne 2) { throw 'Both .NET and Windows Desktop win-x64 runtime pack notices are required.' }

    $metadata = [ordered]@{
        application = 'Tiny Transcriber'
        version = $Version
        commit = $commit
        runtimeIdentifier = 'win-x64'
        targetFramework = 'net10.0-windows'
        sdkVersion = $sdk
        selfContained = $true
        singleFile = $true
        signed = $false
        runtimePacks = $runtimePacks
    }
    $metadata | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $assets 'release-metadata.json') -Encoding utf8NoBOM
    Copy-Item -LiteralPath (Join-Path $assets 'release-metadata.json') -Destination $bundle
    Copy-Item -LiteralPath $executable -Destination (Join-Path $assets "TinyTranscriber-$Version-win-x64.exe")
    foreach ($name in 'LICENSE', 'THIRD-PARTY-NOTICES.md') {
        Copy-Item -LiteralPath (Join-Path $repositoryRoot $name) -Destination $assets
    }
    [IO.Compression.ZipFile]::CreateFromDirectory(
        $bundle, (Join-Path $assets "TinyTranscriber-$Version-win-x64.zip"), [IO.Compression.CompressionLevel]::Optimal, $false
    )
    $checksums = foreach ($file in Get-ChildItem -LiteralPath $assets -File | Sort-Object Name) {
        '{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $file.Name
    }
    $checksums | Set-Content -LiteralPath (Join-Path $assets 'SHA256SUMS') -Encoding ascii
    Assert-ReleaseAssetSet -Directory $assets -Version $Version -ExpectedCommit $commit
    [IO.Directory]::Move($assets, $destination)
    if ($GitHubOutput) {
        "asset-directory=$destination" | Add-Content -LiteralPath $GitHubOutput -Encoding utf8NoBOM
    }
    Write-Output "Release assets: $destination"
}
finally {
    Pop-Location
    Remove-Item -LiteralPath $stage -Recurse -Force
}
