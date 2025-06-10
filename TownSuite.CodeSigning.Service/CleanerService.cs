
using System.Runtime;
using System.Threading;

namespace TownSuite.CodeSigning.Service
{
    public class CleanerService : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                CleanupOldFolders();
                await Task.Delay(TimeSpan.FromMinutes(10));
            }
        }

        static void CleanupOldFolders()
        {
            // if a working folder is more than 1 hour old delete it
            var baseFolder = new DirectoryInfo(BatchedSigning.GetTempFolder());
            var folders = baseFolder.GetDirectories();
            DateTime currentTime = DateTime.Now;
            foreach (var folder in folders)
            {
                TimeSpan age = currentTime - folder.CreationTime;
                if (age.TotalHours > 1)
                {
                    try
                    {
                        folder.Delete(true);
                    }
                    catch (Exception)
                    {
                        Console.Error.WriteLine($"Failed to delete folder {folder.FullName} and will try again later");
                    }

                }
            }

        }
    }
}
