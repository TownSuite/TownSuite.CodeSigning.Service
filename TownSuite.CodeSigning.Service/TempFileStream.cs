namespace TownSuite.CodeSigning.Service
{
    public class TempFileStream: FileStream
    {
        private readonly DirectoryInfo _dir;

        public TempFileStream(string workingFile, DirectoryInfo directoryToRemove)
            : base(workingFile, FileMode.Open, FileAccess.Read, FileShare.None, 4096, FileOptions.DeleteOnClose)
        {
            _dir = directoryToRemove;
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            _dir.Delete(true);
        }

        public new void Dispose()
        {
            base.Dispose();
            _dir.Delete(true);
        }
    }
}
