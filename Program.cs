using System;
using System.Threading;
using System.Threading.Tasks;

namespace Internet_Monitor
{
    class Program
    {
        static void Main(string[] args)
        {
            var monitor = new Monitor();
            monitor.RaiseEvent += (sender, data) => Console.WriteLine(data.Json);
            while (true)
            {
                Thread.Sleep(60000 - (int)(DateTimeOffset.Now.ToUnixTimeMilliseconds() % 60000));
                monitor.Execute();
            }
        }
    }
}
