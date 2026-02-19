using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

// Detached signature generator producing a PKCS#7 detached signature.
// Uses System.Security.Cryptography.Pkcs (SignedCms/CmsSigner) and can sign using a certificate
// provided via PFX in Settings or from the Windows certificate store (depending on configuration).
namespace TownSuite.CodeSigning.Service
{
    public class DetachedSigner
    {
        readonly DetachedSignerSettings _settings;
        readonly ILogger _logger;

        public DetachedSigner(DetachedSignerSettings settings, ILogger logger)
        {
            _settings = settings;
            _logger = logger;
        }

        public async Task<(bool IsSigned, string Message)> SignDetachedAsync(string inputFilePath, string signatureFilePath)
        {
            System.Security.Cryptography.X509Certificates.X509Certificate2 storeCert = null;
            try
            {
                var thumbprint = _settings.Thumbprint;
                var subject = _settings.SubjectName;
                var fpath = _settings.CertificateFilePath?
                    .Replace("{BaseDirectory}", AppContext.BaseDirectory + System.IO.Path.DirectorySeparatorChar);
                var pfxPass = _settings.CertificatePassword;
                var digestAlgorithm = GetDigestAlgorithmOid(_settings.DigestAlgorithm);
                var cspName = _settings.CspName;
                var keyContainer = _settings.KeyContainer;
                var tokenPin = _settings.TokenPin;
                bool useCsp = !string.IsNullOrWhiteSpace(cspName) && !string.IsNullOrWhiteSpace(keyContainer);

                // Try locate certificate in store by thumbprint
                if (!string.IsNullOrWhiteSpace(thumbprint))
                {
                    var t = thumbprint.Replace(" ", "").ToUpperInvariant();
                    storeCert = FindCertByThumbprint(System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine, t)
                                ?? FindCertByThumbprint(System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser, t);
                }

                // If CertificateFilePath points to a .cer, try to use that to find the certificate in store
                if (storeCert == null && !string.IsNullOrWhiteSpace(fpath))
                {
                    try
                    {
                        var expanded = Environment.ExpandEnvironmentVariables(fpath);
                        if (File.Exists(expanded) && string.Equals(Path.GetExtension(expanded), ".cer", StringComparison.OrdinalIgnoreCase))
                        {
                            using var certFromFile = new System.Security.Cryptography.X509Certificates.X509Certificate2(expanded);
                            var t = certFromFile.Thumbprint?.Replace(" ", "").ToUpperInvariant();
                            if (!string.IsNullOrWhiteSpace(t))
                            {
                                storeCert = FindCertByThumbprint(System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine, t)
                                            ?? FindCertByThumbprint(System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser, t);
                            }
                        }
                    }
                    catch { /* ignore and continue */ }
                }

                // If subject name provided, search by subject
                if (storeCert == null && !string.IsNullOrWhiteSpace(subject))
                {
                    storeCert = FindCertBySubjectName(System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine, subject)
                                ?? FindCertBySubjectName(System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser, subject);
                }

                // If we found a cert in store and it has a private key, create a detached PKCS#7 with SignedCms.
                // When a CSP with PIN is available, supply the PIN to avoid interactive prompts.
                // When a CSP is specified without a PIN, use non-silent mode to allow the provider to prompt.
                if (storeCert != null && storeCert.HasPrivateKey)
                {
                    var content = await File.ReadAllBytesAsync(inputFilePath);
                    var contentInfo = new System.Security.Cryptography.Pkcs.ContentInfo(content);
                    var signedCms = new System.Security.Cryptography.Pkcs.SignedCms(contentInfo, detached: true);

                    if (useCsp && !string.IsNullOrEmpty(tokenPin))
                    {
                        using var rsa = new System.Security.Cryptography.RSACryptoServiceProvider(
                            BuildCspParameters(cspName, keyContainer, tokenPin));
                        using var certWithKey = storeCert.CopyWithPrivateKey(rsa);
                        var signer = new System.Security.Cryptography.Pkcs.CmsSigner(
                            System.Security.Cryptography.Pkcs.SubjectIdentifierType.IssuerAndSerialNumber, certWithKey)
                        {
                            DigestAlgorithm = digestAlgorithm
                        };
                        signedCms.ComputeSignature(signer, silent: true);
                    }
                    else
                    {
                        var signer = new System.Security.Cryptography.Pkcs.CmsSigner(
                            System.Security.Cryptography.Pkcs.SubjectIdentifierType.IssuerAndSerialNumber, storeCert)
                        {
                            DigestAlgorithm = digestAlgorithm
                        };
                        signedCms.ComputeSignature(signer, silent: !useCsp);
                    }

                    var outBytes = signedCms.Encode();
                    await File.WriteAllBytesAsync(signatureFilePath, outBytes);
                    return (true, string.Empty);
                }

                // CSP-based signing: load certificate from PFX and use CSP/key container for the private key (e.g., hardware token)
                if (useCsp && !string.IsNullOrWhiteSpace(fpath))
                {
                    try
                    {
                        var expanded = Environment.ExpandEnvironmentVariables(fpath);
                        if (File.Exists(expanded) && (expanded.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase) || expanded.EndsWith(".p12", StringComparison.OrdinalIgnoreCase)))
                        {
                            using var pfxCert = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                                expanded,
                                pfxPass ?? string.Empty,
                                System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.EphemeralKeySet);
                            using var rsa = new System.Security.Cryptography.RSACryptoServiceProvider(
                                BuildCspParameters(cspName, keyContainer, tokenPin));
                            using var certWithKey = pfxCert.CopyWithPrivateKey(rsa);

                            var content = await File.ReadAllBytesAsync(inputFilePath);
                            var contentInfo = new System.Security.Cryptography.Pkcs.ContentInfo(content);
                            var signedCms = new System.Security.Cryptography.Pkcs.SignedCms(contentInfo, detached: true);
                            var signer = new System.Security.Cryptography.Pkcs.CmsSigner(
                                System.Security.Cryptography.Pkcs.SubjectIdentifierType.IssuerAndSerialNumber, certWithKey)
                            {
                                DigestAlgorithm = digestAlgorithm
                            };
                            signedCms.ComputeSignature(signer, silent: !string.IsNullOrEmpty(tokenPin));
                            var outBytes = signedCms.Encode();
                            await File.WriteAllBytesAsync(signatureFilePath, outBytes);
                            return (true, string.Empty);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to use CSP-based signing with PFX certificate");
                    }
                }

                // Fallback: if CertificateFilePath references a PFX, try to load it with provided password
                if (!string.IsNullOrWhiteSpace(fpath))
                {
                    try
                    {
                        var expanded = Environment.ExpandEnvironmentVariables(fpath);
                        if (File.Exists(expanded) && (expanded.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase) || expanded.EndsWith(".p12", StringComparison.OrdinalIgnoreCase)))
                        {
                            using var pfx = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                                expanded,
                                pfxPass ?? string.Empty,
                                System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.MachineKeySet |
                                System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.EphemeralKeySet);
                            if (pfx.HasPrivateKey)
                            {
                                var content = await File.ReadAllBytesAsync(inputFilePath);
                                var contentInfo = new System.Security.Cryptography.Pkcs.ContentInfo(content);
                                var signedCms = new System.Security.Cryptography.Pkcs.SignedCms(contentInfo, detached: true);
                                var signer = new System.Security.Cryptography.Pkcs.CmsSigner(System.Security.Cryptography.Pkcs.SubjectIdentifierType.IssuerAndSerialNumber, pfx)
                                {
                                    DigestAlgorithm = digestAlgorithm
                                };
                                signedCms.ComputeSignature(signer, silent: true);
                                var outBytes = signedCms.Encode();
                                await File.WriteAllBytesAsync(signatureFilePath, outBytes);
                                return (true, string.Empty);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to use PFX fallback for detached signing");
                    }
                }

                return (false, "No usable certificate/private key found for detached signing");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Detached signing failed");
                return (false, ex.Message);
            }
            finally
            {
                storeCert?.Dispose();
            }
        }

