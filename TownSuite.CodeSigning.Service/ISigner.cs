namespace TownSuite.CodeSigning.Service
{
    public interface ISigner
    {
        Task<(bool IsSigned, string Message)> SignAsync(string workingDir, string[] files);
        string? FindResultFile(DirectoryInfo workingFolder, string id);
    }
}