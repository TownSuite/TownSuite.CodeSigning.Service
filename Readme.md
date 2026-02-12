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
./TownSuite.CodeSigning.Client -folder "/path/to/folder/with/assemblies" -file "*.dll;*.exe" -timeout 30000 -url "https://localhost:5000/sign" -token "the token"
```

## Multiple folders with per-folder file lists

Each `-folders` entry pairs a folder path with its own file patterns separated by `|`. Files within are `;` separated. Can be specified multiple times.

Duplicate files across folders are detected by SHA-256 hash and only signed once. After signing, the signed copy is distributed to all duplicate locations.

```bash
./TownSuite.CodeSigning.Client -folders "C:\publish\win-x64|*.dll;*.exe" -folders "C:\publish\linux-x64|mylib.dll;*.so" -timeout 30000 -url "https://localhost:5000/sign" -token "the token"
```

## Recursive folder scan

Use `-rfolder` to recursively scan a parent folder and all its subdirectories for matching files. Same `folder|patterns` syntax as `-folders`. Can be specified multiple times.

This is useful in CI where multiple publish outputs (e.g. `win-x64/`, `linux-x64/`, `win-arm64/`) live under a common parent directory and may contain duplicate files.

```bash
./TownSuite.CodeSigning.Client -rfolder "C:\publish|*.dll;*.exe" -timeout 30000 -url "https://localhost:5000/sign" -token "the token"
```

Multiple recursive roots with different patterns:

```bash
./TownSuite.CodeSigning.Client -rfolder "C:\publish|*.dll;*.exe" -rfolder "C:\other|mylib.dll" -timeout 30000 -url "https://localhost:5000/sign" -token "the token"
```

## Combining options

All folder modes can be combined. Files from every source are merged, deduplicated by SHA-256, signed once, and the signed copy is copied back to all duplicate locations.

```bash
./TownSuite.CodeSigning.Client -file "extra.dll" -folder "C:\extras" -folders "C:\publish\win-x64|*.dll;*.exe" -rfolder "C:\publish\shared|*.dll" -timeout 30000 -url "https://localhost:5000/sign" -token "the token"
```



# Windows Defender Exclusion

For increased performance add the services working directory to the windows defender exclusion path.

**Important Note:**
- **Security Risks**: Be cautious when excluding directories from antivirus scans, as this can potentially expose your system to threats if malicious files are placed in these directories.


```powershell
Add-MpPreference -ExclusionPath "C:\Users\[USER]\AppData\Local\Temp\1\townsuite\codesigning"
```

# Initial Security

- [x] Add a firewall allow list on the server hosting the code signing service.
- [ ] bearer tokens
