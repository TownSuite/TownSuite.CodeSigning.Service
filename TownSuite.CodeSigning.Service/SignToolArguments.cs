using System.Text.RegularExpressions;

namespace TownSuite.CodeSigning.Service
{
    /// <summary>
    /// Helpers for deriving variants of the configured signtool argument string.
    /// </summary>
    public static class SignToolArguments
    {
        // signtool accepts both "/" and "-" as the option prefix. The timestamp options are:
        //   /t <url>   Authenticode timestamp     /tr <url>  RFC 3161 timestamp
        //   /td <alg>  RFC 3161 digest algorithm  /tseal     sealing timestamp (requires /tr)
        // The value is matched as either a quoted string or a run of non-whitespace so both the
        // quoted and bare forms are removed along with their option.
        private static readonly Regex TimestampOptionPattern = new Regex(
            @"(?<=^|\s)[/-](?:tr|td|t)\s+(?:""[^""]*""|\S+)|(?<=^|\s)[/-]tseal(?=\s|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Returns <paramref name="options"/> with the timestamp options removed, leaving the rest
        /// of the argument string byte for byte intact (including the "{FilePath}" and
        /// "{BaseDirectory}" placeholders, which callers substitute later).
        ///
        /// This is a text substitution, not a full command line parse: a quoted value that itself
        /// contains something that looks like a timestamp option (a certificate path containing
        /// " /t ", say) would be mangled. Configure Settings.CanarySignToolOptions explicitly in
        /// that case rather than relying on this.
        /// </summary>
        public static string StripTimestampOptions(string options)
        {
            if (string.IsNullOrWhiteSpace(options))
            {
                return options;
            }

            // The whitespace left behind by a removal is deliberately not collapsed: signtool's
            // argument parsing treats a run of spaces as a single separator, and collapsing would
            // risk touching spaces inside a quoted value such as a certificate path.
            return TimestampOptionPattern.Replace(options, string.Empty).Trim();
        }
    }
}
