# pattern: Imperative Shell
<#
.SYNOPSIS
    Update an existing YOLO checkout and build a Windows nightly.
.EXAMPLE
    .\build_windows_nightly.ps1 -Project C:\scratch\Claude\YARG
.EXAMPLE
    .\build_windows_nightly.ps1 -Project .\YARG -CheckoutOnly
.EXAMPLE
    .\build_windows_nightly.ps1 -Project .\YARG -OutputRoot 'N:\scratch\yarg\builds\Windows'
.NOTES
    Close Unity first. The script fast-forwards the current branch, updates a
    clean YARG.Core to the pinned commit, and refuses local edits. Builds reuse
    Unity's cache by default. Use -Clean to clear Library and Temp before
    building. Finished players and logs are stored under Builds/WindowsNightly
    unless -OutputRoot is supplied. A shareable player ZIP is written to the
    output root's current folder. Paths are relative to the calling directory.
    Unity's build log is displayed live and saved under the run's logs folder.
    Builds require licensed Unity, .NET, CMake 3.25+, and Visual Studio 2022 C++
    desktop build tools. Use -UseBundledAudio to skip the native DLL rebuild.
    Use -DiscardGeneratedFiles to remove untracked Addressables linker files.
#>
[CmdletBinding()]
param(
    [string] $Project = '.\YARG',
    [Alias('Root')]
    [string] $OutputRoot,
    [string] $Remote = 'origin',
    [string] $Branch = 'yolo-build',
    [string] $UnityPath,
    [switch] $CheckoutOnly,
    [switch] $Clean,
    [switch] $UseBundledAudio,
    [switch] $DiscardGeneratedFiles
)

$ErrorActionPreference = 'Stop'

