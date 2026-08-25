namespace TownSuite.CodeSigning.Service
{
    public static class WorkingFolderMarkers
    {
        private static readonly string[] MarkerExtensions = { ".sig", ".signed", ".error" };

        public static bool IsMarker(string fileName)
        {
            foreach (var extension in MarkerExtensions)
            {
                if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
