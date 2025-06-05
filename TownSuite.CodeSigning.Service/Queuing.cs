namespace TownSuite.CodeSigning.Service
{
    static class Queuing
    {
        public static void SetSemaphore(Settings settings)
        {
            // Initialize the semaphore with the limit set in settings
            semaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount * settings.SemaphoreSlimProcessPerCpuLimit);
        }
        // Allows concurrent operations up to the number of CPU cores
        public static SemaphoreSlim semaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);
    }
}
