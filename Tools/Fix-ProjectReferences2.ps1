param([string]$SourcesPath = "D:\GitRepos\Aurora Application\Sources\AuroraAbpPro")

$index = @{}
Get-ChildItem $SourcesPath -Filter "*.csproj" -Recurse | ForEach-Object {
    $n = [IO.Path]::GetFileNameWithoutExtension($_.FullName)
    if (-not $index.ContainsKey($n)) { $index[$n] = $_.FullName }
}
Write-Host "Index: $($index.Count) projects"

$fixedFiles = 0
$fixedRefs  = 0
$notFound   = @()

Get-ChildItem $SourcesPath -Filter "*.csproj" -Recurse | ForEach-Object {
    $csprojPath = $_.FullName
    $csprojDir  = $_.DirectoryName

    try { [xml]$doc = Get-Content $csprojPath -Raw -Encoding UTF8 }
    catch { Write-Warning "Parse failed: $csprojPath"; return }

    $changed = $false
    $nodes = $doc.SelectNodes("//ProjectReference")
    if ($null -eq $nodes -or $nodes.Count -eq 0) { return }

    foreach ($node in $nodes) {
        $inc = $node.GetAttribute("Include")
        if ([string]::IsNullOrWhiteSpace($inc)) { continue }

        $full = [IO.Path]::GetFullPath([IO.Path]::Combine($csprojDir, $inc))
        if (Test-Path $full) { continue }

        $refName = [IO.Path]::GetFileNameWithoutExtension($inc)
        if ($index.ContainsKey($refName)) {
            $target = $index[$refName]
            if ($target -ieq $csprojPath) { continue }
            $rel = [IO.Path]::GetRelativePath($csprojDir, $target).Replace('/', '\')
            $node.SetAttribute("Include", $rel)
            $changed = $true
            $fixedRefs++
            Write-Host "  Fixed: $([IO.Path]::GetFileName($csprojPath)) -> $refName -> $rel"
        }
        else {
            $notFound += "$([IO.Path]::GetFileName($csprojPath)) -> $refName"
        }
    }

    if ($changed) {
        $doc.Save($csprojPath)
        $fixedFiles++
    }
}

Write-Host "Done: $fixedFiles files, $fixedRefs refs fixed"
if ($notFound.Count -gt 0) {
    Write-Host "Not found ($($notFound.Count)):"
    foreach ($item in $notFound) { Write-Host "  $item" }
}
