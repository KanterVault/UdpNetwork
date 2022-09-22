using System;
using System.Net;
using System.Linq;
using System.Threading;
using System.Net.Sockets;
using System.Globalization;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Server
{
    public class ServerUDP
    {
        public Action Launched;
        public Action Stopped;
        public Action<EndPoint, byte[]> Send;
        public Action<EndPoint, byte[]> SendGuaranteed;
        public Action<EndPoint, ArraySegment<byte>> Received;
        public Action<EndPoint, ArraySegment<byte>> ReceivedGuaranteed;
        public Action<Exception> Exceptions;
        public Action<EndPoint> UserConnected;
        public Action<EndPoint> UserDisconnected;

        private struct SendedMessage
        {
            public byte[] Buffer;
            public byte Id;
        }

        private class Host
        {
            public EndPoint EndPoint;
            public byte MessageId;
            public ConcurrentQueue<SendedMessage> PackagesToSend;
            public bool SendCompleted;
            public DateTime Time;
            public DateTime AliveTime;
            public SendedMessage SavedSendMessage;
            public byte ReceivedAcceptId;
            public byte LastReceivedGuarantedId;
        }

        private Thread _serverThread = null;
        private Socket _serverSocket = null;
        private bool _live = false;
        public void StartListener(int port)
        {
            try
            {
                if (_serverThread != null) return;
                _live = true;
                _serverThread = new Thread(() =>
                {
                    _serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    _serverSocket.Bind(new IPEndPoint(IPAddress.Any, port));

                    _serverSocket.SendTimeout = 2000;
                    _serverSocket.ReceiveTimeout = 2000;

                    _serverSocket.SendBufferSize = 32768;
                    _serverSocket.ReceiveBufferSize = 32768;

                    try
                    {
                        Send = (endPoint, buffer) =>
                        {
                            if (!_live) return;
                            _serverSocket.SendToAsync(new byte[] { 1, 0 }.Concat(buffer).ToArray(), SocketFlags.None, endPoint);
                        };
                    }
                    catch (Exception ex)
                    {
                        Exceptions?.Invoke(ex);
                    }

                    var hosts = new List<Host>();
                    var hostsLocked = false;
                    try
                    {
                        if (!_live) return;
                        SendGuaranteed = (endPoint, buffer) =>
                        {
                            var selectedHost = hosts.FirstOrDefault(f => f.EndPoint.Equals(endPoint));
                            if (selectedHost.Equals(default(KeyValuePair<EndPoint, Host>))) return;

                            selectedHost.MessageId++;
                            var pack = new SendedMessage()
                            {
                                Id = selectedHost.MessageId,
                                Buffer = buffer
                            };
                            selectedHost.PackagesToSend.Enqueue(pack);
                        };
                    }
                    catch (Exception ex)
                    {
                        Exceptions?.Invoke(ex);
                    }

                    Launched?.Invoke();

                    Parallel.For(0, 16, (id, state) =>
                    {
                    restartTask:
                        try
                        {
                            if (id == 15)
                            {
                                while (_live)
                                {
                                    if (hostsLocked) continue;
                                    foreach (var selectedHost in hosts)
                                    {
                                        if (hostsLocked) break;
                                        if (selectedHost.ReceivedAcceptId == selectedHost.SavedSendMessage.Id) selectedHost.SendCompleted = true;
                                        if (selectedHost.SendCompleted)
                                        {
                                            if (selectedHost.PackagesToSend.TryDequeue(out SendedMessage package))
                                            {
                                                selectedHost.SendCompleted = false;
                                                selectedHost.SavedSendMessage = package;
                                                selectedHost.Time = DateTime.Now.AddMilliseconds(500.0f);
                                                _serverSocket.SendToAsync(new byte[] { 2, package.Id }.Concat(package.Buffer).ToArray(), SocketFlags.None, selectedHost.EndPoint);
                                            }
                                        }
                                        else
                                        {
                                            if (selectedHost.Time < DateTime.Now)
                                            {
                                                _serverSocket.SendToAsync(new byte[] { 2, selectedHost.SavedSendMessage.Id }.Concat(selectedHost.SavedSendMessage.Buffer).ToArray(), SocketFlags.None, selectedHost.EndPoint);
                                                selectedHost.Time = DateTime.Now.AddMilliseconds(500.0f);
                                            }
                                        }
                                    }
                                }
                                state.Stop();
                            }
                            if (id == 14)
                            {
                                while (_live)
                                {
                                    Thread.Sleep(4000);
                                    var hostsToRemove = hosts.Where(f => f.AliveTime < DateTime.Now).ToArray();
                                    for (var i = 0; i < hostsToRemove.Length; i++)
                                    {
                                        hostsLocked = true;
                                        hosts.Remove(hostsToRemove[i]);
                                        hostsLocked = false;
                                        UserDisconnected?.Invoke(hostsToRemove[i].EndPoint);
                                    }
                                }
                                state.Stop();
                            }
                            if (id <= 13)
                            {
                                while (_live)
                                {
                                    var receivedBuffer = new byte[_serverSocket.ReceiveBufferSize];
                                    var remoteEndPoint = (EndPoint)new IPEndPoint(IPAddress.Any, 0);
                                    var receivedBytes = _serverSocket.ReceiveFrom(receivedBuffer, ref remoteEndPoint);
                                    var selectedHost = hosts.FirstOrDefault(f => f.EndPoint.Equals(remoteEndPoint));

                                    if (selectedHost == null)
                                    {
                                        selectedHost = new Host()
                                        {
                                            EndPoint = remoteEndPoint,
                                            PackagesToSend = new ConcurrentQueue<SendedMessage>(),
                                            AliveTime = DateTime.Now.AddSeconds(4)
                                        };
                                        hostsLocked = true;
                                        hosts.Add(selectedHost);
                                        hostsLocked = false;
                                        UserConnected?.Invoke(selectedHost.EndPoint);
                                    }
                                    else
                                    {
                                        selectedHost.AliveTime = DateTime.Now.AddSeconds(4);
                                    }
                                    switch (receivedBuffer[0])
                                    {
                                        case 1: //SIMPLE RECEIVED
                                            Received?.Invoke(remoteEndPoint, receivedBuffer.Skip(2).Take(receivedBytes - 2).ToArray());
                                            break;
                                        case 2: //RECEIVED GUARANTED MESSAGE
                                            if (selectedHost.LastReceivedGuarantedId == receivedBuffer[1])
                                            {
                                                _serverSocket.SendToAsync(new byte[] { 3, receivedBuffer[1] }, SocketFlags.None, selectedHost.EndPoint);
                                            }
                                            else
                                            {
                                                selectedHost.LastReceivedGuarantedId = receivedBuffer[1];
                                                _serverSocket.SendToAsync(new byte[] { 3, receivedBuffer[1] }, SocketFlags.None, selectedHost.EndPoint);
                                                if (_live) ReceivedGuaranteed?.Invoke(remoteEndPoint, receivedBuffer.Skip(2).Take(receivedBytes - 2).ToArray());
                                            }
                                            break;
                                        case 3: //ACCEPT MY MESSAGE
                                            selectedHost.ReceivedAcceptId = receivedBuffer[1];
                                            break;
                                    }
                                }
                                state.Stop();
                            }
                        }
                        catch (Exception ex)
                        {
                            if (_live)
                            {
                                if (ex.HResult.Equals(-2147467259)) goto restartTask;
                                Exceptions?.Invoke(ex);
                                goto restartTask;
                            }
                        }
                    });
                });
                _serverThread.Name = "UDP Server thread";
                _serverThread.Priority = ThreadPriority.Highest;
                _serverThread.IsBackground = true;
                _serverThread.CurrentCulture = new CultureInfo("en-US");
                _serverThread.CurrentUICulture = new CultureInfo("en-US");
                _serverThread.Start();
            }
            catch (Exception ex)
            {
                Exceptions?.Invoke(ex);
                StopServer();
                StartListener(port);
            }
        }

        public void StopServer()
        {
            if (_serverThread == null) return;
            _live = false;
            _serverThread.Join(5000);
            try { _serverSocket.Close(); } catch { }
            try { _serverSocket.Dispose(); } catch { }
            _serverSocket = null;
            _serverThread = null;
            for (var i = 0; i < 3; i++) GC.Collect();
            Stopped?.Invoke();
        }
    }
}
