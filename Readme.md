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
