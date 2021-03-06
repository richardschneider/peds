﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Common.Logging;
using System.Net;
using System.Net.Sockets;

namespace Makaretu.Dns.Peds
{
    /// <summary>
    ///   A forwarding DNS server.
    /// </summary>
    /// <remarks>
    ///   Sends a DNS request to a recursive DNS server and returns the response.
    /// </remarks>
    class UdpServer : IDisposable
    {
        static ILog log = LogManager.GetLogger(typeof(UdpServer));

        ConcurrentDictionary<string, Message> outstandingRequests = new ConcurrentDictionary<string, Message>();
        List<UdpClient> listeners = new List<UdpClient>();

        /// <summary>
        ///   Something that can resolve a DNS query.
        /// </summary>
        /// <value>
        ///   A client to a recursive DNS Server.
        /// </value>
        public IDnsClient Resolver { get; set; }

        /// <summary>
        ///   The port to listen to.
        /// </summary>
        /// <value>
        ///   Defaults to 53.
        /// </value>
        public int Port { get; set; } = 53;

        public void Start()
        {
            foreach (var address in Addresses)
            {
                var endPoint = new IPEndPoint(address, Port);
                var listener = new UdpClient(endPoint);
                listeners.Add(listener);
                ReadRequests(listener);
            }
        }

        /// <summary>
        ///   The addresses of the server.
        /// </summary>
        /// <value>
        ///   Defaults to <see cref="IPAddress.IPv6Loopback"/> and <see cref="IPAddress.Loopback"/>.
        /// </value>
        public IEnumerable<IPAddress> Addresses { get; set; } = new[] 
        {
            IPAddress.IPv6Loopback,
            IPAddress.Loopback
        };

        public void Dispose()
        {
            foreach (var listener in listeners)
            {
                listener.Dispose();
            }
            listeners.Clear();
        }

        async void ReadRequests(UdpClient listener)
        {
            while (true)
            {
                try
                {
                    var request = await listener.ReceiveAsync();
                    Process(request, listener);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset)
                {
                    // eat it.
                }
                catch (Exception e)
                {
                    log.Error(e);
                }
            }
        }

        async void Process(UdpReceiveResult request, UdpClient listener)
        {
            try
            {
                long startTime = 0;
                long endTime = 0;
                if (Performance.Instance.Enabled)
                {
                    Performance.QueryPerformanceCounter(ref startTime);
                    Performance.Instance.RequestCount.Increment();
                    Performance.Instance.RequestCountPerSecond.Increment();
                }
                var query = (Message)new Message().Read(request.Buffer);

                // Check for a duplicate request.
                var qid = query.Id.ToString() + "-" + request.RemoteEndPoint.ToString();
                if (!outstandingRequests.TryAdd(qid, query))
                    return;

                try
                {
                    // Need a unique query ID
                    var originalQueryId = query.Id;
                    query.Id = Resolver.NextQueryId();

                    // Get a response.
                    var response = await Resolver.QueryAsync(query);
                    response.Id = originalQueryId;
                    if (Performance.Instance.Enabled)
                    {
                        Performance.QueryPerformanceCounter(ref endTime);
                        Performance.Instance.AvgResolveTime.IncrementBy(endTime - startTime);
                        Performance.Instance.AvgResolveTimeBase.Increment();
                    }

                    var responseBytes = response.ToByteArray();
                    await listener.SendAsync(responseBytes, responseBytes.Length, request.RemoteEndPoint);
                }
                finally
                {
                    outstandingRequests.TryRemove(qid, out Message _);
                }
            }
            catch (Exception e)
            {
                log.Error(e);
            }
        }

    }
}
