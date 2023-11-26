using BenchmarkDotNet.Running;

namespace Benchmark
{
    internal class Program
    {
        static void Main(string[] args)
        {
#if DEBUG
            var bench = new Benchmark();
            bench.Globalsetup();
            while (true)
            {
                bench.GetRating();
            }
#else
            BenchmarkRunner.Run<Benchmark>();
#endif
        }
    }
}
