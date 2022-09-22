using System;
using System.Net;
using System.Text;

namespace Client
{
    class Client
    {
        private static void Exit(object sender, EventArgs e)
        {
            _client.StopClient();
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            _client.StopClient();
            Environment.Exit(0);
        }

        private static ClientUDP _client = new ClientUDP();
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.ProcessExit += Exit;
            Console.CancelKeyPress += Console_CancelKeyPress;

            Console.WriteLine("Client");
            _client.StartClient(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 25566));

            _client.ReceivedGuaranteed = buffer =>
            {
                Console.WriteLine($"Received: {Encoding.UTF8.GetString(buffer)}");
            };
            _client.Received = buffer =>
            {
                Console.WriteLine($"{Encoding.UTF8.GetString(buffer)}");
            };
            _client.Connected = () => { Console.WriteLine("Connected"); };
            _client.Disconnected = () => { Console.WriteLine("Disconnected"); };
            _client.Exceptions = ex => { Console.WriteLine($"ERROR: {ex}"); };

            var indexS = 0;
            var indexD = 0;
            while (true)
            {
                var key = Console.ReadKey().Key;
                switch (key)
                {
                    case ConsoleKey.S:
                        _client.SendGuaranteed?.Invoke(Encoding.UTF8.GetBytes($"Message number {indexS}."));
                        indexS++;
                        break;
                    case ConsoleKey.D:
                        _client.Send?.Invoke(Encoding.UTF8.GetBytes($"{indexD}"));
                        indexD++;
                        break;
                    case ConsoleKey.R:
                        indexS = 0;
                        indexD = 0;
                        _client.StartClient(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 25566));
                        break;
                    default: break;
                }
            }
        }
    }
}
