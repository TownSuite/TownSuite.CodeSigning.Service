static class Queuing
{
    // Allows concurrent operations up to the number of CPU cores
    public static SemaphoreSlim semaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);
}

