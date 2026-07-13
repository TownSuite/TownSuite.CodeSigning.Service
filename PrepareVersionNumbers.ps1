#!/usr/bin/env pwsh
$ErrorActionPreference = "Stop"
$CURRENTPATH=$pwd.Path


function RunVerionUpdater($loc, $path){
	If ($IsWindows) {
	    & "./tools/AssemblyInfoUtil.exe" -inc:$loc "$path"
	}
	else {
		assemblyinfoutil -inc:$loc "$path"
	}
}

RunVerionUpdater 3 "$CURRENTPATH/Directory.Build.props"
