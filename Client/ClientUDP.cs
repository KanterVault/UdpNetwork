using System;
using System.Net;
using System.Linq;
using System.Threading;
using System.Net.Sockets;
using System.Globalization;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace Client
{
    public class ClientUDP
    {
        public Action Connected;
        public Action Disconnected;
        public Action<Exception> Exceptions;
        public Action<byte[]> Send;
        public Action<byte[]> SendGuaranteed;
        public Action<ArraySegment<byte>> Received;
        public Action<ArraySegment<byte>> ReceivedGuaranteed;

        private struct SendedMessage
        {
            public byte[] Buffer;
            public byte Id;
        }

        private Thread _clientThread = null;
        private Socket _clientSocket = null;
        private bool _live = false;
        public void StartClient(IPEndPoint connectTo)
        {
            try
            {
                if (this._clientThread != null) return;
                _live = true;
                _clientThread = new Thread(() =>
                {
                    _clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    Connected?.Invoke();

                    _clientSocket.SendTimeout = 2000;
                    _clientSocket.ReceiveTimeout = 2000;

                    _clientSocket.SendBufferSize = 32768;
                    _clientSocket.ReceiveBufferSize = 32768;

                    _clientSocket.Connect(connectTo);

                    try
                    {
                        Send = (buffer) =>
                        {
                            if (!_live) return;
                            _clientSocket.SendAsync(new byte[] { 1, 0 }.Concat(buffer).ToArray(), SocketFlags.None);
                        };
                    }
                    catch (Exception ex)
                    {
                        Exceptions?.Invoke(ex);
                    }

                    var packagesToSend = new ConcurrentQueue<SendedMessage>();
                    byte messageId = 0;
                    byte receivedAcceptId = 0;
                    var aliveTime = DateTime.Now.AddSeconds(4);
                    try
                    {
                        SendGuaranteed = (buffer) =>
                        {
                            if (!_live) return;
                            messageId++;
                            var pack = new SendedMessage()
                            {
                                Id = messageId,
                                Buffer = buffer
                            };
                            packagesToSend.Enqueue(pack);
                        };
                    }
                    catch (Exception ex)
                    {
                        Exceptions?.Invoke(ex);
                    }

                    Parallel.For(0, 2, (id, state) =>
                    {
                    restartTask:
                        try
                        {
                            switch (id)
                            {
                                case 0:
                                    while (_live)
                                    {
                                        if (packagesToSend.TryDequeue(out SendedMessage buffer))
                                        {
                                            var date = DateTime.Now.AddMilliseconds(500.0f);
                                            _clientSocket.SendAsync(new byte[] { 2, buffer.Id }.Concat(buffer.Buffer).ToArray(), SocketFlags.None);
                                            while (_live)
                                            {
                                                if (receivedAcceptId == buffer.Id) break;
                                                if (date < DateTime.Now)
                                                {
                                                    _clientSocket.SendAsync(new byte[] { 2, buffer.Id }.Concat(buffer.Buffer).ToArray(), SocketFlags.None);
                                                    date = DateTime.Now.AddMilliseconds(500.0f);
                                                }
                                            }
                                        }
                                    }
                                    state.Stop();
                                    break;
                                case 1:
                                    byte lastReceivedGuarantedId = 0;
                                    while (_live)
                                    {
                                        if (aliveTime < DateTime.Now)
                                        {
                                            Disconnected?.Invoke();
                                            StopClient();
                                            break;
                                        }

                                        var receivedBuffer = new byte[_clientSocket.ReceiveBufferSize];
                                        var receivedBytes = _clientSocket.Receive(receivedBuffer);
                                        if (!_live) state.Stop();
                                        aliveTime = DateTime.Now.AddSeconds(4);
                                       
                                        switch (receivedBuffer[0])
                                        {
                                            case 1: //SIMPLE RECEIVED
                                                if (_live) Received?.Invoke(receivedBuffer.Skip(2).Take(receivedBytes - 2).ToArray());
                                                break;
                                            case 2: //RECEIVED GUARANTED MESSAGE
                                                if (lastReceivedGuarantedId == receivedBuffer[1])
                                                {
                                                    _clientSocket.SendAsync(new byte[] { 3, receivedBuffer[1] }, SocketFlags.None);
                                                }
                                                else
                                                {
                                                    lastReceivedGuarantedId = receivedBuffer[1];
                                                    _clientSocket.SendAsync(new byte[] { 3, receivedBuffer[1] }, SocketFlags.None);
                                                    if (_live) ReceivedGuaranteed?.Invoke(receivedBuffer.Skip(2).Take(receivedBytes - 2).ToArray());
                                                }
                                                break;
                                            case 3: //ACCEPT MY MESSAGE
                                                receivedAcceptId = receivedBuffer[1];
                                                break;
                                        }
                                    }
                                    state.Stop();
                                    break;
                            }
                        }
                        catch (Exception ex)
                        {
                            if (ex.HResult.Equals(-2147467259)) goto restartTask;
                            Exceptions?.Invoke(ex);
                            goto restartTask;
                        }
                    });
                });
                _clientThread.Name = "UDP Client thread";
                _clientThread.Priority = ThreadPriority.Highest;
                _clientThread.IsBackground = false;
                _clientThread.CurrentCulture = new CultureInfo("en-US");
                _clientThread.CurrentUICulture = new CultureInfo("en-US");
                _clientThread.Start();
            }
            catch (Exception ex)
            {
                Exceptions?.Invoke(ex);
            }
        }

        public void StopClient()
        {
            if (_clientThread == null) return;
            _live = false;
            _clientThread.Join(5000);
            try { _clientSocket.Close(); } catch { }
            try { _clientSocket.Dispose(); } catch { }
            _clientSocket = null;
            _clientThread = null;
            for (var i = 0; i < 3; i++) GC.Collect();
        }
    }
}
