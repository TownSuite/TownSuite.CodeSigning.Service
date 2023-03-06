namespace TownSuite.CodeSigning.Service
{
    public class Signer
    {
        readonly Settings _settings;
        public Signer(Settings settings)
        {
            _settings = settings;
        }

        public bool Sign(string currentfile)
        {
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
                        Console.WriteLine($"SignToolInternal StandardError: {errorLine.Data}");
                    }
                };
                p.OutputDataReceived += (sender, outputLine) =>
                {
                    if (outputLine.Data != null)
                    {
                        Console.WriteLine($"SignToolInternal StandardOutput: {outputLine.Data}");
                    }
                };
                p.BeginErrorReadLine();
                p.BeginOutputReadLine();

                if (!p.WaitForExit(_settings.SigntoolTimeoutInMs))
                {
                    Console.WriteLine("signtool timeout reached. Cancelling code signing attempt.");
                    p.Kill();
                    return false;
                }
                return p.ExitCode == 0;
            }
        }
    }
}
