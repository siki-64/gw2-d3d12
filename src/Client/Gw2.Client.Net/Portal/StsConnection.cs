using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;

namespace Gw2.Client.Net.Portal
{
    public sealed class StsReply
    {
        public StsReply(int statusCode, string statusText, XElement? body)
        {
            StatusCode = statusCode;
            StatusText = statusText;
            Body = body;
        }

        public int StatusCode { get; }

        public string StatusText { get; }

        public XElement? Body { get; }

        public string? Element(string name) => Body?.Element(name)?.Value;
    }

    public sealed class StsConnection : IDisposable
    {
        public const int PortalPort = 6112;
        public const int RequestTimeoutMilliseconds = 29000;
        public const int ConnectConnType = 400;
        public const int ConnectProductType = 0;
        public const string ConnectProductName = "Gw2-64";
        public const int ConnectAppIndex = 1;
        public const int ConnectProgram = 101;
        public const int ConnectBuild = 1004;
        private const string StartTlsPath = "/Auth/StartTls";
        private const string HeaderEnd = "\r\n\r\n";
        private const int ReadBufferLength = 64 * 1024;

        private readonly TcpClient _tcp;
        private readonly string _hostname;
        private readonly Func<X509Certificate, bool> _acceptServerCertificate;
        private readonly List<byte> _pending = new List<byte>();
        private readonly byte[] _readBuffer = new byte[ReadBufferLength];
        private Stream _stream;
        private int _sequence;

        public StsConnection(string hostname, int port, Func<X509Certificate, bool> acceptServerCertificate)
        {
            _hostname = hostname;
            _acceptServerCertificate = acceptServerCertificate;
            _tcp = new TcpClient { NoDelay = true, ReceiveTimeout = RequestTimeoutMilliseconds };
            _tcp.Connect(hostname, port);
            _stream = _tcp.GetStream();
        }

        public Action<string>? Log { get; set; }

        public X509Certificate? ServerCertificate { get; private set; }

        public void Open()
        {
            SendConnect();
            StsReply startTls = Request(StartTlsPath, new XElement("Request"));
            if (startTls.StatusCode != 400) throw new InvalidOperationException($"StartTls answered {startTls.StatusCode} {startTls.StatusText}");
            UpgradeToTls();
        }

        public StsReply Request(string path, XElement body, int timeoutMilliseconds = RequestTimeoutMilliseconds)
        {
            int sequence = ++_sequence;
            _tcp.ReceiveTimeout = timeoutMilliseconds;
            byte[] bodyBytes = Encoding.UTF8.GetBytes(XmlText(body));
            string header = $"P {path} STS/1.0\r\ns:{sequence};timeout={timeoutMilliseconds}\r\nl:{bodyBytes.Length}\r\n\r\n";
            Write(Encoding.ASCII.GetBytes(header), bodyBytes);
            Log?.Invoke($"portal -> {path} s:{sequence} l:{bodyBytes.Length}");
            string expectedSequence = $"{sequence}R";
            while (true)
            {
                ReadMessage(out string startLine, out Dictionary<string, string> headers, out string bodyText);
                headers.TryGetValue("s", out string? replySequence);
                if (startLine.StartsWith("P ", StringComparison.Ordinal))
                {
                    Log?.Invoke($"portal <- push {startLine}");
                    continue;
                }
                if (replySequence != expectedSequence)
                {
                    Log?.Invoke($"portal <- unexpected reply s:{replySequence}");
                    continue;
                }
                StsReply reply = ParseReply(startLine, bodyText);
                Log?.Invoke($"portal <- {reply.StatusCode} {reply.StatusText} s:{replySequence} l:{bodyText.Length}");
                return reply;
            }
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _tcp.Dispose();
        }

