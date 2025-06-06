#!/bin/sh

VERSION=$(cat Directory.Build.props | grep "<Version>" | sed 's/[^0-9.]*//g')

dotnet restore TownSuite.CodeSigning.Service.sln
dotnet build -c Release TownSuite.CodeSigning.Service.sln --no-restore
dotnet publish TownSuite.CodeSigning.Client -c Release -r linux-x64 -p:PublishReadyToRun=true --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true

rm -rf /build/linux
mkdir -p build/linux/opt/townsuite/codesigning/client
cp -r TownSuite.CodeSigning.Client/bin/Release/net8.0/linux-x64/publish/* build/linux/opt/townsuite/codesigning/client/
mkdir -p build/linux/usr/bin
cp -r townsuite-code-signing-client build/linux/usr/bin/townsuite-code-signing-client
chmod +x build/linux/usr/bin/townsuite-code-signing-client


gem install fpm 

cd build/linux
# build a deb package using fpm
fpm -s dir -t deb \
  --name townsuite-codesigning-client \
  --version $VERSION \
  --description "TownSuite Code Signing Client" \
  --maintainer "TownSuite" \
  --license "MIT" \
  --architecture all \
  --deb-no-default-config-files \
  --url "https://github.com/TownSuite/TownSuite.CodeSigning.Service" \
  --maintainer "Peter Gill <peter.gill@townsuite.com>" \
  ./

  cd ../../