namespace TownSuite.CodeSigning.Service
{
    public class Settings
    {
        public string SignToolPath { get; init; }
        public string SignToolOptions { get; init; }
        public int SigntoolTimeoutInMs { get; init; }
        /// <summary>
        /// bytes
        /// </summary>
        public long MaxRequestBodySize { get; init; }
        public int SemaphoreSlimProcessPerCpuLimit { get; init; }
        public OpenSSLSettings OpenSSL { get; init; }

    }

    public class OpenSSLSettings
    {
        /// <summary>
        /// Path to the openssl executable to use for detached signing. Supports {BaseDirectory} placeholder.
        /// </summary>
        public string OpenSslPath { get; init; }

        /// <summary>
        /// Full openssl cms command arguments template. Supports placeholders:
        /// {InputFilePath}, {SignerPem}, {SignatureFilePath}.
        /// </summary>
        public string OpenSslOptions { get; init; }

        /// <summary>
        /// Timeout in milliseconds for openssl process invocations.
        /// </summary>
        public int OpenSslTimeoutInMs { get; init; }
        
        public string OsslSignCodePath { get; init; }
        public string TimestampOptions { get; init; }
    }
}
