using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using AstroTracker.Mount;
using AstroTracker.PlateSolving;

/// <summary>Checks the SynScan client against a stand-in for the app, and the precession maths.</summary>
static class MountTests
{
    public static bool Run()
    {
        bool ok = true;
        ok &= Report("precession", PrecessionMatchesStellarium());
        ok &= Report("sync then goto centres the target", SyncLoop().GetAwaiter().GetResult());
        ok &= Report("unknown command is reported", UnknownCommand().GetAwaiter().GetResult());
        ok &= Report("silent port is reported", SilentPort().GetAwaiter().GetResult());
        return ok;
    }

    static bool Report(string name, bool passed)
    {
        Console.WriteLine($"mount: {name}: {(passed ? "ok" : "FAILED")}");
        return passed;
    }

    /// <summary>M82: J2000 position against the "of date" position Stellarium showed on 2026-10-11.</summary>
    static bool PrecessionMatchesStellarium()
    {
        Vec3 j2000 = Vec3.FromRaDecDegrees((9 + 55 / 60.0 + 52.73 / 3600) * 15, 69 + 40 / 60.0 + 45.8 / 3600);
        Vec3 expected = Vec3.FromRaDecDegrees((9 + 58 / 60.0 + 4.0 / 3600) * 15, 69 + 32 / 60.0 + 54.9 / 3600);
        double year = Precession.JulianYear(new DateTime(2026, 10, 10, 23, 0, 0, DateTimeKind.Utc));

        Vec3 ofDate = Precession.FromJ2000(j2000, year);
        double error = ofDate.AngleTo(expected) * Angle.RadToArcsec;
        double roundTrip = Precession.ToJ2000(ofDate, year).AngleTo(j2000) * Angle.RadToArcsec;
        Console.WriteLine($"  year {year:F3}, differs from Stellarium by {error:F1} arcsec " +
                          $"(nutation and aberration are not modelled), round trip {roundTrip:E1} arcsec");
        return error < 40 && roundTrip < 1e-6;
    }

    static async Task<bool> SyncLoop()
    {
        using var app = new FakeSynScan { ErrorRaHours = -0.475, ErrorDecDeg = 5.54 };
        using var client = new SynScanClient("127.0.0.1", app.Port);

        bool ok = await client.GetAppVersion() == "2.5.30" && await client.GetMountConnection() == 1 &&
                  await client.CanSync() && await client.GetEquatorialSystem() == SynScanClient.EquatorialOfDate;

        const double targetRa = 9.9676, targetDec = 69.5486;
        await client.Slew(targetRa, targetDec);
        ok &= await client.IsSlewing();
        ok &= !await client.IsSlewing();
        (double ra, double dec) = await client.GetPosition();
        ok &= Math.Abs(ra - targetRa) < 1e-5 && Math.Abs(dec - targetDec) < 1e-5;
        double missBefore = Math.Abs(app.TrueDecDeg - targetDec);

        // A solved frame says where the telescope really is; pass that on and go again.
        await client.Sync(app.TrueRaHours, app.TrueDecDeg);
        await client.Slew(targetRa, targetDec);
        double missAfter = Math.Abs(app.TrueDecDeg - targetDec) + Math.Abs(app.TrueRaHours - targetRa);
        Console.WriteLine($"  miss in Dec before sync {missBefore:F2} deg, total miss after sync and goto {missAfter:E1}");
        return ok && missBefore > 5 && missAfter < 1e-5;
    }

    static async Task<bool> UnknownCommand()
    {
        using var app = new FakeSynScan();
        using var client = new SynScanClient("127.0.0.1", app.Port);
        try
        {
            await client.Send("NoSuchCommand");
            return false;
        }
        catch (SynScanException e)
        {
            return e.Status == "Unknown" && !e.NoAnswer;
        }
    }

    static async Task<bool> SilentPort()
    {
        int port;
        using (var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            port = ((IPEndPoint)probe.Client.LocalEndPoint).Port;

        using var client = new SynScanClient("127.0.0.1", port, 500);
        try
        {
            await client.GetPosition();
            return false;
        }
        catch (SynScanException e)
        {
            return e.NoAnswer;
        }
    }

    /// <summary>
    /// Answers like the SynScan app does according to "SynScan App Protocol" (2025-09-30). The telescope
    /// really points away from where the app believes by a fixed error until a sync removes it.
    /// </summary>
    sealed class FakeSynScan : IDisposable
    {
        readonly UdpClient _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        double _believedRa, _believedDec;
        bool _slewing;

        public double ErrorRaHours, ErrorDecDeg;
        public int Port => ((IPEndPoint)_udp.Client.LocalEndPoint).Port;
        public double TrueRaHours => _believedRa + ErrorRaHours;
        public double TrueDecDeg => _believedDec + ErrorDecDeg;

        public FakeSynScan()
        {
            _ = Serve();
        }

        public void Dispose() => _udp.Dispose();

        async Task Serve()
        {
            try
            {
                while (true)
                {
                    UdpReceiveResult request = await _udp.ReceiveAsync();
                    byte[] reply = Encoding.ASCII.GetBytes(Answer(Encoding.ASCII.GetString(request.Buffer)));
                    await _udp.SendAsync(reply, reply.Length, request.RemoteEndPoint);
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
        }

        string Answer(string command)
        {
            string[] parts = command.Split(',');
            CultureInfo plain = CultureInfo.InvariantCulture;
            switch (parts[0])
            {
                case "RightAscensionDeclinationGet":
                    return string.Format(plain, "Ok,{0},{1},{2}", parts[0], _believedRa, _believedDec);
                case "SlewToCoordinatesAsync":
                    _believedRa = double.Parse(parts[1], plain);
                    _believedDec = double.Parse(parts[2], plain);
                    _slewing = true;
                    return "Ok," + parts[0];
                case "SyncToCoordinates":
                    ErrorRaHours -= double.Parse(parts[1], plain) - _believedRa;
                    ErrorDecDeg -= double.Parse(parts[2], plain) - _believedDec;
                    _believedRa = double.Parse(parts[1], plain);
                    _believedDec = double.Parse(parts[2], plain);
                    return "Ok," + parts[0];
                case "SlewingGet":
                    // The first poll after a GoTo sees the mount moving, the next one sees it arrived.
                    bool slewing = _slewing;
                    _slewing = false;
                    return "Ok,SlewingGet," + (slewing ? "1" : "0");
                case "AbortSlew":
                    _slewing = false;
                    return "Ok,AbortSlew";
                case "CanSyncGet":
                    return "Ok,CanSyncGet,1";
                case "EquatorialSystemGet":
                    return "Ok,EquatorialSystemGet,1";
                case "Action" when parts.Length > 1 && parts[1] == "AppVersionGet":
                    return "Ok,Action,AppVersionGet,2.5.30";
                case "Action" when parts.Length > 1 && parts[1] == "ConnectedGet":
                    return "Ok,Action,ConnectedGet,1";
                default:
                    return "Unknown," + parts[0];
            }
        }
    }
}
