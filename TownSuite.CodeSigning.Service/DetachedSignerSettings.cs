namespace TownSuite.CodeSigning.Service
{
    /// <summary>
    /// Configuration for detached PKCS#7 signing. Bound from the "DetachedSignerSettings" section in appsettings.json.
    /// </summary>
    public record DetachedSignerSettings
    {
        /// <summary>
        /// Certificate thumbprint (SHA-1 hash) used to locate the certificate in the Windows certificate store.
        /// </summary>
        public string Thumbprint { get; init; }

        /// <summary>
        /// Certificate subject name used to locate the certificate in the Windows certificate store.
        /// </summary>
        public string SubjectName { get; init; }

        /// <summary>
        /// Path to a certificate file (.pfx, .p12, or .cer). Supports {BaseDirectory} placeholder.
        /// </summary>
        public string CertificateFilePath { get; init; }

        /// <summary>
        /// Password for the PFX/P12 certificate file.
        /// </summary>
        public string CertificatePassword { get; init; }

        /// <summary>
        /// Digest algorithm name (SHA1, SHA256, SHA384, SHA512). Defaults to SHA256 when not set.
        /// </summary>
        public string DigestAlgorithm { get; init; }

        /// <summary>
        /// Cryptographic Service Provider name for hardware token signing.
        /// </summary>
        public string CspName { get; init; }

        /// <summary>
        /// Key container name within the CSP.
        /// </summary>
        public string KeyContainer { get; init; }

        /// <summary>
        /// PIN for the hardware token. When set, signing runs in silent mode.
        /// </summary>
        public string TokenPin { get; init; }
    }
}
