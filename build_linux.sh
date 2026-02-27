#!/bin/sh
$CURRENTPATH=$(pwd)

VERSION=$(cat Directory.Build.props | grep "<Version>" | sed 's/[^0-9.]*//g')

dotnet restore TownSuite.CodeSigning.Service.sln
dotnet build -c Release TownSuite.CodeSigning.Service.sln
dotnet publish TownSuite.CodeSigning.Client -c Release -r linux-x64 -p:PublishReadyToRun=true --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true --output $CURRENTPATH/build/linux-x64/TownSuite.CodeSigning.Client
dotnet publish TownSuite.CodeSigning.Service -c Release -r linux-x64 --output $CURRENTPATH/build/linux-x64/TownSuite.CodeSigning.Service

rm -rf /build/linux
mkdir -p build/linux/opt/townsuite/codesigning/client
cp -r $CURRENTPATH/build/linux-x64/TownSuite.CodeSigning.Client/* build/linux/opt/townsuite/codesigning/client/
mkdir -p build/linux/usr/bin
cp -r townsuite-code-signing-client build/linux/usr/bin/townsuite-code-signing-client
chmod +x build/linux/usr/bin/townsuite-code-signing-client


sudo gem install fpm --no-document

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

# zip the linux-x64 folder
cd $CURRENTPATH/build
zip -r TownSuite.CodeSigning.Client-$VERSION-linux-x64.zip linux-x64/TownSuite.CodeSigning.Client
zip -r TownSuite.CodeSigning.Client-$VERSION-linux-x64-deb.zip *.deb

# create *.SHA256SUMS per file
sha256sum TownSuite.CodeSigning.Client-$VERSION-linux-x64.zip > TownSuite.CodeSigning.Client-$VERSION-linux-x64.zip.SHA256SUMS
sha256sum TownSuite.CodeSigning.Client-$VERSION-linux-x64-deb.zip > TownSuite.CodeSigning.Client-$VERSION-linux-x64-deb.zip.SHA256SUMS

cd $CURRENTPATH
