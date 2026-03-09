# StyleAudit.ps1
# Запускать из корня проекта: .\StyleAudit.ps1

$projectRoot = "."

$definitions = @{}
$usages = @{}
$fileMap = @{}

Get-ChildItem -Path $projectRoot -Recurse -Filter "*.axaml" | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    $file = $_.FullName.Replace($projectRoot, ".")

    # === ОПРЕДЕЛЕНИЯ ===

    # Style Selector="..."
    [regex]::Matches($content, '<Style\s+Selector="([^"]+)"') | ForEach-Object {
        $sel = $_.Groups[1].Value
        if (-not $definitions.ContainsKey($sel)) { $definitions[$sel] = @() }
        $definitions[$sel] += $file
    }

    # x:Key="..."
    [regex]::Matches($content, 'x:Key="([^"]+)"') | ForEach-Object {
        $key = $_.Groups[1].Value
        if (-not $definitions.ContainsKey($key)) { $definitions[$key] = @() }
        $definitions[$key] += $file
    }

    # === ИСПОЛЬЗОВАНИЯ ===

    # Classes="class1 class2"
    [regex]::Matches($content, 'Classes="([^"]+)"') | ForEach-Object {
        $_.Groups[1].Value -split '\s+' | ForEach-Object {
            $cls = $_.Trim()
            if ($cls -ne "") {
                if (-not $usages.ContainsKey($cls)) { $usages[$cls] = @() }
                $usages[$cls] += $file
            }
        }
    }

    # {DynamicResource XXX} и {StaticResource XXX}
    [regex]::Matches($content, '\{(?:Dynamic|Static)Resource\s+(\w+)\}') | ForEach-Object {
        $res = $_.Groups[1].Value
        if (-not $usages.ContainsKey($res)) { $usages[$res] = @() }
        $usages[$res] += $file
    }
}

# === ОТЧЁТ ===

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  STYLE AUDIT REPORT" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

# --- Ресурсы (x:Key) ---
Write-Host "`n--- РЕСУРСЫ (x:Key) ---" -ForegroundColor Yellow

$allKeys = $definitions.Keys | Where-Object { $_ -notmatch '^[A-Z]' -or $_ -notmatch '\.' }
$resources = $definitions.GetEnumerator() | Where-Object {
    $_.Key -notmatch '<|>|\s' -and $_.Key -notmatch '^\.'
} | Sort-Object Key

$unusedResources = @()
$usedResources = @()

foreach ($r in $resources) {
    $key = $r.Key
    # Пропускаем Style Selector'ы
    if ($key -match '[:\.#/\s\|>]') { continue }

    if ($usages.ContainsKey($key)) {
        $count = ($usages[$key] | Select-Object -Unique).Count
        $usedResources += [PSCustomObject]@{
            Key = $key
            DefinedIn = ($r.Value | Select-Object -Unique) -join ", "
            UsedIn = $count
            Files = ($usages[$key] | Select-Object -Unique) -join ", "
        }
    } else {
        $unusedResources += [PSCustomObject]@{
            Key = $key
            DefinedIn = ($r.Value | Select-Object -Unique) -join ", "
        }
    }
}

Write-Host "`n  ИСПОЛЬЗУЕМЫЕ РЕСУРСЫ: $($usedResources.Count)" -ForegroundColor Green
foreach ($r in $usedResources) {
    Write-Host "  ✅ $($r.Key)" -ForegroundColor Green -NoNewline
    Write-Host " (в $($r.UsedIn) файлах)" -ForegroundColor Gray
}

Write-Host "`n  НЕИСПОЛЬЗУЕМЫЕ РЕСУРСЫ: $($unusedResources.Count)" -ForegroundColor Red
foreach ($r in $unusedResources) {
    Write-Host "  ❌ $($r.Key)" -ForegroundColor Red -NoNewline
    Write-Host " (определён: $($r.DefinedIn))" -ForegroundColor Gray
}

# --- Style Classes ---
Write-Host "`n--- STYLE CLASSES ---" -ForegroundColor Yellow

$styleClasses = $definitions.GetEnumerator() | Where-Object {
    $_.Key -match '\.'
} | Sort-Object Key

$unusedStyles = @()
$usedStyles = @()

foreach ($s in $styleClasses) {
    $selector = $s.Key
    # Извлекаем имена классов из селектора
    $classes = [regex]::Matches($selector, '\.(\w+)') | ForEach-Object { $_.Groups[1].Value }

    $found = $false
    foreach ($cls in $classes) {
        if ($usages.ContainsKey($cls)) {
            $found = $true
            break
        }
    }

    if ($found) {
        $usedStyles += $selector
    } else {
        $unusedStyles += $selector
    }
}

Write-Host "`n  ИСПОЛЬЗУЕМЫЕ СТИЛИ: $($usedStyles.Count)" -ForegroundColor Green
foreach ($s in $usedStyles) {
    Write-Host "  ✅ $s" -ForegroundColor Green
}

Write-Host "`n  ВОЗМОЖНО НЕИСПОЛЬЗУЕМЫЕ: $($unusedStyles.Count)" -ForegroundColor Red
foreach ($s in $unusedStyles) {
    Write-Host "  ❌ $s" -ForegroundColor Red
}

# --- Дубликаты ---
Write-Host "`n--- ДУБЛИКАТЫ ОПРЕДЕЛЕНИЙ ---" -ForegroundColor Yellow

$duplicates = $definitions.GetEnumerator() | Where-Object {
    ($_.Value | Select-Object -Unique).Count -gt 1 -or $_.Value.Count -gt 1
}

if ($duplicates) {
    foreach ($d in $duplicates) {
        $files = ($d.Value | Select-Object -Unique) -join ", "
        $count = $d.Value.Count
        if ($count -gt 1) {
            Write-Host "  ⚠️  $($d.Key)" -ForegroundColor DarkYellow -NoNewline
            Write-Host " ($count определений: $files)" -ForegroundColor Gray
        }
    }
} else {
    Write-Host "  Дубликатов не найдено" -ForegroundColor Green
}

# --- Итого ---
Write-Host "`n============================================" -ForegroundColor Cyan
Write-Host "  ИТОГО:" -ForegroundColor Cyan
Write-Host "    Ресурсов определено: $($usedResources.Count + $unusedResources.Count)"
Write-Host "    Ресурсов используется: $($usedResources.Count)" -ForegroundColor Green
Write-Host "    Ресурсов НЕ используется: $($unusedResources.Count)" -ForegroundColor Red
Write-Host "    Стилей определено: $($usedStyles.Count + $unusedStyles.Count)"
Write-Host "    Стилей используется: $($usedStyles.Count)" -ForegroundColor Green
Write-Host "    Стилей возможно НЕ используется: $($unusedStyles.Count)" -ForegroundColor Red
Write-Host "============================================" -ForegroundColor Cyan