using Microsoft.VisualBasic;
using System.Diagnostics;
using System.Text;

namespace TownSuite.CodeSigning.Service
{
    public class Signer : IDisposable
    {
        readonly Settings _settings;
        StringBuilder msg = new StringBuilder();
        Process p;
        public Signer(Settings settings)
        {
            _settings = settings;
        }

        public void Dispose()
        {
            if(p == null)
            {
                return;
            }
            p.OutputDataReceived -= process_OutputDataReceived;
            p.ErrorDataReceived -= process_ErrorDataReceived;
            p.Dispose();
        }

        public (bool IsSigned, string Message) Sign(string currentfile)
        {
            p = new System.Diagnostics.Process();
            p.StartInfo.FileName = _settings.SignToolPath;

            p.StartInfo.Arguments = _settings.SignToolOptions.Replace("{FilePath}", currentfile);
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.ErrorDialog = false;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.RedirectStandardOutput = true;

            p.Start();

            // see https://stackoverflow.com/questions/5693191/c-sharp-run-multiple-non-blocking-external-programs-in-parallel/5695109#5695109
            p.ErrorDataReceived += process_ErrorDataReceived;
            p.OutputDataReceived += process_OutputDataReceived;

            p.BeginErrorReadLine();
            p.BeginOutputReadLine();

            if (!p.WaitForExit(_settings.SigntoolTimeoutInMs))
            {
                msg.AppendLine("signtool timeout reached. Cancelling code signing attempt.");
                Console.WriteLine(msg.ToString());
                try
                {
                    p.Kill();
                }
                catch(Exception ex)
                {
                    Console.WriteLine(ex);
                }

                return (false, msg.ToString());
            }
            Console.WriteLine(msg.ToString());
            return (p.ExitCode == 0, msg.ToString());

        }

        private void process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                msg.AppendLine($"SignToolInternal StandardError: {e.Data}");
            }
        }

        private void process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                msg.AppendLine($"SignToolInternal StandardOutput: {e.Data}");
            }
        }
    }
}
