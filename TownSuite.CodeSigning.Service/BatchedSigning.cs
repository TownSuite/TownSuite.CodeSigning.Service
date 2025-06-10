using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Collections;
using System.Runtime.InteropServices;

namespace TownSuite.CodeSigning.Service
{
    public static class BatchedSigning
    {
        public static string GetTempFolder()
        {
            return Path.Combine(Path.GetTempPath(), "townsuite", "codesigning");
        }

        public static async Task<IResult> Sign(Dictionary<string, StringValues> headers, Stream body, Settings settings, ILogger logger)
        {
            headers.TryGetValue("X-BatchId", out var batchId);
            headers.TryGetValue("X-BatchReady", out var batchReady);
            bool isBatchJob = VerifyBatchId(batchId);

            string id = Guid.NewGuid().ToString();

            var workingFolder = new DirectoryInfo(Path.Combine(GetTempFolder(), isBatchJob ? batchId : id));
            string workingFilePath = System.IO.Path.Combine(workingFolder.FullName, $"{id}.workingfile");
            try
            {
                if (!workingFolder.Exists)
                {
                    workingFolder.Create();
                }

                await using (var fileStream = new FileStream(workingFilePath, FileMode.Create))
                {
                    if (body.CanSeek) body.Position = 0;
                    await body.CopyToAsync(fileStream);
                }

                if (!isBatchJob)
                {
                    // single file batch, only for backwards compatiblity
                    ProcessFile(settings, logger, id, workingFolder, [workingFilePath]);
                }
                else if (isBatchJob && !string.IsNullOrWhiteSpace(batchReady))
                {
                    var files = GetBatchFiles(workingFolder);
                    ProcessFile(settings, logger, batchId, workingFolder, files);
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

        private static void ProcessFile(Settings settings, ILogger logger, string id, DirectoryInfo workingFolder, string[] files)
        {
            BackgroundQueue.Instance.QueueThread(async () =>
            {
                try
                {
                    using var signer = new Signer(settings, logger);
                    await Queuing.Semaphore.WaitAsync();
                    var results = await signer.SignAsync(workingFolder.FullName, files);

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
            return workingFolder.GetFiles("*.workingfile").Where(p => p.Length>0).Select(p => p.Name).ToArray();
        }

        public static async Task<IResult> Get(Dictionary<string, StringValues> headers, string id, ILogger logger)
        {
            headers.TryGetValue("X-BatchId", out var batchId);
            bool isBatchJob = VerifyBatchId(batchId);

            var workingFolder = new DirectoryInfo(Path.Combine(GetTempFolder(), isBatchJob ? batchId : id));

            if (!workingFolder.Exists)
            {
                return Results.Problem(title: "Not Found", detail: "The id was not found", statusCode: 404);
            }

            if (System.IO.File.Exists(System.IO.Path.Combine(workingFolder.FullName, $"{id}.signed")))
            {
                var workingFile = System.IO.Path.Combine(workingFolder.FullName, $"{id}.workingfile");
                var fileStream = new TempFileStream(workingFile, !isBatchJob ? workingFolder : null);
                return Results.Stream(fileStream);
            }

            if (System.IO.File.Exists(System.IO.Path.Combine(workingFolder.FullName, $"{id}.error")))
            {
                return Results.Problem(title: "Failure to sign", detail: await File.ReadAllTextAsync(System.IO.Path.Combine(workingFolder.FullName, $"{id}.error")), statusCode: 500);
            }

            return Results.Problem(title: "Not Signed", detail: "The file has not been signed yet", statusCode: 425);
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
