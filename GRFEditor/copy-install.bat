@echo off
rem Puts the install files on the clipboard. Paste into C:\Program Files (x86)\GRF Editor
set "BIN=%~dp0bin\Debug\"
set "GRFCL=%~dp0..\GrfCL\bin\Debug\"
powershell -STA -NoProfile -ExecutionPolicy Bypass -Command "$bin='%BIN%'; $grfcl='%GRFCL%'; Add-Type -AssemblyName System.Windows.Forms; $c=New-Object System.Collections.Specialized.StringCollection; foreach($n in @('GRF Editor.exe','GRF Editor.exe.config','Utilities.dll','GRF.dll','TokeiLibrary.dll','GrfToWpfBridge.dll')){ $p=Join-Path $bin $n; if(Test-Path -LiteralPath $p){ $c.Add($p) } }; foreach($n in @('GrfCL.exe','GrfCL.exe.config')){ $p=Join-Path $grfcl $n; if(Test-Path -LiteralPath $p){ $c.Add((Resolve-Path -LiteralPath $p).Path) } }; if($c.Count -eq 0){ Write-Host 'Nothing to copy. Build GRFEditor\bin\Debug first.'; exit 1 }; [System.Windows.Forms.Clipboard]::SetFileDropList($c); Write-Host ($c.Count.ToString()+' files copied. Paste into C:\Program Files (x86)\GRF Editor')"
pause
