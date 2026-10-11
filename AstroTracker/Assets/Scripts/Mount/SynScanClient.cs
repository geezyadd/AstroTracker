using System;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AstroTracker.Mount
{
    /// <summary>The SynScan app answered with something other than "Ok", or did not answer at all.</summary>
    public sealed class SynScanException : Exception
    {
        /// <summary>Response status from the app ("Error", "Unknown", "Unimplemented", ...), or null when it stayed silent.</summary>
        public string Status { get; }

        public bool NoAnswer => Status == null;

        public SynScanException(string status, string message) : base(message)
        {
            Status = status;
        }
    }

    /// <summary>
    /// Talks to the Sky-Watcher SynScan app over its "SynScan App Protocol": plain text commands named after
    /// the ASCOM ITelescopeV3 members, one UDP datagram each way. The app keeps driving the mount; this only
    /// tells it where to go and where the telescope really points.
    /// </summary>
    public sealed class SynScanClient : IDisposable
    {
        public const int DefaultPort = 11881;

        /// <summary>EquatorialSystemGet value for coordinates of the current date (JNow).</summary>
        public const int EquatorialOfDate = 1;
        /// <summary>EquatorialSystemGet value for J2000 coordinates.</summary>
        public const int EquatorialJ2000 = 2;

        readonly UdpClient _udp;
        readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        readonly int _timeoutMs;
        Task<UdpReceiveResult> _pending;

        public string Host { get; }
        public int Port { get; }

        /// <param name="timeoutMs">The protocol allows the app up to about 800 ms on poor Wi-Fi.</param>
        public SynScanClient(string host = "127.0.0.1", int port = DefaultPort, int timeoutMs = 2000)
        {
            Host = host;
            Port = port;
            _timeoutMs = timeoutMs;
            _udp = new UdpClient();
            _udp.Connect(host, port);
        }

        public void Dispose() => _udp.Dispose();

        /// <summary>Sends one command and returns the arguments of its "Ok" response.</summary>
        public async Task<string[]> Send(string command)
        {
            await _gate.WaitAsync();
            try
            {
                byte[] bytes = Encoding.ASCII.GetBytes(command);
                string name = command.Split(',')[0];
                Task timeout = Task.Delay(_timeoutMs);
                try
                {
                    await _udp.SendAsync(bytes, bytes.Length);
                    while (true)
                    {
                        _pending ??= _udp.ReceiveAsync();
                        if (await Task.WhenAny(_pending, timeout) != _pending)
                            throw new SynScanException(null, "No answer from the SynScan app");

                        Task<UdpReceiveResult> received = _pending;
                        _pending = null;
                        string[] parts = Encoding.ASCII.GetString((await received).Buffer).Trim().Split(',');

                        // A late answer to an earlier command that timed out: keep waiting for ours.
                        if (parts.Length < 2 || parts[1] != name)
                            continue;
                        if (parts[0] != "Ok")
                            throw new SynScanException(parts[0], $"{name}: {parts[0]}");

                        var arguments = new string[parts.Length - 2];
                        Array.Copy(parts, 2, arguments, 0, arguments.Length);
                        return arguments;
                    }
                }
                catch (SocketException)
                {
                    // Nothing listens on the port: the system reports it on the next socket call.
                    _pending = null;
                    throw new SynScanException(null, "No answer from the SynScan app");
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Where the app believes the telescope points: RA in hours, Dec in degrees.</summary>
        public async Task<(double raHours, double decDeg)> GetPosition()
        {
            string[] values = await Send("RightAscensionDeclinationGet");
            return (ParseDouble(values, 0), ParseDouble(values, 1));
        }

        /// <summary>Starts a GoTo and returns at once; poll <see cref="IsSlewing"/> for the end.</summary>
        public Task Slew(double raHours, double decDeg) => Send("SlewToCoordinatesAsync," + Format(raHours, decDeg));

        /// <summary>Tells the app the telescope points at these coordinates right now.</summary>
        public Task Sync(double raHours, double decDeg) => Send("SyncToCoordinates," + Format(raHours, decDeg));

        public Task Abort() => Send("AbortSlew");

        public async Task<bool> IsSlewing() => ParseInt(await Send("SlewingGet"), 0) != 0;

        public async Task<bool> CanSync() => ParseInt(await Send("CanSyncGet"), 0) != 0;

        /// <summary><see cref="EquatorialOfDate"/>, <see cref="EquatorialJ2000"/> or another ASCOM value.</summary>
        public async Task<int> GetEquatorialSystem() => ParseInt(await Send("EquatorialSystemGet"), 0);

        /// <summary>Whether the app is linked to the mount: 0 no, 1 yes, 2 connecting. SynScan 2.5.30 or newer.</summary>
        public async Task<int> GetMountConnection() => ParseInt(await Send("Action,ConnectedGet"), 1);

        /// <summary>App version such as "2.5.30". SynScan 2.5.30 or newer.</summary>
        public async Task<string> GetAppVersion()
        {
            string[] values = await Send("Action,AppVersionGet");
            return values.Length > 1 ? values[1] : "";
        }

        static string Format(double raHours, double decDeg)
        {
            return raHours.ToString("F6", CultureInfo.InvariantCulture) + "," +
                   decDeg.ToString("F6", CultureInfo.InvariantCulture);
        }

        static double ParseDouble(string[] values, int index)
        {
            if (index < values.Length && double.TryParse(values[index], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double value))
                return value;
            throw new SynScanException("Error", "Unexpected answer from the SynScan app");
        }

        static int ParseInt(string[] values, int index) => (int)Math.Round(ParseDouble(values, index));
    }
}
