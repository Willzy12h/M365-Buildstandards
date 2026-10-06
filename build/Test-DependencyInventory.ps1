[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageRoot, [switch]$TestRejections)
$ErrorActionPreference = 'Stop'
$deps = Get-Content -LiteralPath (Join-Path $PackageRoot 'app/BDIT.TenantToolkit.App.deps.json') -Raw | ConvertFrom-Json
# Windows PowerShell 5.1 returns a JSON array as one pipeline object; @() would wrap it in another array.
# Assignment preserves its elements on 5.1 and collects the enumerated output on PowerShell 7.
$entries = (Get-Content -LiteralPath (Join-Path $PackageRoot 'DEPENDENCIES.json') -Raw | ConvertFrom-Json)
$version = Get-Content -LiteralPath (Join-Path $PackageRoot 'VERSION.json') -Raw | ConvertFrom-Json
$versionEntries = @($version.nugetPackages) + @($version.runtimePacks)
$expected = @($deps.libraries.PSObject.Properties | Where-Object { $_.Value.type -in @('package', 'runtimepack') })

function Assert-Records($records) {
    if ($records.Count -ne $expected.Count -or $records.Count -eq 0) { throw "Dependency record count differs from publish metadata (actual $($records.Count), expected $($expected.Count))." }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($record in $records) {
        $match = @($expected | Where-Object { $_.Name -ceq $record.resolvedIdentity })
        if ($match.Count -ne 1 -or -not $seen.Add($record.resolvedIdentity)) { throw 'Dependency identity is unknown or duplicated.' }
        $parts = $match[0].Name -split '/', 2
        $name = $parts[0] -replace '^runtimepack\.', ''
        if ($record.name -cne $name -or $record.version -cne $parts[1] -or $record.kind -cne $match[0].Value.type) { throw 'Dependency name, version or kind differs from publish metadata.' }
        foreach ($notice in @($record.notices)) {
            $prefix = 'licenses/' + $name + '-' + $parts[1] + '/'
            if (-not $notice.StartsWith($prefix, [StringComparison]::Ordinal) -or $notice.Substring($prefix.Length) -match '[\\/:]|^\.{1,2}$') { throw 'Unsafe dependency notice path.' }
            if (-not (Test-Path -LiteralPath (Join-Path $PackageRoot $notice) -PathType Leaf)) { throw 'Declared dependency notice is missing.' }
        }
    }
}
Assert-Records $entries
Assert-Records $versionEntries
foreach ($entry in $entries) {
    $record = @($versionEntries | Where-Object { $_.resolvedIdentity -ceq $entry.resolvedIdentity })[0]
    if ($record.license -cne $entry.license -or (@($record.notices | Sort-Object) -join "`n") -cne (@($entry.notices | Sort-Object) -join "`n")) { throw 'Dependency notices/licence differ between release inventories.' }
}

if ($TestRejections) {
    foreach ($field in @('name', 'version', 'kind')) {
        $modified = ($entries | ConvertTo-Json -Depth 6 | ConvertFrom-Json)
        $modified[0].$field = 'synthetic-invalid'
        $refused = $false
        try { Assert-Records $modified } catch { $refused = $true }
        if (-not $refused) { throw "Dependency verification did not refuse changed $field." }
    }
    $modified = ($entries | ConvertTo-Json -Depth 6 | ConvertFrom-Json)
    $withNotice = @($modified | Where-Object { @($_.notices).Count -gt 0 })[0]
    if ($null -eq $withNotice) { throw 'Runtime package must supply at least one licence notice.' }
    $withNotice.notices = @('licenses/' + $withNotice.name + '-' + $withNotice.version + '/synthetic-missing-notice.txt')
    $refused = $false
    try { Assert-Records $modified } catch { $refused = $true }
    if (-not $refused) { throw 'Dependency verification did not refuse a missing notice.' }
}