        private void SendConnect()
        {
            var localEndPoint = (IPEndPoint)_tcp.Client.LocalEndPoint!;
            var connect = new XElement("Connect",
                new XElement("ConnType", ConnectConnType),
                new XElement("Address", localEndPoint.Address.MapToIPv4().ToString()),
                new XElement("ProductType", ConnectProductType),
                new XElement("ProductName", ConnectProductName),
                new XElement("AppIndex", ConnectAppIndex),
                new XElement("Epoch", DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                new XElement("Program", ConnectProgram),
                new XElement("Build", ConnectBuild),
                new XElement("Process", Process.GetCurrentProcess().Id));
            byte[] bodyBytes = Encoding.UTF8.GetBytes(XmlText(connect));
            Write(Encoding.ASCII.GetBytes($"P /Sts/Connect STS/1.0\r\nl:{bodyBytes.Length}\r\n\r\n"), bodyBytes);
            Log?.Invoke($"portal -> /Sts/Connect l:{bodyBytes.Length}");
        }

        private void UpgradeToTls()
        {
            if (_pending.Count != 0) throw new InvalidOperationException("Bytes left after StartTls reply");
            var tls = new SslStream(_stream, false, ValidateServerCertificate);
            tls.AuthenticateAsClient(_hostname, null, SslProtocols.Tls12, false);
            _stream = tls;
            Log?.Invoke($"portal tls {tls.SslProtocol}");
        }

        private bool ValidateServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
        {
            ServerCertificate = certificate;
            Log?.Invoke($"portal certificate {certificate?.Subject} issuer {certificate?.Issuer} errors {errors}");
            return certificate != null && _acceptServerCertificate(certificate);
        }

        private void Write(byte[] header, byte[] body)
        {
            _stream.Write(header, 0, header.Length);
            _stream.Write(body, 0, body.Length);
            _stream.Flush();
        }

        private void ReadMessage(out string startLine, out Dictionary<string, string> headers, out string bodyText)
        {
            int headerEnd;
            while ((headerEnd = IndexOfHeaderEnd()) < 0) Fill();
            string[] lines = Encoding.ASCII.GetString(_pending.GetRange(0, headerEnd).ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
            startLine = lines[0];
            headers = new Dictionary<string, string>();
            foreach (string line in lines.Skip(1))
            {
                int colon = line.IndexOf(':');
                if (colon > 0) headers[line.Substring(0, colon)] = line.Substring(colon + 1).Split(';')[0];
            }
            int bodyLength = headers.TryGetValue("l", out string? lengthText) ? int.Parse(lengthText) : 0;
            int messageLength = headerEnd + HeaderEnd.Length + bodyLength;
            while (_pending.Count < messageLength) Fill();
            bodyText = Encoding.UTF8.GetString(_pending.GetRange(headerEnd + HeaderEnd.Length, bodyLength).ToArray());
            _pending.RemoveRange(0, messageLength);
        }

        private int IndexOfHeaderEnd()
        {
            for (int index = 0; index + 3 < _pending.Count; index++)
            {
                if (_pending[index] == '\r' && _pending[index + 1] == '\n' && _pending[index + 2] == '\r' && _pending[index + 3] == '\n') return index;
            }
            return -1;
        }

        private void Fill()
        {
            int read = _stream.Read(_readBuffer, 0, _readBuffer.Length);
            if (read == 0) throw new IOException("Portal closed the connection");
            _pending.AddRange(new ArraySegment<byte>(_readBuffer, 0, read));
        }

        private static StsReply ParseReply(string startLine, string bodyText)
        {
            string[] parts = startLine.Split(new[] { ' ' }, 3);
            int statusCode = parts.Length > 1 && int.TryParse(parts[1], out int code) ? code : 0;
            string statusText = parts.Length > 2 ? parts[2] : "";
            XElement? body = string.IsNullOrWhiteSpace(bodyText) ? null : XElement.Parse(bodyText);
            return new StsReply(statusCode, statusText, body);
        }

        private static string XmlText(XElement element)
        {
            if (!element.HasElements && string.IsNullOrEmpty(element.Value)) return $"<{element.Name}/>\n";
            var text = new StringBuilder();
            text.Append('<').Append(element.Name).Append(">\n");
            foreach (XElement child in element.Elements())
            {
                if (child.HasElements) text.Append(XmlText(child));
                else if (string.IsNullOrEmpty(child.Value)) text.Append('<').Append(child.Name).Append("/>\n");
                else text.Append('<').Append(child.Name).Append('>').Append(System.Security.SecurityElement.Escape(child.Value)).Append("</").Append(child.Name).Append(">\n");
            }
            text.Append("</").Append(element.Name).Append(">\n");
            return text.ToString();
        }
    }
}
