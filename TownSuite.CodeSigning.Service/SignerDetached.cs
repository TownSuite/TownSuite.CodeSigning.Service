using Microsoft.VisualBasic;
using System.Diagnostics;
using System.Security;
using System.Text;
using System.Threading;

namespace TownSuite.CodeSigning.Service
{
    public class SignerDetached : ISigner
    {
        readonly Settings _settings;
        StringBuilder msg = new StringBuilder();

        readonly ILogger _logger;
        public SignerDetached(Settings settings, ILogger logger)
        {
            _settings = settings;
            _logger = logger;
        }

        public async Task<(bool IsSigned, string Message)> SignAsync(string workingDir, string[] files)
        {
            var _cancellationToken = new CancellationTokenSource(_settings.SigntoolTimeoutInMs * files.Length).Token;

            foreach (var file in files)
            {
                using var p = new System.Diagnostics.Process();
                p.StartInfo.FileName = _settings.OpenSSL.OpenSslPath;

                string arguments = _settings.OpenSSL.OpenSslOptions
                    .Replace("{FilePath}", file)
                    .Replace("{BaseDirectory}", AppContext.BaseDirectory + System.IO.Path.DirectorySeparatorChar);

                if (arguments.Contains("{WorkingDirectory}"))
                {
                    arguments = arguments.Replace("{WorkingDirectory}", workingDir + System.IO.Path.DirectorySeparatorChar);
                }

                p.StartInfo.Arguments = arguments;

                p.StartInfo.WorkingDirectory = workingDir;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.ErrorDialog = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.RedirectStandardOutput = true;

                p.Start();

                p.ErrorDataReceived += process_ErrorDataReceived;
                p.OutputDataReceived += process_OutputDataReceived;

                p.BeginErrorReadLine();
                p.BeginOutputReadLine();

                await p.WaitForExitAsync(_cancellationToken);

                if (_cancellationToken.IsCancellationRequested)
                {
                    msg.AppendLine("openssl timeout reached. Cancelling code signing attempt.");
                    _logger.LogWarning(msg.ToString());
                    try
                    {
                        p.Kill();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "openssl process exit failure");
                    }

                    return (false, msg.ToString());
                }

                // TODO: determine if openssl was successful based on exit code and/or output, and return false if it was not successful.
                // For now we will return true regardless of the exit code, as long as the process completed within the timeout,
                // and log the exit code and output for debugging purposes.
                _logger.LogInformation($"OpensslInternal ExitCode: {p.ExitCode}, Message: {msg.ToString()}");
            }

            await TimeStamp(workingDir, files, _cancellationToken);

            CleanupOriginalFiles(workingDir, files);
            return (true, msg.ToString());
        }

        private async Task TimeStamp(string workingDir, string[] files, CancellationToken _cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_settings.OpenSSL.OsslSignCodePath) || string.IsNullOrWhiteSpace(_settings.OpenSSL.TimestampOptions))
            {
                return;
            }
            foreach (var file in files)
            {
                using var p = new System.Diagnostics.Process();
                p.StartInfo.FileName = _settings.OpenSSL.OsslSignCodePath;

                string arguments = _settings.OpenSSL.TimestampOptions
                    .Replace("{FilePath}", file);

                p.StartInfo.Arguments = arguments;

                p.StartInfo.WorkingDirectory = workingDir;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.ErrorDialog = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.RedirectStandardOutput = true;

                p.Start();

                p.ErrorDataReceived += process_ErrorDataReceived;
                p.OutputDataReceived += process_OutputDataReceived;

                p.BeginErrorReadLine();
                p.BeginOutputReadLine();

                await p.WaitForExitAsync(_cancellationToken);

                if (_cancellationToken.IsCancellationRequested)
                {
                    msg.AppendLine("Opensslsigntool timeout reached. Cancelling code signing attempt.");
                    _logger.LogWarning(msg.ToString());
                    try
                    {
                        p.Kill();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Opensslsigntool process exit failure");
                    }
                }
                _logger.LogInformation($"Opensslsigntool Internal ExitCode: {p.ExitCode}, Message: {msg.ToString()}");
            }
        }

        private void CleanupOriginalFiles(string workingDir, string[] files)
        {
            foreach (var file in files)
            {
                try
                {
                    System.IO.File.Delete(System.IO.Path.Combine(workingDir, file));
                }
                catch
                {
                    _logger.LogInformation($"failed to cleanup {file}.  Will try again later");
                }
            }

            // delete any non .sig, .error, .signed files in the working directory, as these are likely the original files that were signed, and we want to clean them up to save space.
            var dirInfo = new System.IO.DirectoryInfo(workingDir);
            var filesToDelete = dirInfo.GetFiles().Where(f => !f.Extension.Equals(".sig", StringComparison.OrdinalIgnoreCase)
                && !f.Extension.Equals(".error", StringComparison.OrdinalIgnoreCase)
                && !f.Extension.Equals(".signed", StringComparison.OrdinalIgnoreCase));

            foreach (var file in filesToDelete)
            {
                try
                {
                    file.Delete();
                }
                catch
                {
                    _logger.LogInformation($"failed to cleanup {file.FullName}.  Will try again later");
                }
            }
        }

        public string GetFileName(string id)
        {
            if (string.IsNullOrWhiteSpace(_settings.OpenSSL.OsslSignCodePath) || string.IsNullOrWhiteSpace(_settings.OpenSSL.TimestampOptions))
            {
                return $"{id}.workingfile.sig";
            }

            return $"{id}.workingfile.timestamped.sig";
        }

        private void process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                msg.AppendLine($"OpensslInternal StandardError: {e.Data}");
            }
        }

        private void process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                msg.AppendLine($"OpensslInternal StandardOutput: {e.Data}");
            }
        }

    }
}
