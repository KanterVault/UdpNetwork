using System;
using System.Net;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace Server
{
    class Server
    {
        private class Connection
        {
            public EndPoint EndPoint;
            public int messageNumberS = 0;
            public int messageNumberD = 0;

            public Connection(EndPoint endPoint)
            {
                EndPoint = endPoint;
            }
        }

        private static void Exit(object sender, EventArgs e)
        {
            _mainLive = false;
            _server.StopServer();
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            _mainLive = false;
            _server.StopServer();
            Environment.Exit(0);
        }

        private static bool _mainLive = true;
        private static ServerUDP _server;
        static void Main(string[] args)
        {
            Console.WriteLine("Server");
            _server = new ServerUDP();
            _server.StartListener(25566);
            
            AppDomain.CurrentDomain.ProcessExit += Exit;
            Console.CancelKeyPress += Console_CancelKeyPress;

            var clients = new List<Connection>();

            _server.Stopped = () => Console.WriteLine($"Server stopped.");
            _server.Launched = () => Console.WriteLine($"Server Launched.");
            _server.Exceptions = ex => Console.WriteLine(ex);
            _server.UserConnected = ep => Console.WriteLine($"Connected user: {ep}");
            _server.UserDisconnected = ep =>
            {
                clients.RemoveAll(f => f.EndPoint.Equals(ep));
                Console.WriteLine($"Disconnected user: {ep}");
            };

            _server.ReceivedGuaranteed = (endPoint, buffer) =>
            {
                var connection = clients.FirstOrDefault(f => f.EndPoint.Equals(endPoint));
                if (connection == null)
                {
                    clients.Add(new Connection(endPoint));
                }
                Console.WriteLine($"Received from {endPoint}: {Encoding.UTF8.GetString(buffer)}");
            };
            _server.Received = (endPoint, buffer) =>
            {
                var connection = clients.FirstOrDefault(f => f.EndPoint.Equals(endPoint));
                if (connection == null)
                {
                    clients.Add(new Connection(endPoint));
                }
                Console.WriteLine($"{endPoint}: {Encoding.UTF8.GetString(buffer)}");
            };
            while (_mainLive)
            {
                try
                {
                    var key = Console.ReadKey();
                    switch (key.Key)
                    {
                        case ConsoleKey.S:
                            foreach (var connection in clients)
                            {
                                _server.SendGuaranteed?.Invoke(connection.EndPoint, Encoding.UTF8.GetBytes($"Message number {connection.messageNumberS}."));
                                connection.messageNumberS++;
                            }
                            break;
                        case ConsoleKey.D:
                            foreach (var connection in clients)
                            {
                                _server.Send?.Invoke(connection.EndPoint, Encoding.UTF8.GetBytes($"{connection.messageNumberD}"));
                                connection.messageNumberD++;
                            }
                            break;
                        case ConsoleKey.D1: _server.StartListener(25566); break;
                        case ConsoleKey.D2: _server.StopServer(); break;
                        default: break;
                    }
                }
                catch { }
            }
        }
    }
}
