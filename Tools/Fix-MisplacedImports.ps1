param(
    [string]$SourcesPath = "D:\GitRepos\Aurora Application\Sources\AuroraAbpPro"
)

$fixed = 0
$errors = @()

Get-ChildItem $SourcesPath -Filter "*.csproj" -Recurse | ForEach-Object {
    $path = $_.FullName

    try {
        [xml]$doc = [IO.File]::ReadAllText($path)
    }
    catch {
        $errors += $path
        return
    }

    $changed = $false

    # 找到所有在 ItemGroup 或其他非 Project 直接子元素内的 Import 节点，删除它们
    foreach ($importNode in @($doc.SelectNodes("//Import"))) {
        $parent = $importNode.ParentNode
        if ($parent.LocalName -ne "Project") {
            # Import 在非 Project 直接子节点中，属于错误位置，删除
            $parent.RemoveChild($importNode) | Out-Null
            $changed = $true
        }
    }

    if ($changed) {
        $doc.Save($path)
        $fixed++
        Write-Host "Fixed: $($_.Name)"
    }
}

Write-Host "Done: $fixed files fixed"
if ($errors.Count -gt 0) {
    Write-Host "Parse errors ($($errors.Count)):"
    $errors | ForEach-Object { Write-Host "  $_" }
}
