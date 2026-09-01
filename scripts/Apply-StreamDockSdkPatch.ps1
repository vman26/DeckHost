param(
	[string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
	[switch]$CheckOnly
)

$submodulePath = Join-Path $RepositoryRoot 'native/StreamDock-SDK'
$patchPath = Join-Path $RepositoryRoot 'native/patches/streamdock-sdk.patch'

if (-not (Test-Path $submodulePath)) {
	throw "StreamDock SDK submodule not found: $submodulePath"
}

if (-not (Test-Path $patchPath)) {
	throw "Patch file not found: $patchPath"
}

Push-Location $submodulePath
try {
	& git apply --check --ignore-space-change --ignore-whitespace $patchPath 2>$null
	$canApply = $LASTEXITCODE -eq 0

	& git apply --reverse --check --ignore-space-change --ignore-whitespace $patchPath 2>$null
	$alreadyApplied = $LASTEXITCODE -eq 0

	if ($CheckOnly) {
		if ($canApply) {
			Write-Host 'Patch can be applied cleanly.'
		}
		elseif ($alreadyApplied) {
			Write-Host 'Patch is already applied.'
		}
		else {
			throw 'Patch cannot be applied cleanly and is not already applied.'
		}
		return
	}

	if ($alreadyApplied) {
		Write-Host 'Patch is already applied.'
		return
	}

	if (-not $canApply) {
		throw 'Patch cannot be applied cleanly.'
	}

	& git apply --apply --ignore-space-change --ignore-whitespace $patchPath
	if ($LASTEXITCODE -ne 0) {
		throw "git apply failed with exit code $LASTEXITCODE"
	}
}
finally {
	Pop-Location
}

Write-Host 'Patch applied successfully.'
