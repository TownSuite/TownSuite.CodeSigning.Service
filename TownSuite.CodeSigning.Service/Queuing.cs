namespace TownSuite.CodeSigning.Service
{
    static class Queuing
    {
        public static void SetSemaphore(Settings settings)
        {
            // Validate that the configured limit is greater than 0
            if (settings.SemaphoreSlimProcessPerCpuLimit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(settings.SemaphoreSlimProcessPerCpuLimit), "SemaphoreSlimProcessPerCpuLimit must be greater than 0.");
            }

            int count = Environment.ProcessorCount * settings.SemaphoreSlimProcessPerCpuLimit;
            // Initialize the semaphore with both initialCount and maxCount set to the calculated value
            semaphore = new SemaphoreSlim(count, count);
        }
        // Allows concurrent operations up to the number of CPU cores
        public static SemaphoreSlim semaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);
    }
}
