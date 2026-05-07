param(
    [string]$SourcesPath  = "D:\GitRepos\Aurora Application\Sources\AuroraAbpPro",
    [string]$PkgPropsPath = "C:\Users\zhengkai\.aurora.abp.pro\Source\abpframework_abp_10.2.0\abpframework-abp-91e99c9\Directory.Packages.props"
)

function Get-RelPath([string]$fromDir, [string]$toFile) {
    $fromDir = [IO.Path]::GetFullPath($fromDir).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $toFile  = [IO.Path]::GetFullPath($toFile)
    $fromUri = [Uri]$fromDir
    $toUri   = [Uri]$toFile
    return [Uri]::UnescapeDataString($fromUri.MakeRelativeUri($toUri).ToString()).Replace('/', [IO.Path]::DirectorySeparatorChar)
}

# Build project index
$projectIndex = @{}
Get-ChildItem $SourcesPath -Filter "*.csproj" -Recurse | ForEach-Object {
    $n = [IO.Path]::GetFileNameWithoutExtension($_.FullName)
    if (-not $projectIndex.ContainsKey($n)) { $projectIndex[$n] = $_.FullName }
}
Write-Host "Project index: $($projectIndex.Count)"

# Build version map from Directory.Packages.props
$versionMap = @{}
if (Test-Path $PkgPropsPath) {
    [xml]$pkgProps = Get-Content $PkgPropsPath -Raw -Encoding UTF8
    foreach ($node in $pkgProps.SelectNodes("//*[local-name()='PackageVersion']")) {
        $include = $node.GetAttribute("Include")
        $update  = $node.GetAttribute("Update")
        $name    = if (-not [string]::IsNullOrWhiteSpace($include)) { $include } else { $update }
        $version = $node.GetAttribute("Version")
        if (-not [string]::IsNullOrWhiteSpace($name) -and -not [string]::IsNullOrWhiteSpace($version) -and -not $versionMap.ContainsKey($name)) {
            $versionMap[$name] = $version
        }
    }
}
Write-Host "Version map: $($versionMap.Count)"

$fixedFiles = 0
$fixedRefs  = 0
$addedVersions = 0
$notFound   = @()

Get-ChildItem $SourcesPath -Filter "*.csproj" -Recurse | ForEach-Object {
    $csprojPath = $_.FullName
    $csprojDir  = $_.DirectoryName

    try { [xml]$doc = Get-Content $csprojPath -Raw -Encoding UTF8 }
    catch { Write-Warning "Parse failed: $csprojPath"; return }

    $changed = $false

    foreach ($node in @($doc.SelectNodes("//PackageReference"))) {
        $pkgName = $node.GetAttribute("Include")
        if ([string]::IsNullOrWhiteSpace($pkgName)) { continue }

        $existingVer = $node.GetAttribute("Version")
        $hasVer      = -not [string]::IsNullOrWhiteSpace($existingVer)

        # Try to convert to ProjectReference if project exists in Sources
        if ($projectIndex.ContainsKey($pkgName)) {
            $target = $projectIndex[$pkgName]
            if ($target -ieq $csprojPath) { continue }
            $rel = Get-RelPath $csprojDir $target
            $newNode = $doc.CreateElement("ProjectReference", $node.NamespaceURI)
            $newNode.SetAttribute("Include", $rel)
            $node.ParentNode.ReplaceChild($newNode, $node) | Out-Null
            $changed = $true
            $fixedRefs++
            continue
        }

        # Add version if missing
        if (-not $hasVer) {
            if ($versionMap.ContainsKey($pkgName)) {
                $node.SetAttribute("Version", $versionMap[$pkgName])
                $changed = $true
                $addedVersions++
            }
            else {
                $notFound += "$([IO.Path]::GetFileName($csprojPath)) -> $pkgName"
            }
        }
    }

    if ($changed) {
        $doc.Save($csprojPath)
        $fixedFiles++
    }
}

Write-Host "Done: $fixedFiles files, $fixedRefs converted to ProjectRef, $addedVersions versions added"
if ($notFound.Count -gt 0) {
    Write-Host "Packages without version ($($notFound.Count)):"
    $notFound | Sort-Object | Get-Unique | ForEach-Object { Write-Host "  $PSItem" }
}
