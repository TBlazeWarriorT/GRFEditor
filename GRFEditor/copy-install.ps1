Add-Type -AssemblyName System.Windows.Forms

# Whichever of bin\Debug and bin\Release was built last, judged by its exe
function Get-NewestBuild($project, $exe) {
	$builds = @('Debug', 'Release') | ForEach-Object { Get-Item -LiteralPath (Join-Path $project "bin\$_\$exe") -ErrorAction SilentlyContinue }
	$newest = $builds | Sort-Object LastWriteTime -Descending | Select-Object -First 1
	if ($newest) {
		Write-Host ("$exe from " + $newest.Directory.Name + ' (' + $newest.LastWriteTime + ')')
		return $newest.DirectoryName
	}
	return Join-Path $project 'bin\Debug'
}

$bin = Get-NewestBuild $PSScriptRoot 'GRF Editor.exe'
$grfcl = Get-NewestBuild (Join-Path $PSScriptRoot '..\GrfCL') 'GrfCL.exe'
$list = New-Object System.Collections.Specialized.StringCollection

foreach ($name in @('GRF Editor.exe', 'GRF Editor.exe.config', 'Utilities.dll', 'GRF.dll', 'TokeiLibrary.dll', 'GrfToWpfBridge.dll')) {
	$path = Join-Path $bin $name
	if (Test-Path -LiteralPath $path) {
		[void]$list.Add((Resolve-Path -LiteralPath $path).Path)
	}
}

foreach ($name in @('GrfCL.exe', 'GrfCL.exe.config')) {
	$path = Join-Path $grfcl $name
	if (Test-Path -LiteralPath $path) {
		[void]$list.Add((Resolve-Path -LiteralPath $path).Path)
	}
}

if ($list.Count -eq 0) {
	Write-Host 'Nothing to copy. Build GRFEditor first (Debug or Release).'
	exit 1
}

$data = New-Object System.Windows.Forms.DataObject
$data.SetFileDropList($list)
# The second argument keeps the files after this process exits.
[System.Windows.Forms.Clipboard]::SetDataObject($data, $true)

Write-Host ($list.Count.ToString() + ' files copied. Paste into C:\Program Files (x86)\GRF Editor')
$list | ForEach-Object { Write-Host $_ }
