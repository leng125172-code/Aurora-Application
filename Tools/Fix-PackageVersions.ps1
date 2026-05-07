param(
    [string]$SourcesPath  = "D:\GitRepos\Aurora Application\Sources\AuroraAbpPro",
    [string]$PkgPropsPath = "C:\Users\zhengkai\.aurora.abp.pro\Source\abpframework_abp_10.2.0\abpframework-abp-91e99c9\Directory.Packages.props"
)

# Load version map from Directory.Packages.props
[xml]$pkgProps = Get-Content $PkgPropsPath -Raw -Encoding UTF8
$versionMap = @{}
foreach ($node in $pkgProps.SelectNodes("//PackageVersion")) {
    $name    = $node.GetAttribute("Include") -or $node.GetAttribute("Update")
    $version = $node.GetAttribute("Version")
    if ($name -and $version -and -not $versionMap.ContainsKey($name)) {
        $versionMap[$name] = $version
    }
}
# also check Update attribute
foreach ($node in $pkgProps.SelectNodes("//PackageVersion[@Update]")) {
    $name    = $node.GetAttribute("Update")
    $version = $node.GetAttribute("Version")
    if ($name -and $version -and -not $versionMap.ContainsKey($name)) {
        $versionMap[$name] = $version
    }
}
Write-Host "Version map: $($versionMap.Count) entries"

$fixedFiles = 0
$fixedRefs  = 0
$notFound   = @()

Get-ChildItem $SourcesPath -Filter "*.csproj" -Recurse | ForEach-Object {
    $csprojPath = $_.FullName

    try { [xml]$doc = Get-Content $csprojPath -Raw -Encoding UTF8 }
    catch { Write-Warning "Parse failed: $csprojPath"; return }

    $changed = $false
    foreach ($node in @($doc.SelectNodes("//PackageReference"))) {
        # Skip if already has Version attribute or child element
        $existingVer = $node.GetAttribute("Version")
        if (-not [string]::IsNullOrWhiteSpace($existingVer)) { continue }
        $childVer = $node.SelectSingleNode("Version")
        if ($null -ne $childVer) { continue }

        $pkgName = $node.GetAttribute("Include")
        if ([string]::IsNullOrWhiteSpace($pkgName)) { continue }

        if ($versionMap.ContainsKey($pkgName)) {
            $node.SetAttribute("Version", $versionMap[$pkgName])
            $changed = $true
            $fixedRefs++
        }
        else {
            $notFound += "$([IO.Path]::GetFileName($csprojPath)) -> $pkgName"
        }
    }

    if ($changed) {
        $doc.Save($csprojPath)
        $fixedFiles++
    }
}

Write-Host "Done: $fixedFiles files, $fixedRefs refs fixed"
if ($notFound.Count -gt 0) {
    Write-Host "Not in version map ($($notFound.Count)):"
    foreach ($item in $notFound) { Write-Host "  $item" }
}
