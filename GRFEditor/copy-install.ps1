Add-Type -AssemblyName System.Windows.Forms

$bin = Join-Path $PSScriptRoot 'bin\Debug'
$grfcl = Join-Path $PSScriptRoot '..\GrfCL\bin\Debug'
$list = New-Object System.Collections.Specialized.StringCollection

foreach ($name in @('GRF Editor.exe', 'GRF Editor.exe.config', 'Utilities.dll', 'GRF.dll', 'TokeiLibrary.dll', 'GrfToWpfBridge.dll')) {
	$path = Join-Path $bin $name
	if (Test-Path -LiteralPath $path) {
		$list.Add((Resolve-Path -LiteralPath $path).Path)
	}
}

foreach ($name in @('GrfCL.exe', 'GrfCL.exe.config')) {
	$path = Join-Path $grfcl $name
	if (Test-Path -LiteralPath $path) {
		$list.Add((Resolve-Path -LiteralPath $path).Path)
	}
}

if ($list.Count -eq 0) {
	Write-Host 'Nothing to copy. Build GRFEditor\bin\Debug first.'
	exit 1
}

$data = New-Object System.Windows.Forms.DataObject
$data.SetFileDropList($list)
# The second argument keeps the files after this process exits.
[System.Windows.Forms.Clipboard]::SetDataObject($data, $true)

Write-Host ($list.Count.ToString() + ' files copied. Paste into C:\Program Files (x86)\GRF Editor')
$list | ForEach-Object { Write-Host $_ }
