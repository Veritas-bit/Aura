# Audit all project XAML StaticResource references before compiling.
# WPF compiles even when a resource key will be missing at runtime.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$xamlRoot = Join-Path $root 'DynamicIsland'
$files = @(Get-ChildItem -Path $xamlRoot -Filter '*.xaml' -Recurse -File |
    Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]' })
$keys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$resources = @()

foreach ($file in $files) {
    $xml = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($definition in [regex]::Matches($xml, 'x:Key\s*=\s*["'']([^"'']+)["'']')) {
        [void]$keys.Add($definition.Groups[1].Value)
    }
    $resources += [pscustomobject]@{ Path = $file.FullName; Text = $xml }
}

$missing = @()
$count = 0
foreach ($file in $resources) {
    foreach ($reference in [regex]::Matches($file.Text, '\{StaticResource\s+([^\s}]+)')) {
        $key = $reference.Groups[1].Value
        $count++
        if (-not $keys.Contains($key)) {
            $relative = [System.IO.Path]::GetRelativePath($root, $file.Path)
            $missing += "$relative : {$key}"
        }
    }
}

Write-Host "Checked $count StaticResource references, $($keys.Count) keys in $($files.Count) XAML files."
if ($missing.Count -gt 0) {
    foreach ($item in $missing) { Write-Error "Undefined XAML resource: $item" }
    throw "$($missing.Count) missing StaticResource definition(s)."
}
Write-Host "All project StaticResource references resolve." -ForegroundColor Green