function Add-WindowsPlayerEntries {
    param($Archive, [string] $Directory, [string] $Prefix = '')

    foreach ($item in (Get-ChildItem -LiteralPath $Directory -Force)) {
        $name = $item.Name
        if ($name -in @('.git', '.DS_Store', '__MACOSX') -or $name.StartsWith('._') -or
            $name -match '(?:_DoNotShip|_ButDontShipItWithYourGame)$') {
            continue
        }
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Cannot include a linked player component in the ZIP: $($item.FullName)"
        }
        $entry = "$Prefix$name"
        if ($item.PSIsContainer) {
            $Archive.CreateEntry("$entry/") | Out-Null
            Add-WindowsPlayerEntries -Archive $Archive -Directory $item.FullName -Prefix "$entry/"
        }
        else {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($Archive, $item.FullName, $entry,
                [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
}

function Publish-WindowsPlayerZip {
    param([string] $Player, [string] $OutputRoot, [string] $RunName)

    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $current = Join-Path $OutputRoot 'current'
    $previous = Join-Path $OutputRoot 'previous'
    [IO.Directory]::CreateDirectory($current) | Out-Null
    $destination = Join-Path $current "$RunName.zip"
    $olderDestination = Join-Path $previous "$RunName.zip"
    $temporary = Join-Path $current ('.package-' + [guid]::NewGuid().ToString('N'))
    $lock = [IO.File]::Open((Join-Path $OutputRoot '.nightly-share.lock'),
        [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        if ((Test-Path -LiteralPath $destination) -or (Test-Path -LiteralPath $olderDestination)) {
            throw "Share archive already exists: $destination"
        }
        $archive = [IO.Compression.ZipFile]::Open($temporary, [IO.Compression.ZipArchiveMode]::Create)
        try { Add-WindowsPlayerEntries -Archive $archive -Directory $Player }
        finally { $archive.Dispose() }
        [IO.File]::Move($temporary, $destination)

        $archives = @(Get-ChildItem -LiteralPath $current -File | Where-Object {
            $_.Name -cmatch '^windows-nightly-\d{8}-\d{6}-[0-9a-f]{6}\.zip$' -and
                !($_.Attributes -band [IO.FileAttributes]::ReparsePoint)
        } | Sort-Object Name -Descending)
        if ($archives.Count -gt 1) {
            [IO.Directory]::CreateDirectory($previous) | Out-Null
            foreach ($old in ($archives | Select-Object -Skip 1)) {
                [IO.File]::Move($old.FullName, (Join-Path $previous $old.Name))
            }
        }
        if (Test-Path -LiteralPath $destination) { return $destination }
        return $olderDestination
    }
    finally {
        $lock.Dispose()
        [IO.File]::Delete($temporary)
    }
}

function Invoke-Checked {
    param([string] $Program, [string[]] $Arguments)

    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Program $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Watch-UnityBuildLog {
    param([Diagnostics.Process] $Process, [string] $LogPath)

    $reader = $null
    try {
        do {
            # Read once more after Unity exits to include its final messages.
            $exited = $Process.WaitForExit(250)
            if ($null -eq $reader -and [IO.File]::Exists($LogPath)) {
                $share = [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete
                $stream = [IO.File]::Open($LogPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, $share)
                $reader = [IO.StreamReader]::new($stream)
            }
            if ($null -ne $reader) {
                while ($null -ne ($line = $reader.ReadLine())) {
                    Write-Host $line
                }
            }
        } until ($exited)
    }
    catch {
        Write-Warning "Could not stream Unity's build log at $LogPath. $($_.Exception.Message)"
        $Process.WaitForExit()
    }
    finally {
        if ($null -ne $reader) { $reader.Dispose() }
    }
}

function Assert-NoLocalPaths {
    param([string] $Player, [string] $ProjectPath)

    $binaryText = [Text.Encoding]::GetEncoding(28591)
    $needles = foreach ($path in @($ProjectPath, $ProjectPath.Replace('\', '/'))) {
        foreach ($encoding in @([Text.Encoding]::UTF8, [Text.Encoding]::Unicode, [Text.Encoding]::BigEndianUnicode)) {
            $binaryText.GetString($encoding.GetBytes($path))
        }
    }
    $files = Get-ChildItem -LiteralPath $Player -File -Recurse | Where-Object {
        $_.Extension -in @('.dll', '.exe', '.json')
    }
    foreach ($file in $files) {
        $content = $binaryText.GetString([IO.File]::ReadAllBytes($file.FullName))
        foreach ($needle in $needles) {
            if ($content.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                throw "Player contains a local checkout path in $($file.Name)."
            }
        }
    }
}

function Invoke-Git {
    param([string[]] $Arguments)

    # The published .gitmodules uses an SSH host alias from the Mac checkout.
    Invoke-Checked git (@('-c', 'core.longpaths=true', '-c',
        'url.https://github.com/.insteadOf=git@github-xaiax:') + $Arguments)
}

function Restore-LfsFiles {
    param([string] $Project)

    Invoke-Git @('-C', $Project, 'lfs', 'install', '--local') | Out-Host
    Invoke-Git @('-C', $Project, 'lfs', 'pull', '--include=*', '--exclude=') | Out-Host
    Invoke-Git @('-C', $Project, '-c', 'lfs.fetchexclude=', 'lfs', 'fsck', 'HEAD') | Out-Host
    $pointers = @(Invoke-Git @('-C', $Project, 'lfs', 'ls-files', '--long') |
        Where-Object { $_ -match '^[0-9a-f]{64} - ' })
    if ($pointers.Count -gt 0) {
        throw "Git LFS left $($pointers.Count) pointer files in $Project. First pointer: $($pointers[0])"
    }
}

function Assert-CleanCheckout {
    param([string] $Path, [switch] $ApplyGeneratedCleanup, [switch] $AllowCoreCommitMismatch)

    $statusArguments = @('-C', $Path, 'status', '--untracked-files=normal', '--ignore-submodules=none')
    $changes = @(Invoke-Git ($statusArguments + '--porcelain'))
    if ($AllowCoreCommitMismatch -and $changes -ccontains ' M YARG.Core') {
        # Porcelain v2 reports a changed submodule commit separately from tracked and untracked edits.
        $coreStatus = @(Invoke-Git ($statusArguments + @('--porcelain=v2', '--', 'YARG.Core')))
        if ($coreStatus.Count -eq 1 -and $coreStatus[0] -cmatch '^1 \.M SC\.\. .* YARG\.Core$') {
            $changes = @($changes | Where-Object { $_ -cne ' M YARG.Core' })
        }
    }
    $linkerFiles = @('Assets/Settings/AddressableAssetsData/link.xml',
        'Assets/Settings/AddressableAssetsData/link.xml.meta')
    $generated = @($linkerFiles | Where-Object { $changes -ccontains "?? $_" })
    if ($DiscardGeneratedFiles) {
        $generatedEntries = @($generated | ForEach-Object { "?? $_" })
        $changes = @($changes | Where-Object { $generatedEntries -cnotcontains $_ })
    }
    if ($changes.Count -gt 0) {
        if (!$DiscardGeneratedFiles -and $generated.Count -eq $changes.Count) {
            throw 'Unity left untracked Addressables linker files. Rerun with -DiscardGeneratedFiles to remove them.'
        }
        throw "Save or remove the local changes in $Path before building.`n$($changes -join "`n")"
    }
    if ($DiscardGeneratedFiles -and $ApplyGeneratedCleanup) {
        foreach ($file in $generated) {
            Write-Host "Removing generated Addressables file $file"
            Remove-Item -LiteralPath (Join-Path $Path $file)
        }
    }
}

function Clear-UnityCache {
    param([string] $Project)

    $cacheFolders = @('Library', 'Temp')
    foreach ($name in $cacheFolders) {
        $path = Join-Path $Project $name
        if (!(Test-Path -LiteralPath $path)) { continue }
        $item = Get-Item -LiteralPath $path -Force
        if (!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Expected a regular Unity cache directory: $path"
        }
        $tracked = @(Invoke-Git @('-C', $Project, 'ls-files', '--', "$name/"))
        if ($tracked.Count -gt 0) { throw "Git tracks files in $path. Clear the cache manually." }
        Invoke-Git @('-C', $Project, 'check-ignore', '--quiet', '--', "$name/")
    }
    foreach ($name in $cacheFolders) {
        $path = Join-Path $Project $name
        if (Test-Path -LiteralPath $path) { [IO.Directory]::Delete($path, $true) }
    }
}

# Unity compiles this temporary entry point before invoking it.
# The existing File > Make Nightly Build action expects the interactive editor.
$unityEntryPoint = @'
// pattern: Imperative Shell
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class YoloWindowsNightly
{
    public static void Build()
    {
        const BuildTarget target = BuildTarget.StandaloneWindows64;
        if (!Application.isBatchMode || EditorUserBuildSettings.activeBuildTarget != target ||
            !BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
            throw new InvalidOperationException("Start Unity in batch mode with Windows x64 build support and -buildTarget Win64.");

        string output = Argument("-yoloBuildOutput");
        string receipt = Argument("-yoloBuildReceipt");
        var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
        if (scenes.Length == 0) throw new InvalidOperationException("No enabled build scenes.");
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) throw new InvalidOperationException("Addressables settings are missing.");

        AddressableAssetSettings.CleanPlayerContent();
        AddressableAssetSettings.BuildPlayerContent(out var content);
        if (content == null || !string.IsNullOrEmpty(content.Error))
            throw new InvalidOperationException("Addressables build failed: " + content?.Error);

        var priorContentMode = settings.BuildAddressablesWithPlayerBuild;
        try
        {
            settings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(output, "YARG.exe"),
                target = target,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
                extraScriptingDefines = new[] { "YARG_NIGHTLY_BUILD" }
            }).summary;
            if (result.result != BuildResult.Succeeded || result.totalErrors != 0)
                throw new InvalidOperationException($"Windows player build {result.result}: {result.totalErrors} errors.");
        }
        finally
        {
            settings.BuildAddressablesWithPlayerBuild = priorContentMode;
        }

        File.WriteAllText(receipt, target.ToString());
    }

    private static string Argument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, name);
        if (index < 0 || index + 1 == args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
            throw new ArgumentException("Missing command-line argument " + name);
        return Path.GetFullPath(args[index + 1]);
    }
}
'@

if (!$CheckoutOnly -and $env:OS -ne 'Windows_NT') {
    throw 'Run the player build on Windows. Use -CheckoutOnly to update the existing checkout.'
}

Get-Command git -CommandType Application | Out-Null
Invoke-Checked git @('lfs', 'version') | Out-Host
if (!$CheckoutOnly) {
    Get-Command dotnet -CommandType Application | Out-Null
    if (!$UseBundledAudio) { Get-Command cmake -CommandType Application | Out-Null }
}

$projectPath = (Resolve-Path -LiteralPath $Project).ProviderPath
$gitRoot = Invoke-Git @('-C', $projectPath, 'rev-parse', '--show-toplevel')
if ([IO.Path]::GetFullPath($gitRoot).TrimEnd('\', '/') -ne $projectPath.TrimEnd('\', '/')) {
    throw "-Project must point to the root of the YARG checkout: $projectPath"
}
if (Test-Path -LiteralPath (Join-Path $projectPath 'Temp/UnityLockfile')) {
    throw "Close Unity before updating or building $projectPath."
}
if (!$OutputRoot) { $OutputRoot = Join-Path $projectPath 'Builds/WindowsNightly' }
$rootPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputRoot)
$currentBranch = Invoke-Git @('-C', $projectPath, 'branch', '--show-current')
if ($currentBranch -cne $Branch) { throw "Open the $Branch branch in $projectPath first. Current branch: $currentBranch" }
Assert-CleanCheckout $projectPath -AllowCoreCommitMismatch

$runName = 'windows-nightly-{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), ([guid]::NewGuid().ToString('N').Substring(0, 6))
$run = (New-Item -ItemType Directory -Path (Join-Path $rootPath $runName)).FullName
$logs = (New-Item -ItemType Directory -Path (Join-Path $run 'logs')).FullName
Start-Transcript -LiteralPath (Join-Path $logs 'build.log') | Out-Null

$entryPointPath = Join-Path $projectPath 'Assets/Editor/YoloWindowsNightly.cs'
$entryCreated = $false
$compilerResponse = Join-Path $projectPath 'Assets/csc.rsp'
$compilerResponseCreated = $false
$installedAudioHash = $null
$audioBackup = Join-Path $logs 'original-yarg_audio.dll'
$sourceDll = Join-Path $projectPath 'Assets/Plugins/YargAudio/Windows/x86_64/yarg_audio.dll'
$settings = Join-Path $projectPath 'Assets/Settings/AddressableAssetsData/AddressableAssetSettings.asset'
$settingsBackup = Join-Path $logs 'original-AddressableAssetSettings.asset'
$buildLock = $null
try {
    $gitDirectory = Invoke-Git @('-C', $projectPath, 'rev-parse', '--absolute-git-dir')
    $lockPath = Join-Path $gitDirectory 'yolo-windows-nightly.lock'
    $buildLock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::Write, [IO.FileShare]::None)
    Assert-CleanCheckout $projectPath -ApplyGeneratedCleanup -AllowCoreCommitMismatch
    Write-Host "Updating $Branch in $projectPath"
    Invoke-Git @('-C', $projectPath, 'fetch', '--', $Remote, "refs/heads/$Branch") | Out-Host
    Invoke-Git @('-C', $projectPath, 'merge', '--ff-only', 'FETCH_HEAD') | Out-Host
    Invoke-Git @('-C', $projectPath, 'submodule', 'update', '--init', '--recursive', '--checkout') | Out-Host
    $core = Join-Path $projectPath 'YARG.Core'
    $gameCommit = Invoke-Git @('-C', $projectPath, 'rev-parse', 'HEAD')
    $coreCommit = Invoke-Git @('-C', $core, 'rev-parse', 'HEAD')
    $corePin = Invoke-Git @('-C', $projectPath, 'rev-parse', 'HEAD:YARG.Core')
    if ($coreCommit -ne $corePin) { throw 'YARG.Core does not match the commit pinned by the game.' }
    Assert-CleanCheckout $projectPath
    Restore-LfsFiles $projectPath
    Restore-LfsFiles $core
    $receipt = [ordered]@{ status = 'CheckedOut'; branch = $Branch; gameCommit = $gameCommit; coreCommit = $coreCommit }
    $summaryPath = Join-Path $run 'build-info.json'
    $receipt | ConvertTo-Json | Set-Content -LiteralPath $summaryPath -Encoding UTF8
    if ($CheckoutOnly) {
        Write-Host "Clean checkout ready: $projectPath"
        return
    }

    $versionText = Get-Content -Raw -LiteralPath (Join-Path $projectPath 'ProjectSettings/ProjectVersion.txt')
    if ($versionText -notmatch '(?m)^m_EditorVersion:\s*(\S+)') { throw 'Unable to read the Unity version.' }
    $version = $Matches[1]
    if (!$UnityPath) { $UnityPath = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$version/Editor/Unity.exe" }
    $unity = (Get-Item -LiteralPath $UnityPath).FullName

    # Run the package restore tool locally even when the build output is on the NAS.
    $nugetTool = Join-Path ([IO.Path]::GetTempPath()) "yarg-nuget-$runName"
    $priorRollForward = $env:DOTNET_ROLL_FORWARD
    Push-Location -LiteralPath $projectPath
    try {
        # Allow the restore and native build tools to use a newer installed runtime.
        $env:DOTNET_ROLL_FORWARD = 'Major'
        Invoke-Checked dotnet @('tool', 'install', 'NuGetForUnity.Cli', '--version', '4.5.0', '--tool-path', $nugetTool) | Out-Host
        Invoke-Checked (Join-Path $nugetTool 'nugetforunity.exe') @('restore') | Out-Host
        if (!$UseBundledAudio) {
            $audioOutput = Join-Path $logs 'native-audio'
            Invoke-Checked dotnet @('run', '--project', 'scripts/NativeBuild', '--', 'build', '--output', $audioOutput) | Out-Host
            $rebuiltDll = Join-Path $audioOutput 'yarg_audio.dll'
            $installedAudioHash = (Get-FileHash -LiteralPath $rebuiltDll).Hash
            Copy-Item -LiteralPath $sourceDll -Destination $audioBackup
            Copy-Item -LiteralPath $rebuiltDll -Destination $sourceDll -Force
        }
    }
    finally {
        $env:DOTNET_ROLL_FORWARD = $priorRollForward
        Pop-Location
        if (Test-Path -LiteralPath $nugetTool) {
            Remove-Item -LiteralPath $nugetTool -Recurse -Force -ErrorAction Continue
        }
    }

    if ((Test-Path -LiteralPath $entryPointPath) -or (Test-Path -LiteralPath "$entryPointPath.meta")) {
        throw "Unity entry point or its metadata already exists: $entryPointPath"
    }
    if (Test-Path -LiteralPath (Join-Path $projectPath 'Temp/UnityLockfile')) {
        throw "Close Unity before building $projectPath."
    }
    if ((Test-Path -LiteralPath $compilerResponse) -or (Test-Path -LiteralPath "$compilerResponse.meta")) {
        throw "Unity compiler response file or its metadata already exists: $compilerResponse"
    }
    if ($Clean) {
        Write-Host 'Clearing Unity import caches for a clean build.'
        Clear-UnityCache $projectPath
    }
    else {
        Write-Host 'Reusing Unity import caches.'
    }
    if (Test-Path -LiteralPath $settings -PathType Leaf) { Copy-Item -LiteralPath $settings -Destination $settingsBackup }
    $entryCreated = $true
    Set-Content -LiteralPath $entryPointPath -Value $unityEntryPoint -Encoding UTF8
    $compilerResponseCreated = $true
    [IO.File]::WriteAllText($compilerResponse, ('-pathmap:"{0}"=/_/YARG' -f $projectPath) + "`n")
    $staging = (New-Item -ItemType Directory -Path (Join-Path $run 'player-incomplete')).FullName
    $unityLog = Join-Path $logs 'unity.log'
    $unityReceipt = Join-Path $logs 'unity-result.txt'
    # Build validation opens the gameplay scene, whose highway camera needs a graphics device.
    $unityArgs = @('-batchmode', '-quit', '-projectPath', $projectPath,
        '-buildTarget', 'Win64', '-executeMethod', 'YoloWindowsNightly.Build',
        '-yoloBuildOutput', $staging, '-yoloBuildReceipt', $unityReceipt, '-logFile', $unityLog)
    # Start-Process joins ArgumentList into a string on Windows, so preserve path boundaries.
    $unityCommandLine = ($unityArgs | ForEach-Object { '"' + $_ + '"' }) -join ' '
    Write-Host "Building Windows nightly. Unity log: $unityLog"
    $process = Start-Process -FilePath $unity -ArgumentList $unityCommandLine -PassThru
    Watch-UnityBuildLog -Process $process -LogPath $unityLog
    if ($process.ExitCode -ne 0) { throw "Unity exited with code $($process.ExitCode). See $unityLog" }
    if (!(Test-Path -LiteralPath $unityReceipt -PathType Leaf) -or
        (Get-Content -Raw -LiteralPath $unityReceipt).Trim() -ne 'StandaloneWindows64') {
        throw "Unity did not write a successful Windows build receipt. See $unityLog"
    }

    $data = Join-Path $staging 'YARG_Data'
    $addressables = Join-Path $data 'StreamingAssets/aa'
    foreach ($file in @((Join-Path $staging 'YARG.exe'), (Join-Path $addressables 'settings.json'),
            (Join-Path $addressables 'catalog.json'))) {
        if (!(Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) {
            throw "Build is missing a required file: $file"
        }
    }
    $bundles = @(Get-ChildItem -LiteralPath (Join-Path $addressables 'StandaloneWindows64') -Filter '*.bundle' -File -Recurse)
    if ($bundles.Count -eq 0 -or @($bundles | Where-Object Length -EQ 0).Count -gt 0) {
        throw 'The packaged Addressables bundles are missing or empty.'
    }

    $playerDlls = @(Get-ChildItem -LiteralPath (Join-Path $data 'Plugins') -Filter 'yarg_audio.dll' -File -Recurse)
    $sourceHash = (Get-FileHash -LiteralPath $sourceDll).Hash
    if ($playerDlls.Count -ne 1 -or (Get-FileHash -LiteralPath $playerDlls[0].FullName).Hash -ne $sourceHash) {
        throw 'The player does not contain the expected YargAudio DLL.'
    }

    Assert-NoLocalPaths -Player $staging -ProjectPath $projectPath
    $player = Join-Path $run 'player'
    Move-Item -LiteralPath $staging -Destination $player
    $shareArchive = Publish-WindowsPlayerZip -Player $player -OutputRoot $rootPath -RunName $runName
    $receipt['status'] = 'Succeeded'
    $receipt['unityVersion'] = $version
    $receipt['addressablesBundles'] = $bundles.Count
    $receipt['rebuiltAudio'] = !$UseBundledAudio
    $receipt['audioDllSha256'] = $sourceHash
    $receipt['localPathCheckPassed'] = $true
    $receipt['shareArchive'] = $shareArchive
    $receipt | ConvertTo-Json | Set-Content -LiteralPath $summaryPath -Encoding UTF8
    Write-Host "Windows nightly ready: $(Join-Path $player 'YARG.exe')"
    Write-Host "Windows share archive: $shareArchive"
}
catch {
    throw "Build run failed. Logs: $logs`n$($_.Exception.Message)"
}
finally {
    try {
        if ($compilerResponseCreated) {
            foreach ($path in @($compilerResponse, "$compilerResponse.meta")) {
                if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
            }
        }
        if ($entryCreated) {
            foreach ($path in @($entryPointPath, "$entryPointPath.meta")) {
                if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
            }
        }
        if (Test-Path -LiteralPath $audioBackup) {
            if ((Test-Path -LiteralPath $sourceDll) -and (Get-FileHash -LiteralPath $sourceDll).Hash -eq $installedAudioHash) {
                Copy-Item -LiteralPath $audioBackup -Destination $sourceDll -Force
            }
            else { Write-Warning "The audio DLL changed during the build. The original is in $audioBackup." }
        }
        if ((Test-Path -LiteralPath $settingsBackup) -and (Test-Path -LiteralPath $settings)) {
            $hashPattern = '(?m)^(  m_currentHash:\r?\n    serializedVersion: \d+\r?\n    Hash: )[0-9a-f]+'
            $before = (Get-Content -LiteralPath $settingsBackup -Raw) -replace $hashPattern, '$1'
            $after = (Get-Content -LiteralPath $settings -Raw) -replace $hashPattern, '$1'
            if ($before -ceq $after) { Copy-Item -LiteralPath $settingsBackup -Destination $settings -Force }
            else { Write-Warning "Addressables settings changed during the build. The original is in $settingsBackup." }
        }
    }
    finally {
        if ($null -ne $buildLock) { $buildLock.Dispose() }
        Stop-Transcript | Out-Null
    }
}
