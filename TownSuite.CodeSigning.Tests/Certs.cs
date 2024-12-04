using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace TownSuite.CodeSigning.Tests
{
    static internal class Certs
    {
        public static void CreateTestCert(string savePath = "testcert.pfx", string password = "password")
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

        public static bool ValidateDigitalSignature(string assemblyPath, string certPath, string password)
        {
            using X509Certificate certificate = X509Certificate.CreateFromSignedFile(assemblyPath);
            using X509Certificate cert = new X509Certificate(certPath, password);

            return cert.Issuer.Equals(certificate.Issuer);
        }
    }
}
