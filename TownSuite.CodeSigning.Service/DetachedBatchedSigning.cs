using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace TownSuite.CodeSigning.Service
{
    /// <summary>
    /// Handles batched detached signing only (produces .sig files without invoking signtool).
    /// </summary>
    public static class DetachedBatchedSigning
    {
        public static async Task<IResult> Sign(Dictionary<string, StringValues> headers, Stream body, Settings settings, ILogger logger)
        {
            headers.TryGetValue("X-BatchId", out var batchId);
            headers.TryGetValue("X-BatchReady", out var batchReady);
            bool isBatchJob = VerifyBatchId(batchId);

            string id = Guid.NewGuid().ToString();

            var workingFolder = new DirectoryInfo(Path.Combine(BatchedSigning.GetTempFolder(), isBatchJob ? batchId : id));
            string workingFilePath = System.IO.Path.Combine(workingFolder.FullName, $"{id}.workingfile");
            try
            {
                workingFolder.CreateIfNotExists();

                await using (var fileStream = new FileStream(workingFilePath, FileMode.Create))
                {
                    if (body.CanSeek) body.Position = 0;
                    await body.CopyToAsync(fileStream);
                }

                if (!isBatchJob)
                {
                    ProcessFile(settings, logger, id, workingFolder, workingFilePath);
                }
                else if (isBatchJob && !string.IsNullOrWhiteSpace(batchReady))
                {
                    var files = GetBatchFiles(workingFolder);
                    foreach (var file in files)
                    {
                        string fileId = Path.GetFileNameWithoutExtension(file);
                        ProcessFile(settings, logger, fileId, workingFolder, Path.Combine(workingFolder.FullName, file));
                    }

                    // Write a batch-level signed indicator once all files are queued
                    await File.WriteAllTextAsync(Path.Combine(workingFolder.FullName, $"{batchId}.signed"), "true");
                }

                return Results.Ok(id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "/sign/detached POST failure");
                return Results.Problem(title: "Failure accept", detail: ex.Message ?? "", statusCode: 500);
            }
        }

        private static void ProcessFile(Settings settings, ILogger logger, string id, DirectoryInfo workingFolder, string inputFilePath)
        {
            BackgroundQueue.Instance.QueueThread(async () =>
            {
                try
                {
                    await Queuing.Semaphore.WaitAsync();

                    var sigPath = Path.Combine(workingFolder.FullName, $"{id}.sig");
                    var detachedSigner = new DetachedSigner(settings, logger);
                    var result = await detachedSigner.SignDetachedAsync(inputFilePath, sigPath);

                    workingFolder.CreateIfNotExists();
                    if (result.IsSigned)
                    {
                        await File.WriteAllTextAsync(Path.Combine(workingFolder.FullName, $"{id}.signed"), "true");
                    }
                    else
                    {
                        await File.WriteAllTextAsync(Path.Combine(workingFolder.FullName, $"{id}.error"), result.Message);
                    }
                }
                catch (Exception ex)
                {
                    workingFolder.CreateIfNotExists();
                    await File.WriteAllTextAsync(Path.Combine(workingFolder.FullName, $"{id}.error"), ex.Message);
                    logger.LogError(ex, $"Failed detached signing for {inputFilePath}");
                    CleanupDir(workingFolder, logger);
                }
                finally
                {
                    Queuing.Semaphore.Release();
                }
            });
        }

        public static async Task<IResult> Get(Dictionary<string, StringValues> headers, string id, ILogger logger)
        {
            headers.TryGetValue("X-BatchId", out var batchId);
            bool isBatchJob = VerifyBatchId(batchId);

            var workingFolder = new DirectoryInfo(Path.Combine(BatchedSigning.GetTempFolder(), isBatchJob ? batchId : id));

            if (!workingFolder.Exists)
            {
                return Results.Problem(title: "Not Found", detail: "The id was not found", statusCode: 404);
            }

            string signedFilesIndicator = isBatchJob ? $"{batchId}.signed" : $"{id}.signed";
            if (System.IO.File.Exists(Path.Combine(workingFolder.FullName, signedFilesIndicator)))
            {
                var sigFile = Path.Combine(workingFolder.FullName, $"{id}.sig");
                if (System.IO.File.Exists(sigFile))
                {
                    var resultStream = new TempFileStream(sigFile, !isBatchJob ? workingFolder : null);
                    return Results.Stream(resultStream);
                }
                else
                {
                    return Results.Problem(title: "Not Signed", detail: "Detached signature not ready", statusCode: 425);
                }
            }

            if (System.IO.File.Exists(Path.Combine(workingFolder.FullName, $"{id}.error")))
            {
                return Results.Problem(title: "Failure to sign", detail: await File.ReadAllTextAsync(Path.Combine(workingFolder.FullName, $"{id}.error")), statusCode: 500);
            }

            return Results.Problem(title: "Not Signed", detail: "The file has not been signed yet", statusCode: 425);
        }

        private static bool VerifyBatchId(StringValues batchId)
        {
            if (!string.IsNullOrWhiteSpace(batchId))
            {
                var isValidGuid = Guid.TryParse(batchId, out var _);
                if (!isValidGuid)
                {
                    throw new InvalidDataException("When set X-BatchId should be a valid GUID");
                }
                return true;
            }
            return false;
        }

        private static string[] GetBatchFiles(DirectoryInfo workingFolder)
        {
            return workingFolder.GetFiles("*.workingfile").Where(p => p.Length > 0).Select(p => p.Name).ToArray();
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
