using TownSuite.CodeSigning.Service;

namespace TownSuite.CodeSigning.Tests
{
    [TestFixture]
    public class SignToolArgumentsTest
    {
        [Test]
        public void StripTimestampOptions_RemovesRfc3161TimestampAndDigest()
        {
            var stripped = SignToolArguments.StripTimestampOptions(
                "sign /tr \"http://timestamp.digicert.com\" /td SHA256 /fd SHA256 /f \"{BaseDirectory}testcert.pfx\" /p \"password\" /v \"{FilePath}\"");

            Assert.Multiple(() =>
            {
                Assert.That(stripped, Does.Not.Contain("/tr"));
                Assert.That(stripped, Does.Not.Contain("/td"));
                Assert.That(stripped, Does.Not.Contain("timestamp.digicert.com"));
                // Everything else has to survive untouched, placeholders and quoting included.
                Assert.That(stripped, Does.Contain("/fd SHA256"));
                Assert.That(stripped, Does.Contain("/f \"{BaseDirectory}testcert.pfx\""));
                Assert.That(stripped, Does.Contain("/p \"password\""));
                Assert.That(stripped, Does.Contain("\"{FilePath}\""));
                Assert.That(stripped, Does.StartWith("sign"));
            });
        }

        [Test]
        public void StripTimestampOptions_RemovesAuthenticodeTimestamp()
        {
            var stripped = SignToolArguments.StripTimestampOptions(
                "sign /fd SHA256 /f \"{BaseDirectory}testcert.pfx\" /p \"password\" /t \"http://timestamp.digicert.com\" /v \"{FilePath}\"");

            Assert.Multiple(() =>
            {
                Assert.That(stripped, Does.Not.Contain("timestamp.digicert.com"));
                Assert.That(stripped, Does.Contain("/fd SHA256"));
                Assert.That(stripped, Does.Contain("/v \"{FilePath}\""));
            });
        }

        [Test]
        public void StripTimestampOptions_HandlesDashPrefixAndUnquotedUrls()
        {
            // signtool accepts "-" as well as "/", and the URL does not have to be quoted.
            var stripped = SignToolArguments.StripTimestampOptions(
                "sign -tr http://timestamp.digicert.com -td sha256 -fd SHA256 -f cert.pfx \"{FilePath}\"");

            Assert.Multiple(() =>
            {
                Assert.That(stripped, Does.Not.Contain("timestamp.digicert.com"));
                Assert.That(stripped, Does.Not.Contain("-td"));
                Assert.That(stripped, Does.Contain("-fd SHA256"));
                Assert.That(stripped, Does.Contain("-f cert.pfx"));
            });
        }

        [Test]
        public void StripTimestampOptions_RemovesSealingTimestamp()
        {
            var stripped = SignToolArguments.StripTimestampOptions(
                "sign /tr \"http://timestamp.digicert.com\" /tseal /fd SHA256 \"{FilePath}\"");

            Assert.Multiple(() =>
            {
                Assert.That(stripped, Does.Not.Contain("/tseal"));
                Assert.That(stripped, Does.Not.Contain("/tr"));
                Assert.That(stripped, Does.Contain("/fd SHA256"));
            });
        }

        [Test]
        public void StripTimestampOptions_LeavesOptionsThatMerelyStartWithTAlone()
        {
            // /td and /tr are the only "t" options being removed; nothing else may be eaten.
            var stripped = SignToolArguments.StripTimestampOptions(
                "sign /fd SHA256 /tp 0 /f \"cert.pfx\" \"{FilePath}\"");

            Assert.That(stripped, Does.Contain("/tp 0"));
        }

        [Test]
        public void StripTimestampOptions_IsANoOp_WhenNothingIsTimestamped()
        {
            const string options = "sign /fd SHA256 /f \"{BaseDirectory}testcert.pfx\" /p \"password\" /v \"{FilePath}\"";

            Assert.That(SignToolArguments.StripTimestampOptions(options), Is.EqualTo(options));
        }
    }
}
