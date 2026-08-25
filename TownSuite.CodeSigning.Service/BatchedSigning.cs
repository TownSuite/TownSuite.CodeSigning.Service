using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Collections;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace TownSuite.CodeSigning.Service
{
    public static class BatchedSigning
    {
        private static readonly Regex SafeExtension = new(@"^\.[A-Za-z0-9]{1,15}$", RegexOptions.Compiled);

        public static string GetSanitizedExtension(StringValues fileExtension)
        {
            string value = fileExtension.ToString();
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            value = value.Trim();
            if (!value.StartsWith('.'))
            {
                value = "." + value;
            }

            return SafeExtension.IsMatch(value) ? value.ToLowerInvariant() : string.Empty;
        }

        public static string DetectContainerExtension(string filePath)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                Span<byte> magic = stackalloc byte[4];
                if (stream.Read(magic) != 4) return string.Empty;
                if (magic[0] != 0x50 || magic[1] != 0x4B) return string.Empty;

                stream.Position = 0;
                using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
                bool isAppxPackage = zip.Entries.Any(e =>
                    e.FullName.Equals("AppxManifest.xml", StringComparison.OrdinalIgnoreCase)
                    || e.FullName.Equals("AppxBundleManifest.xml", StringComparison.OrdinalIgnoreCase)
                    || e.FullName.Equals("AppxMetadata/AppxBundleManifest.xml", StringComparison.OrdinalIgnoreCase));

                return isAppxPackage ? ".msix" : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        public static string GetTempFolder()
        {
            string? envTemp = Environment.GetEnvironmentVariable("TOWNSUITE_CODESIGNING_TEMP");
            if (!string.IsNullOrWhiteSpace(envTemp))
            {
                return envTemp;
            }

            return Path.Combine(Path.GetTempPath(), "townsuite", "codesigning");
        }

        public static async Task<IResult> Sign(Dictionary<string, StringValues> headers, Stream body, ILogger logger, ISigner signer)
        {
            headers.TryGetValue("X-BatchId", out var batchId);
            headers.TryGetValue("X-BatchReady", out var batchReady);
            headers.TryGetValue("X-FileExtension", out var fileExtension);
            bool isBatchJob = VerifyBatchId(batchId);

            string id = Guid.NewGuid().ToString();
            string extension = GetSanitizedExtension(fileExtension);

            var workingFolder = new DirectoryInfo(Path.Combine(GetTempFolder(), isBatchJob ? batchId : id));
            string workingFilePath = System.IO.Path.Combine(workingFolder.FullName, $"{id}.workingfile{extension}");
            try
            {
                workingFolder.CreateIfNotExists();

                await using (var fileStream = new FileStream(workingFilePath, FileMode.Create))
                {
                    if (body.CanSeek) body.Position = 0;
                    await body.CopyToAsync(fileStream);
                }

                if (string.IsNullOrEmpty(extension))
                {
                    extension = DetectContainerExtension(workingFilePath);
                    if (!string.IsNullOrEmpty(extension))
                    {
                        string detectedPath = System.IO.Path.Combine(workingFolder.FullName, $"{id}.workingfile{extension}");
                        File.Move(workingFilePath, detectedPath, overwrite: true);
                        workingFilePath = detectedPath;
                    }
                }

                if (!isBatchJob)
                {
                    // single file batch, only for backwards compatiblity
                    ProcessFile(signer, logger, id, workingFolder, new[] { workingFilePath });
                }
                else if (isBatchJob && !string.IsNullOrWhiteSpace(batchReady))
                {
                    var files = GetBatchFiles(workingFolder);
                    ProcessFile(signer, logger, batchId, workingFolder, files);
                }

                return Results.Ok(id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "/sign/batch POST failure");
                return Results.Problem(title: "Failure accept", detail: ex.Message ?? "", statusCode: 500);
            }
        }

        private static bool VerifyBatchId(StringValues batchId)
        {
            if (!string.IsNullOrWhiteSpace(batchId))
            {
                // verify X-BatchId is a guid
                var isValidGuid = Guid.TryParse(batchId, out var _);
                if (!isValidGuid)
                {
                    throw new InvalidDataException("When set X-BatchId should be a valid GUID");
                }
                return true;
            }
            return false;
        }

        private static void ProcessFile(ISigner signer, ILogger logger, string id, DirectoryInfo workingFolder, string[] files)
        {
            BackgroundQueue.Instance.QueueThread(async () =>
            {
                try
                {
                    await Queuing.Semaphore.WaitAsync();

                    var results = await signer.SignAsync(workingFolder.FullName, files);

                    workingFolder.CreateIfNotExists();
                    if (results.IsSigned)
                    {
                        await File.WriteAllTextAsync(System.IO.Path.Combine(workingFolder.FullName, $"{id}.signed"), "true");
                    }
                    else
                    {
                        await File.WriteAllTextAsync(System.IO.Path.Combine(workingFolder.FullName, $"{id}.error"), results.Message);
                    }
                }
                catch (Exception ex)
                {
                    workingFolder.CreateIfNotExists();
                    await File.WriteAllTextAsync(System.IO.Path.Combine(workingFolder.FullName, $"{id}.error"), ex.Message);
                    logger.LogError(ex, $"Failed to sign file {string.Join(",", files)}");
                    CleanupDir(workingFolder, logger);
                }
                finally
                {
                    Queuing.Semaphore.Release();
                }
            });
        }

        private static string[] GetBatchFiles(DirectoryInfo workingFolder)
        {
            return workingFolder.GetFiles("*.workingfile*")
                .Where(p => p.Length > 0)
                .Where(p => !WorkingFolderMarkers.IsMarker(p.Name))
                .Select(p => p.Name)
                .ToArray();
        }

        public static async Task<IResult> Get(Dictionary<string, StringValues> headers, string id, ISigner signer)
        {
            headers.TryGetValue("X-BatchId", out var batchId);
            bool isBatchJob = VerifyBatchId(batchId);

            var workingFolder = new DirectoryInfo(Path.Combine(GetTempFolder(), isBatchJob ? batchId : id));

            if (!workingFolder.Exists)
            {
                return Results.Problem(title: "Not Found", detail: "The id was not found", statusCode: 404);
            }

            string signedFilesIndicator = isBatchJob ? $"{batchId}.signed" : $"{id}.signed";
            if (System.IO.File.Exists(System.IO.Path.Combine(workingFolder.FullName, signedFilesIndicator)))
            {
                var workingFile = signer.FindResultFile(workingFolder, id);
                if (workingFile is null)
                {
                    return Results.Problem(title: "Failure to sign",
                        detail: $"The signing result for {id} was not found in the working folder.", statusCode: 500);
                }

                var workingStream = new TempFileStream(workingFile, !isBatchJob ? workingFolder : null);
                return Results.Stream(workingStream);
            }

            string? errorFile = FindErrorFile(workingFolder, isBatchJob ? batchId.ToString() : null, id);
            if (errorFile is not null)
            {
                return Results.Problem(title: "Failure to sign", detail: await File.ReadAllTextAsync(errorFile), statusCode: 500);
            }

            return Results.Problem(title: "Not Signed", detail: "The file has not been signed yet", statusCode: 425);
        }

        private static string? FindErrorFile(DirectoryInfo workingFolder, string? batchId, string id)
        {
            var candidates = new[] { $"{id}.error", batchId is null ? null : $"{batchId}.error" };
            foreach (var candidate in candidates)
            {
                if (candidate is null) continue;
                string path = System.IO.Path.Combine(workingFolder.FullName, candidate);
                if (System.IO.File.Exists(path)) return path;
            }
            return null;
        }

        static void CleanupDir(DirectoryInfo dir, ILogger logger)
        {
            try
            {
                dir.Delete(true);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"failed to cleanup dir {dir}");
            }
        }
    }
}
