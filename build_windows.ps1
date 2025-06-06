#!/usr/bin/pwsh


dotnet restore TownSuite.CodeSigning.Service.sln
dotnet build -c Release TownSuite.CodeSigning.Service.sln --no-restore

dotnet publish TownSuite.CodeSigning.Client -c Release -r win-x64 -p:PublishReadyToRun=true --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
