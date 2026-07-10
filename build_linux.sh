#!/bin/sh

VERSION=$(cat Directory.Build.props | grep "<Version>" | sed 's/[^0-9.]*//g')

dotnet restore TownSuite.CodeSigning.Service.sln
dotnet build -c Release TownSuite.CodeSigning.Service.sln

dotnet publish TownSuite.CodeSigning.Client -c Release -r linux-x64 -p:PublishReadyToRun=true --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true --output ./build/linux-x64/TownSuite.CodeSigning.Client
dotnet publish TownSuite.CodeSigning.Service -c Release -r linux-x64 --output ./build/linux-x64/TownSuite.CodeSigning.Service

dotnet publish TownSuite.CodeSigning.Client -c Release -r linux-arm64 -p:PublishReadyToRun=true --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true --output ./build/linux-arm64/TownSuite.CodeSigning.Client
dotnet publish TownSuite.CodeSigning.Service -c Release -r linux-arm64 --output ./build/linux-arm64/TownSuite.CodeSigning.Service

sudo gem install fpm --no-document

rm -rf ./build/pkg-linux-amd64
mkdir -p build/pkg-linux-amd64/opt/townsuite/codesigning/client
cp -r ./build/linux-x64/TownSuite.CodeSigning.Client/* build/pkg-linux-amd64/opt/townsuite/codesigning/client/
mkdir -p build/pkg-linux-amd64/usr/bin
cp townsuite-code-signing-client build/pkg-linux-amd64/usr/bin/townsuite-code-signing-client
chmod +x build/pkg-linux-amd64/usr/bin/townsuite-code-signing-client

cd build/pkg-linux-amd64
fpm -s dir -t deb \
  --name townsuite-codesigning-client \
  --version $VERSION \
  --description "TownSuite Code Signing Client" \
  --maintainer "Peter Gill <peter.gill@townsuite.com>" \
  --license "MIT" \
  --architecture amd64 \
  --deb-no-default-config-files \
  --url "https://github.com/TownSuite/TownSuite.CodeSigning.Service" \
  ./
cd ../..

rm -rf ./build/pkg-linux-arm64
mkdir -p build/pkg-linux-arm64/opt/townsuite/codesigning/client
cp -r ./build/linux-arm64/TownSuite.CodeSigning.Client/* build/pkg-linux-arm64/opt/townsuite/codesigning/client/
mkdir -p build/pkg-linux-arm64/usr/bin
cp townsuite-code-signing-client build/pkg-linux-arm64/usr/bin/townsuite-code-signing-client
chmod +x build/pkg-linux-arm64/usr/bin/townsuite-code-signing-client

cd build/pkg-linux-arm64
fpm -s dir -t deb \
  --name townsuite-codesigning-client \
  --version $VERSION \
  --description "TownSuite Code Signing Client" \
  --maintainer "Peter Gill <peter.gill@townsuite.com>" \
  --license "MIT" \
  --architecture arm64 \
  --deb-no-default-config-files \
  --url "https://github.com/TownSuite/TownSuite.CodeSigning.Service" \
  ./
cd ../..

cd build
zip -r TownSuite.CodeSigning.Client-$VERSION-linux-x64.zip linux-x64/TownSuite.CodeSigning.Client
zip -r TownSuite.CodeSigning.Service-$VERSION-linux-x64.zip linux-x64/TownSuite.CodeSigning.Service
zip -r TownSuite.CodeSigning.Client-$VERSION-linux-arm64.zip linux-arm64/TownSuite.CodeSigning.Client
zip -r TownSuite.CodeSigning.Service-$VERSION-linux-arm64.zip linux-arm64/TownSuite.CodeSigning.Service

sha256sum TownSuite.CodeSigning.Client-$VERSION-linux-x64.zip > TownSuite.CodeSigning.Client-$VERSION-linux-x64.zip.SHA256SUMS
sha256sum TownSuite.CodeSigning.Service-$VERSION-linux-x64.zip > TownSuite.CodeSigning.Service-$VERSION-linux-x64.zip.SHA256SUMS
sha256sum TownSuite.CodeSigning.Client-$VERSION-linux-arm64.zip > TownSuite.CodeSigning.Client-$VERSION-linux-arm64.zip.SHA256SUMS
sha256sum TownSuite.CodeSigning.Service-$VERSION-linux-arm64.zip > TownSuite.CodeSigning.Service-$VERSION-linux-arm64.zip.SHA256SUMS

cd ..