        /// <summary>
        /// Creates CspParameters for a hardware token CSP, optionally setting the token PIN via KeyPassword.
        /// Resolves the correct provider type from the Windows registry to avoid
        /// "Provider type does not match registered value" errors with hardware tokens.
        /// </summary>
        private static System.Security.Cryptography.CspParameters BuildCspParameters(string providerName, string containerName, string pin)
        {
            int providerType = GetRegisteredProviderType(providerName);
            var cspParams = new System.Security.Cryptography.CspParameters(providerType, providerName, containerName)
            {
                Flags = System.Security.Cryptography.CspProviderFlags.UseExistingKey
            };
            if (!string.IsNullOrEmpty(pin))
            {
                var securePin = new System.Security.SecureString();
                foreach (char c in pin)
                    securePin.AppendChar(c);
                securePin.MakeReadOnly();
                cspParams.KeyPassword = securePin;
            }
            return cspParams;
        }

        /// <summary>
        /// Looks up the registered provider type for a CSP name from the Windows registry.
        /// Falls back to PROV_RSA_AES (24) if the lookup fails.
        /// </summary>
        private static int GetRegisteredProviderType(string providerName)
        {
            const int PROV_RSA_AES = 24;
            if (string.IsNullOrWhiteSpace(providerName))
                return PROV_RSA_AES;

            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    $@"SOFTWARE\Microsoft\Cryptography\Defaults\Provider\{providerName}");
                if (key?.GetValue("Type") is int registeredType)
                    return registeredType;
            }
            catch
            {
                // Registry lookup may fail on non-Windows or with restricted permissions.
            }

            return PROV_RSA_AES;
        }

        private System.Security.Cryptography.X509Certificates.X509Certificate2 FindCertByThumbprint(System.Security.Cryptography.X509Certificates.StoreLocation location, string thumbprint)
        {
            try
            {
                using var store = new System.Security.Cryptography.X509Certificates.X509Store(System.Security.Cryptography.X509Certificates.StoreName.My, location);
                store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);
                var certs = store.Certificates.Find(System.Security.Cryptography.X509Certificates.X509FindType.FindByThumbprint, thumbprint, validOnly: false);
                if (certs.Count > 0) return certs[0];
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Failed to find cert in {location} by thumbprint");
            }
            return null;
        }

        private System.Security.Cryptography.X509Certificates.X509Certificate2 FindCertBySubjectName(System.Security.Cryptography.X509Certificates.StoreLocation location, string subjectName)
        {
            try
            {
                using var store = new System.Security.Cryptography.X509Certificates.X509Store(System.Security.Cryptography.X509Certificates.StoreName.My, location);
                store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);
                var certs = store.Certificates.Find(System.Security.Cryptography.X509Certificates.X509FindType.FindBySubjectName, subjectName, validOnly: false);
                if (certs.Count > 0) return certs[0];
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Failed to find cert in {location} by subject");
            }
            return null;
        }

        private System.Security.Cryptography.Oid GetDigestAlgorithmOid(string fileDigest)
        {
            if (string.IsNullOrWhiteSpace(fileDigest))
            {
                return new System.Security.Cryptography.Oid("2.16.840.1.101.3.4.2.1");
            }

            switch (fileDigest.Trim().ToUpperInvariant())
            {
                case "SHA1":
                    return new System.Security.Cryptography.Oid("1.3.14.3.2.26");
                case "SHA256":
                    return new System.Security.Cryptography.Oid("2.16.840.1.101.3.4.2.1");
                case "SHA384":
                    return new System.Security.Cryptography.Oid("2.16.840.1.101.3.4.2.2");
                case "SHA512":
                    return new System.Security.Cryptography.Oid("2.16.840.1.101.3.4.2.3");
                default:
                    _logger.LogWarning("Unknown /fd value '{FileDigest}'. Using SHA256 for detached signing.", fileDigest);
                    return new System.Security.Cryptography.Oid("2.16.840.1.101.3.4.2.1");
            }
        }
    }
}
