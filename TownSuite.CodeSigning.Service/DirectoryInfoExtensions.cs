namespace TownSuite.CodeSigning.Service;

internal static class DirectoryInfoExtensions
{
    internal static void CreateIfNotExists(this DirectoryInfo dir)
    {
        if (!dir.Exists)
        {
            dir.Create();
        }
    }
}