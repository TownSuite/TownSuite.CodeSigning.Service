# Run the service

Open appsettings.json set the path to the windows sdk signtool.exe and modify the signtool options.

Open powershell and run:

```powershelll
$env:ASPNETCORE_URLS="http://+:5000"; .\TownSuite.CodeSigning.Service.exe
```

# curl example

```bash
curl --location 'https://localhost:7153/sign' \
--header 'Content-Type: application/x-msdownload' \
--data '@/C:/the/file/to/upload/and/sign.dll' \
-o output-signed-file.dll
```

# TownSuite.CodeSigning.Client examples

## Single folder

Sign all matching files in one folder using `-folder` and `-file`.

```bash
./TownSuite.CodeSigning.Client -folder "/path/to/folder/with/assemblies" -file "*.dll;*.exe" -timeout 30000 -url "https://localhost:5000" -token "the token"
```

## Multiple folders with per-folder file lists

Each `-folders` entry pairs a folder path with its own file patterns separated by `|`. Files within are `;` separated. Can be specified multiple times.

Duplicate files across folders are detected by SHA-256 hash and only signed once. After signing, the signed copy is distributed to all duplicate locations.

```powershell
.\TownSuite.CodeSigning.Client.exe -folders "C:\publish\win-x64|*.dll;*.exe" -folders "C:\publish\linux-x64|mylib.dll;*.so" -timeout 30000 -url "https://localhost:5000" -token "the token"
```

## Recursive folder scan

Use `-rfolder` to recursively scan a parent folder and all its subdirectories for matching files. Same `folder|patterns` syntax as `-folders`. Can be specified multiple times.

This is useful in CI where multiple publish outputs (e.g. `win-x64/`, `linux-x64/`, `win-arm64/`) live under a common parent directory and may contain duplicate files.

```powershell
.\TownSuite.CodeSigning.Client.exe -rfolder "C:\publish|*.dll;*.exe" -timeout 30000 -url "https://localhost:5000" -token "the token"
```

Multiple recursive roots with different patterns:

```powershell
.\TownSuite.CodeSigning.Client.exe -rfolder "C:\publish|*.dll;*.exe" -rfolder "C:\other|mylib.dll" -timeout 30000 -url "https://localhost:5000" -token "the token"
```

## Combining options

All folder modes can be combined. Files from every source are merged, deduplicated by SHA-256, signed once, and the signed copy is copied back to all duplicate locations.

```powershell
.\TownSuite.CodeSigning.Client.exe -file "extra.dll" -folder "C:\extras" -folders "C:\publish\win-x64|*.dll;*.exe" -rfolder "C:\publish\shared|*.dll" -timeout 30000 -url "https://localhost:5000" -token "the token"
```


## Detached signing (zip/update package)

You can create a detached PKCS#7 (.sig) for an update package (zip). This is useful when your build server produces an update .zip that should be signed separately from the individual exe/dll code signing.

Client usage example (opt-in detached signing):

```powershell
.\TownSuite.CodeSigning.Client.exe -file "update-package.zip" -timeout 30000 -url "https://your-codesign-server:5000" -token "the token" -detached
```

The client will upload the zip as a batch and poll for result. When detached signing is requested the client will save the returned signature next to the original file as `update-package.zip.sig` instead of overwriting the zip.

Curl example (upload zip directly and save signature):

**Step 1**: submit batch signing request (returns an ID in the response)
```bash
curl --location 'https://localhost:7153/sign/batch' \
  --header 'Content-Type: application/zip' \
  --header 'X-Detached: true' \
  --header 'X-BatchId: PLACEHOLDER' \
  --header 'X-BatchReady: true' \
  --data-binary '@./update-package.zip'
```

**Step 2**: poll for the completed signature using the returned ID
# Replace <id-from-step-1> with the actual ID returned by Step 1

```bash
curl --location "https://localhost:7153/sign/batch?id=<id-returned-from-step-1>" \
  --header 'Content-Type: application/zip' \
  --header 'X-Detached: true' \
  --header 'X-BatchId: PLACEHOLDER'
```

Server behavior:
- The service parses your existing `SignToolOptions` to locate the certificate (sha1/subject or referenced PFX). If the configured signer uses a hardware token / CSP that is registered in the certificate store, the detached signer will use that key (no separate PFX required).
- The detached signature produced is a PKCS#7 detached signature suitable for verification by standard tools.

Recursive example (sign all zips under a parent folder)

```powershell
.\TownSuite.CodeSigning.Client.exe -rfolder "C:\builds|*.zip" -timeout 30000 -url "https://your-codesign-server:5000" -token "the token" -detached
```

Notes:
- `-rfolder "C:\builds|*.zip"` will recursively scan `C:\builds` and all subfolders for files matching `*.zip` and upload them for detached signing.
- The client will save a signature file next to each zip as `your-package.zip.sig` (it will not overwrite the original zip).
- Duplicate zip content across multiple folders is deduplicated by the client; signatures are created once and copied to duplicates.


# Windows Defender Exclusion

For increased performance add the services working directory to the windows defender exclusion path.

**Important Note:**
- **Security Risks**: Be cautious when excluding directories from antivirus scans, as this can potentially expose your system to threats if malicious files are placed in these directories.


```powershell
Add-MpPreference -ExclusionPath "C:\Users\[USER]\AppData\Local\Temp\1\townsuite\codesigning"
```

