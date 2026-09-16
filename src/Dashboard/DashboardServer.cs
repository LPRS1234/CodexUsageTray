using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexUsageTray
{
    internal sealed class DashboardServer : IDisposable
    {
        private readonly string _dashboardPath;
        private readonly string _dashboardStylesPath;
        private readonly string _dashboardScriptPath;
        private readonly string _logoPath;
        private readonly Func<DashboardState> _getState;
        private readonly Action _requestRefresh;
        private readonly object _gate = new object();
        private TcpListener _listener;
        private int _port;
        private volatile bool _disposed;

        public string Url
        {
            get { return "http://127.0.0.1:" + _port.ToString(CultureInfo.InvariantCulture) + "/"; }
        }

        public DashboardServer(string dashboardPath, string logoPath,
            Func<DashboardState> getState, Action requestRefresh)
        {
            _dashboardPath = dashboardPath;
            string assetDirectory = Path.GetDirectoryName(dashboardPath);
            _dashboardStylesPath = Path.Combine(assetDirectory, "dashboard.css");
            _dashboardScriptPath = Path.Combine(assetDirectory, "dashboard.js");
            _logoPath = logoPath;
            _getState = getState;
            _requestRefresh = requestRefresh;
        }

        public void EnsureStarted()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException("DashboardServer");
                }

                if (_listener != null)
                {
                    return;
                }

                if (!File.Exists(_dashboardPath))
                {
                    throw new FileNotFoundException("대시보드 화면 파일을 찾을 수 없습니다.", _dashboardPath);
                }
                if (!File.Exists(_dashboardStylesPath) || !File.Exists(_dashboardScriptPath))
                {
                    throw new FileNotFoundException("대시보드 리소스 파일을 찾을 수 없습니다.");
                }

                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start(8);
                _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                Task.Run(new Func<Task>(ListenLoopAsync));
            }
        }

        private async Task ListenLoopAsync()
        {
            while (!_disposed)
            {
                TcpClient client = null;
                try
                {
                    client = await _listener.AcceptTcpClientAsync();
                    TcpClient acceptedClient = client;
                    client = null;
                    Task ignored = Task.Run(() => HandleClient(acceptedClient));
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    if (_disposed)
                    {
                        break;
                    }
                }
                finally
                {
                    if (client != null)
                    {
                        client.Dispose();
                    }
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 5000;
                    client.SendTimeout = 5000;
                    using (NetworkStream stream = client.GetStream())
                    using (StreamReader reader = new StreamReader(
                        stream, Encoding.ASCII, false, 1024, true))
                    {
                        string requestLine = reader.ReadLine();
                        if (string.IsNullOrWhiteSpace(requestLine))
                        {
                            return;
                        }

                        int headerCount = 0;
                        string header;
                        do
                        {
                            header = reader.ReadLine();
                            headerCount++;
                        }
                        while (!string.IsNullOrEmpty(header) && headerCount < 64);

                        string[] parts = requestLine.Split(' ');
                        if (parts.Length < 2)
                        {
                            WriteResponse(stream, 400, "Bad Request", "text/plain; charset=utf-8",
                                Encoding.UTF8.GetBytes("잘못된 요청입니다."));
                            return;
                        }

                        string method = parts[0];
                        string target = parts[1];
                        int queryIndex = target.IndexOf('?');
                        string path = queryIndex >= 0 ? target.Substring(0, queryIndex) : target;

                        RouteRequest(stream, method, path);
                    }
                }
                catch
                {
                    // A browser may close speculative connections before a response is written.
                }
            }
        }

        private void RouteRequest(Stream stream, string method, string path)
        {
            if (TryWriteStaticFile(stream, path))
            {
                return;
            }

            if (string.Equals(path, "/api/snapshot", StringComparison.Ordinal))
            {
                DashboardState state = _getState();
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                byte[] body = Encoding.UTF8.GetBytes(serializer.Serialize(state.ToDictionary()));
                WriteResponse(stream, 200, "OK", "application/json; charset=utf-8", body);
                return;
            }

            if (string.Equals(path, "/api/refresh", StringComparison.Ordinal) &&
                string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                _requestRefresh();
                WriteResponse(stream, 202, "Accepted", "application/json; charset=utf-8",
                    Encoding.UTF8.GetBytes("{\"accepted\":true}"));
                return;
            }

            if (string.Equals(path, "/favicon.ico", StringComparison.Ordinal))
            {
                WriteResponse(stream, 204, "No Content", "image/x-icon", new byte[0]);
                return;
            }

            WriteResponse(stream, 404, "Not Found", "text/plain; charset=utf-8",
                Encoding.UTF8.GetBytes("페이지를 찾을 수 없습니다."));
        }

        private bool TryWriteStaticFile(Stream stream, string path)
        {
            string filePath = null;
            string contentType = null;
            if (string.Equals(path, "/", StringComparison.Ordinal))
            {
                filePath = _dashboardPath;
                contentType = "text/html; charset=utf-8";
            }
            else if (string.Equals(path, "/dashboard.css", StringComparison.Ordinal))
            {
                filePath = _dashboardStylesPath;
                contentType = "text/css; charset=utf-8";
            }
            else if (string.Equals(path, "/dashboard.js", StringComparison.Ordinal))
            {
                filePath = _dashboardScriptPath;
                contentType = "application/javascript; charset=utf-8";
            }
            else if (string.Equals(path, "/logo.png", StringComparison.Ordinal))
            {
                filePath = _logoPath;
                contentType = "image/png";
            }

            if (filePath == null)
            {
                return false;
            }

            if (!File.Exists(filePath))
            {
                WriteResponse(stream, 404, "Not Found", "text/plain; charset=utf-8",
                    Encoding.UTF8.GetBytes("리소스 파일을 찾을 수 없습니다."));
                return true;
            }

            WriteResponse(stream, 200, "OK", contentType, File.ReadAllBytes(filePath));
            return true;
        }

        private static void WriteResponse(Stream stream, int statusCode, string statusText,
            string contentType, byte[] body)
        {
            string headers = "HTTP/1.1 " + statusCode.ToString(CultureInfo.InvariantCulture) + " " +
                statusText + "\r\n" +
                "Content-Type: " + contentType + "\r\n" +
                "Content-Length: " + body.Length.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                "Cache-Control: no-store\r\n" +
                "Content-Security-Policy: default-src 'self'; img-src 'self' data:; " +
                "style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self'\r\n" +
                "Referrer-Policy: no-referrer\r\n" +
                "X-Content-Type-Options: nosniff\r\n" +
                "Connection: close\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
            stream.Write(headerBytes, 0, headerBytes.Length);
            if (body.Length > 0)
            {
                stream.Write(body, 0, body.Length);
            }
            stream.Flush();
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                if (_listener != null)
                {
                    _listener.Stop();
                    _listener = null;
                }
            }
        }
    }

}
