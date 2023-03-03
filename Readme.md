Initial Security

- [x] Add a firewall allow list on the server hosting the code signing service.
- [ ] bearer tokens

# curl example

```bash
curl --location 'https://localhost:7153/sign' \
--header 'Content-Type: application/x-msdownload' \
--data '@/C:/the/file/to/upload/and/sign.dll' \
-o output-signed-file.dll
```
