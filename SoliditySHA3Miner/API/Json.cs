﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SoliditySHA3Miner.API
{
    public class Json : IDisposable
    {
        public bool IsSupported { get; }

        private Miner.IMiner[] m_miners;
        private HttpListener m_Listener;
        private bool m_isOngoing;

        public Json(params Miner.IMiner[] miners)
        {
            IsSupported = HttpListener.IsSupported;
            if (!IsSupported)
            {
                Program.Print("[ERROR] Obsolete OS detected, JSON-API will not start.");
                return;
            }
            m_miners = miners;
        }

        public void Start(string apiBind)
        {
            m_isOngoing = false;

            var httpBind = apiBind.ToString();
            if (string.IsNullOrWhiteSpace(httpBind))
            {
                Program.Print("[INFO] minerJsonAPI is null or empty, using default...");
                httpBind = Defaults.JsonAPIPath;
            }
            else if (apiBind == "0")
            {
                Program.Print("[INFO] JSON-API is disabled.");
                return;
            }

            if (!httpBind.StartsWith("http://") || httpBind.StartsWith("https://")) httpBind = "http://" + httpBind;
            if (!httpBind.EndsWith("/")) httpBind += "/";

            if (!int.TryParse(httpBind.Split(':')[2].TrimEnd('/'), out int port))
            {
                Program.Print("[ERROR] Invalid port provided for JSON-API.");
                return;
            }

            var tempIPAddress = httpBind.Split(new string[]{ "//" }, StringSplitOptions.None)[1].Split(':')[0];
            if (!IPAddress.TryParse(tempIPAddress, out IPAddress ipAddress))
            {
                Program.Print("[ERROR] Invalid IP address provided for JSON-API.");
                return;
            }

            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                try { socket.Bind(new IPEndPoint(ipAddress, port)); }
                catch (Exception)
                {
                    Program.Print("[ERROR] JSON-API failed to bind to: " + apiBind);
                    return; 
                }
            };

            try
            {
                m_Listener = new HttpListener();
                m_Listener.Prefixes.Add(httpBind);

                Task.Run(() => Process(m_Listener));
                m_isOngoing = true;
            }
            catch (Exception ex)
            {
                Program.Print("[ERROR] An error has occured while starting JSON-API: " + ex.Message);
                return;
            }
        }

        public void Stop()
        {
            if (!m_isOngoing) return;
            try
            {
                m_isOngoing = false;
                Program.Print("[INFO] JSON-API service stopping...");
                m_Listener.Stop();
            }
            catch (Exception ex)
            {
                Program.Print("[ERROR] An error has occured while stopping JSON-API: " + ex.Message);
            }
        }

        private void Process(HttpListener listener)
        {
            listener.Start();
            Program.Print(string.Format("[INFO] JSON-API service started at {0}...", listener.Prefixes.ElementAt(0)));
            while (m_isOngoing)
            {
                HttpListenerResponse response = listener.GetContext().Response;
                
                var api = new JsonAPI();
                float divisor = 0;
                ulong totalHashRate = 0ul;
                foreach (var miner in m_miners)
                {
                    totalHashRate += miner.GetTotalHashrate();
                }

                var sTotalHashRate = totalHashRate.ToString();
                if (sTotalHashRate.Length >= 12)
                {
                    divisor = 1000000000000;
                    api.HashRateUnit = "TH/s";
                    api.TotalHashRate = totalHashRate / divisor;
                }
                else if (sTotalHashRate.Length >= 9)
                {
                    divisor = 1000000000;
                    api.HashRateUnit = "GH/s";
                    api.TotalHashRate = totalHashRate / divisor;
                }
                else if (sTotalHashRate.Length >= 6)
                {
                    divisor = 1000000;
                    api.HashRateUnit = "MH/s";
                    api.TotalHashRate = totalHashRate / divisor;
                }
                else if (sTotalHashRate.Length >= 3)
                {
                    divisor = 1000;
                    api.HashRateUnit = "KH/s";
                    api.TotalHashRate = totalHashRate / divisor;
                }
                
                foreach (var miner in m_miners)
                {
                    foreach (var device in miner.Devices.Where(d => d.DeviceID > -1))
                    {
                        var newMiner = new JsonAPI.Miner()
                        {
                            Type = device.Type,
                            DeviceID = device.DeviceID,
                            ModelName = device.Name,
                            HashRate = miner.GetHashrateByDevice(device.Platform, device.DeviceID) / divisor
                        };
                        api.Miners.Add(newMiner);
                    }
                }

                byte[] buffer = Encoding.UTF8.GetBytes(Utils.Json.SerializeFromObject(api));
                response.ContentLength64 = buffer.Length;

                using (var output = response.OutputStream)
                    if (buffer != null) output.Write(buffer, 0, buffer.Length);

                response.Close();
            }
        }

        public void Dispose()
        {
            m_isOngoing = false;
            if (m_Listener != null) m_Listener.Close();
        }

        public class JsonAPI
        {
            public DateTime SystemDateTime => DateTime.Now;
            public float TotalHashRate { get; set; }
            public string HashRateUnit { get; set; }
            public List<Miner> Miners { get; set; }

            public JsonAPI() => Miners = new List<Miner>();

            public class Miner
            {
                public string Type { get; set; }
                public int DeviceID { get; set; }
                public string ModelName { get; set; }
                public float HashRate { get; set; }
            }
        }
    }
}
