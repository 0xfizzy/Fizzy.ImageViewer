Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Release.Common.psm1"
function Get-CudaMatrix { Get-Content "$PSScriptRoot/../eng/cuda-matrix.json" -Raw | ConvertFrom-Json }
function Get-CudaVariant([int]$Major) {
    $variant = (Get-CudaMatrix).variants | Where-Object major -eq $Major
    if (!$variant) { throw "Unsupported CUDA major: $Major" }
    $variant
}
function Resolve-CudaArchitectures([int]$Major, [string]$Architectures, [switch]$FullMatrix) {
    if ($FullMatrix) {
        $expected = (Get-CudaVariant $Major).architectures
        if ($Architectures -and $Architectures -cne $expected) { throw 'Full matrix builds cannot override the official architectures.' }
        return $expected
    }
    if (!$Architectures) {
        $smi = Get-Command nvidia-smi -ErrorAction SilentlyContinue
        if (!$smi) { throw 'No identifiable GPU. Specify -Architectures or -FullMatrix.' }
        $capabilities = @(& $smi.Source --query-gpu=compute_cap --format=csv,noheader 2>&1)
        if ($LASTEXITCODE -or !$capabilities.Count -or @($capabilities | Where-Object { "$_".Trim() -notmatch '^\d+\.\d$' }).Count) {
            throw 'No identifiable GPU. Specify -Architectures or -FullMatrix.'
        }
        $Architectures = (@($capabilities | ForEach-Object { "$($_)".Trim().Replace('.','') + '-real' } | Sort-Object -Unique)) -join ';'
    }
    if ($Architectures -cnotmatch '^\d+(?:-real|-virtual)?(?:;\d+(?:-real|-virtual)?)*$') {
        throw 'Architectures must be numeric CMake targets separated by semicolons, optionally suffixed -real or -virtual.'
    }
    $Architectures
}
function Get-CudaArchitectureTargets([string]$Architectures) {
    $real = @(); $ptx = @()
    foreach ($target in $Architectures.Split(';')) {
        if ($target -notmatch '^(\d+)(-real|-virtual)?$') { throw "Invalid architecture: $target" }
        $number = $Matches[1]
        $suffix = if ($Matches.ContainsKey(2)) { $Matches[2] } else { '' }
        if ($suffix -ne '-virtual') { $real += $number }
        if ($suffix -ne '-real') { $ptx += $number }
    }
    @{real=@($real | Sort-Object -Unique);ptx=@($ptx | Sort-Object -Unique)}
}
function Test-CudaBinaryArchitectures([string]$Cuobjdump, [string]$Dll, [string]$Architectures, [string]$Output) {
    if (!(Test-Path $Cuobjdump)) { throw 'cuobjdump is required from the selected Toolkit; run Setup-Cuda.ps1 -DownloadToolkit.' }
    $elf = @(& $Cuobjdump --list-elf $Dll 2>&1)
    if ($LASTEXITCODE) { throw "cuobjdump --list-elf failed: $elf" }
    $ptx = @(& $Cuobjdump --dump-ptx $Dll 2>&1)
    if ($LASTEXITCODE) { throw "cuobjdump --dump-ptx failed: $ptx" }
    $elf | Set-Content "$Output/architectures.elf.txt"
    $ptx | Set-Content "$Output/architectures.ptx.txt"
    $expected = Get-CudaArchitectureTargets $Architectures
    $modules = @()
    foreach ($module in @('display','queries')) {
        $real = @([regex]::Matches(($elf -join "`n"), "(?m)^ELF file\s+\d+: $module\.sm_(\d+)\.cubin\s*$") | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        $virtual = @(foreach ($match in [regex]::Matches(($ptx -join "`n"), 'Fatbin ptx code:[\s\S]*?(?=Fatbin (?:elf|ptx) code:|\z)')) {
            $block = $match.Value
            if ($block -match "(?m)^identifier = .*[/\\]$module\.cu\s*$") {
                $target = [regex]::Match($block, '(?m)^\.target\s+sm_(\d+)\s*$')
                if (!$target.Success -or $block -notmatch '\.entry\s+') { throw "Missing executable PTX for $module." }
                $target.Groups[1].Value
            }
        })
        $virtual = @($virtual | Sort-Object -Unique)
        if (($real -join ';') -cne ($expected.real -join ';') -or ($virtual -join ';') -cne ($expected.ptx -join ';')) {
            throw "CUDA binary architecture mismatch for ${module}: real=$($real -join ';'), PTX=$($virtual -join ';'); requested $Architectures"
        }
        $modules += @{name=$module;real=$real;ptx=$virtual}
    }
    $inspection = @{nativeHash=(Get-Hash $Dll);architectures=$Architectures;modules=$modules;elfHash=(Get-Hash "$Output/architectures.elf.txt");ptxHash=(Get-Hash "$Output/architectures.ptx.txt")}
    Write-Json $inspection "$Output/architectures.json"
    $inspection
}
function Assert-CudaArchitectureInspection($Info, [string]$Directory) {
    if (!$Info.PSObject.Properties['architectureInspection']) { throw 'Missing CUDA binary architecture inspection; rebuild.' }
    $inspection = $Info.architectureInspection
    if ($inspection.nativeHash -cne $Info.nativeHash -or $inspection.architectures -cne $Info.architectures) { throw 'CUDA architecture inspection identity mismatch.' }
    foreach ($kind in @('elf','ptx')) {
        $path = "$Directory/architectures.$kind.txt"
        if (!(Test-Path $path) -or (Get-Hash $path) -cne $inspection."${kind}Hash") { throw 'CUDA architecture inspection checksum mismatch.' }
    }
    $expected = Get-CudaArchitectureTargets $Info.architectures
    if (($inspection.modules.name -join ';') -cne 'display;queries') { throw 'CUDA architecture inspection modules mismatch.' }
    foreach ($module in $inspection.modules) {
        if (($module.real -join ';') -cne ($expected.real -join ';') -or ($module.ptx -join ';') -cne ($expected.ptx -join ';')) { throw 'CUDA architecture inspection targets mismatch.' }
    }
}
function Get-CudaSourceIdentity {
    $repo = Get-Repo
    $files = @(& git -C $repo ls-files --cached --others --exclude-standard -- native/Cuda src/Fizzy.ImageViewer.Cuda eng/cuda-matrix.json scripts/Build-Cuda.ps1 scripts/Cuda.Common.psm1)
    if ($LASTEXITCODE) { throw 'Cannot enumerate CUDA sources.' }
    $entries = @($files | Sort-Object -Unique | ForEach-Object {
        if (Test-Path "$repo/$_") { "$_=$(Get-Hash "$repo/$_")" } else { "$_=deleted" }
    })
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($entries -join "`n")))).ToLowerInvariant()
}
function Get-CudaDownload($Asset, [string]$Directory) {
    [void][IO.Directory]::CreateDirectory($Directory)
    $file = Join-Path $Directory ($Asset.sha256 + '.zip')
    if (!(Test-Path $file) -or (Get-Hash $file) -cne $Asset.sha256) { [void](Get-RemoteFile $Asset.url $file) }
    if ((Get-Hash $file) -cne $Asset.sha256) { throw "Download checksum mismatch: $($Asset.url)" }
    $file
}
function Assert-CudaNative([int]$Major, [string]$Configuration, [string]$Directory, [switch]$Release) {
    $dll = Join-Path $Directory 'Fizzy.ImageViewer.Cuda.Native.dll'
    $record = Join-Path $Directory 'build.json'
    if (!(Test-Path $dll) -or !(Test-Path $record)) { throw 'CUDA native DLL and build record are required.' }
    $info = Get-Content $record -Raw | ConvertFrom-Json
    if ($info.schema -ne 1 -or $info.major -ne $Major -or $info.configuration -cne $Configuration) { throw 'CUDA native variant/configuration mismatch.' }
    if ($info.nativeHash -cne (Get-Hash $dll)) { throw 'CUDA native checksum mismatch.' }
    if ($info.commit -cne (Get-Commit) -or $info.sourceHash -cne (Get-CudaSourceIdentity)) { throw 'CUDA native source identity mismatch; rebuild the selected variant.' }
    Assert-CudaArchitectureInspection $info $Directory
    if ($Release) {
        Assert-ReleaseIdentity (Get-PackageVersion)
        $variant = Get-CudaVariant $Major
        $matrix = Get-CudaMatrix
        if ($info.dirty -or $info.nvcc -cne $variant.nvcc -or $info.toolkit -cne $variant.toolkit -or $info.architectures -cne $variant.architectures -or $info.msvc -notlike "$($matrix.msvc).*" -or $info.sdk -cne $matrix.sdk -or $info.cmake -cne $matrix.cmake.version -or $info.ninja -cne $matrix.ninja.version) { throw 'Release requires clean sources and the exact CUDA matrix baseline.' }
    }
    $info
}
Export-ModuleMember -Function *
