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
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.RedirectStandardOutput = true;

                p.Start();

                string output = p.StandardOutput.ReadToEnd();
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Console.WriteLine($"SignToolInternal StandardOutput: {output}");
                }
                string errors = p.StandardError.ReadToEnd();

                p.WaitForExit();
                int exitCode = p.ExitCode;
                if (exitCode > 0)
                {
                    var msg = string.Format("Error signing dlls: {0}", currentfile);
                    System.Console.WriteLine(msg);
                    if (!string.IsNullOrWhiteSpace(errors))
                    {
                        Console.WriteLine($"SignToolInternal StandardError: {errors}");
                    }
                }
                p.Close();
                return exitCode == 0;
            }
        }
    }
}
