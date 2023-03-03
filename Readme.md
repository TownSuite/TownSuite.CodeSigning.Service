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

# Initial Security

- [x] Add a firewall allow list on the server hosting the code signing service.
- [ ] bearer tokens
