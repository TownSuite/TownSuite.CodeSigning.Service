#!/usr/bin/pwsh
$ErrorActionPreference = "Stop"
$CURRENTPATH = $pwd.Path

function GetVersions([ref]$theVersion, [ref]$path) {	

	# Read the content of the file and find the line containing <Version>
	$versionLine = Get-Content -Path $path.Value | Select-String -Pattern "<Version>"

	# Extract the version number using a regular expression
	if ($versionLine -match "<Version>([0-9.]+)</Version>") {
		$VERSION = $matches[1]
	} else {
		Write-Error "Version tag not found in the file."
	}

    $theVersion.Value = $VERSION
}

# delete the old build output if it exists
if (Test-Path "$CURRENTPATH/build") {
    Write-Host "Deleting old build output..." -ForegroundColor Green
    Remove-Item -Recurse -Force "$CURRENTPATH/build"
    New-Item -ItemType Directory -Path "$CURRENTPATH/build" | Out-Null
}

dotnet restore TownSuite.CodeSigning.Service.sln
dotnet build -c Release TownSuite.CodeSigning.Service.sln

dotnet publish TownSuite.CodeSigning.Client -c Release -r win-x64 -p:PublishReadyToRun=true --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true --output "$CURRENTPATH\build\win-x64\TownSuite.CodeSigning.Client"
dotnet publish TownSuite.CodeSigning.Service -c Release -r win-x64 --output "$CURRENTPATH\build\win-x64\TownSuite.CodeSigning.Service"

$version = ""
GetVersions ([ref] $version)  ([ref]"$CURRENTPATH\Directory.Build.props")

$GITHASH = git rev-parse --short HEAD
$GITHASH_FULL = git rev-parse HEAD
Add-Content "$CURRENTPATH/build/parameterproperties.txt" "VERSION=$version"
Add-Content "$CURRENTPATH/build/parameterproperties.txt" "GITHASH=$GITHASH"
Add-Content "$CURRENTPATH/build/parameterproperties.txt" "GITHASH_FULL=$GITHASH_FULL"
