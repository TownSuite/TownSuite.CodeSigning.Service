using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography.Pkcs;

namespace TownSuite.CodeSigning.Service
{
    static public class Certs
    {
        public static void CreateTestCert(string savePath, string password)
        {
            // Define the certificate subject name
            string subjectName = "CN=TestCertificate";

            // Create a new RSA key pair
            using (RSA rsa = RSA.Create(2048))
            {
                // Create a certificate request
                var certRequest = new CertificateRequest(
                    subjectName,
                    rsa,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);

                // Specify certificate extensions
                certRequest.CertificateExtensions.Add(
                    new X509BasicConstraintsExtension(false, false, 0, false));
                certRequest.CertificateExtensions.Add(
                    new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
                certRequest.CertificateExtensions.Add(
                    new X509SubjectKeyIdentifierExtension(certRequest.PublicKey, false));

                // Create the self-signed certificate
                using (X509Certificate2 certificate = certRequest.CreateSelfSigned(
                    DateTimeOffset.Now,
                    DateTimeOffset.Now.AddYears(100)))
                {
                    // Export the certificate to a PFX file
                    byte[] pfxBytes = certificate.Export(X509ContentType.Pfx, password);

                    // Write the PFX file to disk
                    File.WriteAllBytes(savePath, pfxBytes);

                }
            }
        }

        public static void ConvertPfxToPrivatePublicPem(string pfxPath, string password, out string privateKeyPem, out string publicCertPem)
        {
            using var cert = new X509Certificate2(pfxPath, password, X509KeyStorageFlags.Exportable);
            var privateKey = cert.GetRSAPrivateKey();
            privateKeyPem = ExportPrivateKeyToPem(privateKey);

            // Export full certificate (not just public key) in PEM format so tooling like openssl
            // can use it as a signer certificate file.
            var certBytes = cert.Export(X509ContentType.Cert);
            var certPem = new StringBuilder();
            certPem.AppendLine("-----BEGIN CERTIFICATE-----");
            certPem.AppendLine(Convert.ToBase64String(certBytes, Base64FormattingOptions.InsertLineBreaks));
            certPem.AppendLine("-----END CERTIFICATE-----");
            publicCertPem = certPem.ToString();
        }

        private static string ExportPrivateKeyToPem(RSA privateKey)
        {
            var privateKeyBytes = privateKey.ExportPkcs8PrivateKey();
            var privateKeyPem = new StringBuilder();
            privateKeyPem.AppendLine("-----BEGIN PRIVATE KEY-----");
            privateKeyPem.AppendLine(Convert.ToBase64String(privateKeyBytes, Base64FormattingOptions.InsertLineBreaks));
            privateKeyPem.AppendLine("-----END PRIVATE KEY-----");
            return privateKeyPem.ToString();
        }

        private static string ExportPublicKeyToPem(RSA publicKey)
        {
            var publicKeyBytes = publicKey.ExportSubjectPublicKeyInfo();
            var publicKeyPem = new StringBuilder();
            publicKeyPem.AppendLine("-----BEGIN PUBLIC KEY-----");
            publicKeyPem.AppendLine(Convert.ToBase64String(publicKeyBytes, Base64FormattingOptions.InsertLineBreaks));
            publicKeyPem.AppendLine("-----END PUBLIC KEY-----");
            return publicKeyPem.ToString();
        }


        public static bool ValidateDigitalSignature(string assemblyPath, string certPath, string password)
        {
            using X509Certificate certificate = X509Certificate.CreateFromSignedFile(assemblyPath);
            using X509Certificate cert = new X509Certificate(certPath, password);

            return cert.Issuer.Equals(certificate.Issuer);
        }

        public static bool ValidateDetachedSignature(string originalFilePath, string signatureFilePath, string certPath, string password)
        {
            try
            {
                var originalBytes = File.ReadAllBytes(originalFilePath);
                var signatureBytes = File.ReadAllBytes(signatureFilePath);

                var contentInfo = new ContentInfo(originalBytes);
                var signedCms = new SignedCms(contentInfo, detached: true);
                signedCms.Decode(signatureBytes);

                // Load expected certificate and provide it as extra store in case the
                // CMS signature does not embed the full certificate. VerifySignatureOnly
                // avoids building a trusted chain (the test cert is self-signed).
                using var expectedCert = new X509Certificate2(certPath, password);
                var extra = new X509Certificate2Collection(expectedCert);
                signedCms.CheckSignature(extra, verifySignatureOnly: true);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
