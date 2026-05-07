param([string]$SourcesPath = "D:\GitRepos\Aurora Application\Sources\AuroraAbpPro",
      [string]$SolutionRoot = "D:\GitRepos\Aurora Application")

$commonProps       = Join-Path $SolutionRoot "common.props"
$configureAwait    = Join-Path $SolutionRoot "configureawait.props"

$fixedFiles = 0; $fixedRefs = 0

Get-ChildItem $SourcesPath -Filter "*.csproj" -Recurse | ForEach-Object {
    $csprojPath = $_.FullName
    $csprojDir  = $_.DirectoryName

    try { [xml]$doc = Get-Content $csprojPath -Raw -Encoding UTF8 }
    catch { Write-Warning "Parse failed: $csprojPath"; return }

    $changed = $false
    foreach ($node in @($doc.SelectNodes("//Import"))) {
        $proj = $node.GetAttribute("Project")
        if ([string]::IsNullOrWhiteSpace($proj)) { continue }

        $targetFile = $null
        if ($proj -like "*common.props") { $targetFile = $commonProps }
        elseif ($proj -like "*configureawait.props") { $targetFile = $configureAwait }
        else { continue }

        # Resolve current path
        $currentFull = [IO.Path]::GetFullPath([IO.Path]::Combine($csprojDir, $proj))
        if ($currentFull -ieq $targetFile) { continue }  # already correct

        # Compute correct relative path
        $fromDir = $csprojDir.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        $fromUri = [Uri]$fromDir
        $toUri   = [Uri]$targetFile
        $rel     = [Uri]::UnescapeDataString($fromUri.MakeRelativeUri($toUri).ToString()).Replace('/', [IO.Path]::DirectorySeparatorChar)

        $node.SetAttribute("Project", $rel)
        $changed = $true; $fixedRefs++
        Write-Host "Fixed import in $([IO.Path]::GetFileName($csprojPath)): $proj -> $rel"
    }

    if ($changed) { $doc.Save($csprojPath); $fixedFiles++ }
}

Write-Host "Done: $fixedFiles files, $fixedRefs imports fixed"
