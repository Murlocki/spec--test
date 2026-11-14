using System;
using System.Web.Http.SelfHost;

namespace BankVerification
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            string address = args.Length == 0 ? "http://localhost:8085" : args[0];
            var uri = new Uri(address);
            if (!uri.IsLoopback || uri.Scheme != "http")
                throw new ArgumentException("Use an HTTP loopback address.");
            using (var configuration = new HttpSelfHostConfiguration(uri))
            {
                BankApi.Configure(configuration);
                using (var server = new HttpSelfHostServer(configuration))
                {
                    server.OpenAsync().GetAwaiter().GetResult();
                    Console.WriteLine("Bank API listening on " + address);
                    Console.WriteLine("Accounts: 1 = 1000, 2 = 500 minor units. Ctrl+C stops the server.");
                    var stopped = new System.Threading.ManualResetEvent(false);
                    Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e) {
                        e.Cancel = true; stopped.Set();
                    };
                    stopped.WaitOne();
                    server.CloseAsync().GetAwaiter().GetResult();
                }
            }
        }
    }
}
