using Microsoft.VisualBasic;
using System.Text;

namespace TownSuite.CodeSigning.Service
{
    public class Signer
    {
        readonly Settings _settings;
        public Signer(Settings settings)
        {
            _settings = settings;
        }

        public (bool IsSigned, string Message) Sign(string currentfile)
        {
            var msg = new StringBuilder();
            using (var p = new System.Diagnostics.Process())
            {
                p.StartInfo.FileName = _settings.SignToolPath;

                p.StartInfo.Arguments = _settings.SignToolOptions.Replace("{FilePath}", currentfile);
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.ErrorDialog = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.RedirectStandardOutput = true;

                p.Start();

                // see https://stackoverflow.com/questions/5693191/c-sharp-run-multiple-non-blocking-external-programs-in-parallel/5695109#5695109
                p.ErrorDataReceived += (sender, errorLine) =>
                {
                    if (errorLine.Data != null)
                    {
                        msg.AppendLine($"SignToolInternal StandardError: {errorLine.Data}");
                    }
                };
                p.OutputDataReceived += (sender, outputLine) =>
                {
                    if (outputLine.Data != null)
                    {
                        msg.AppendLine($"SignToolInternal StandardOutput: {outputLine.Data}");
                    }
                };
                p.BeginErrorReadLine();
                p.BeginOutputReadLine();

                if (!p.WaitForExit(_settings.SigntoolTimeoutInMs))
                {
                    msg.AppendLine("signtool timeout reached. Cancelling code signing attempt.");
                    Console.WriteLine(msg.ToString());
                    p.Kill();
                    return (false, msg.ToString());
                }
                Console.WriteLine(msg.ToString());
                return (p.ExitCode == 0, msg.ToString());
            }
        }
    }
}
