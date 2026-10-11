using System;
using System.Threading.Tasks;
using AstroTracker.Mount;
using AstroTracker.PlateSolving;
using UnityEngine;
using UnityEngine.UIElements;
using Angle = AstroTracker.PlateSolving.Angle;

namespace AstroTracker
{
    /// <summary>
    /// The mount block of the solve screen: sends the target and the solved position to the SynScan app.
    /// Positions come in and go out as J2000 directions; conversion to the app's own system happens here.
    /// </summary>
    sealed class MountPanel
    {
        const int SlewPollMs = 1000;
        const int SlewTimeoutSeconds = 300;

        readonly Label _title, _status;
        readonly TextField _address;
        readonly Button _check, _goTo, _sync, _stop;
        readonly Func<Vec3?> _target, _solved;
        readonly Action _mountMoved;

        SynScanClient _client;
        string _clientAddress;
        int? _equatorialSystem;
        bool _busy, _stopRequested;
        Func<string> _statusText;

        /// <param name="target">Target from the screen's fields, or null when they do not hold valid coordinates.</param>
        /// <param name="solved">Centre of the last solved frame, or null when there is none for the current pointing.</param>
        /// <param name="mountMoved">Called when a GoTo starts: the last solved frame no longer shows where the telescope points.</param>
        public MountPanel(VisualElement root, Func<Vec3?> target, Func<Vec3?> solved, Action mountMoved)
        {
            _target = target;
            _solved = solved;
            _mountMoved = mountMoved;

            _title = root.Q<Label>("mount-title");
            _status = root.Q<Label>("mount-status");
            _address = root.Q<TextField>("mount-address");
            _check = root.Q<Button>("mount-check");
            _goTo = root.Q<Button>("mount-goto");
            _sync = root.Q<Button>("mount-sync");
            _stop = root.Q<Button>("mount-stop");

            _check.clickable = new Clickable(() => Run(Check));
            _goTo.clickable = new Clickable(() => Run(GoTo));
            _sync.clickable = new Clickable(() => Run(Sync));
            _stop.clickable = new Clickable(Stop);
            Refresh();
        }

        public void Dispose()
        {
            _stopRequested = true;
            _client?.Dispose();
            _client = null;
        }

        public void ApplyLanguage()
        {
            _title.text = Localization.Get("mountTitle");
            _address.label = Localization.Get("mountAddress");
            _check.text = Localization.Get("mountCheck");
            _goTo.text = Localization.Get("mountGoTo");
            _sync.text = Localization.Get("mountSync");
            _stop.text = Localization.Get("mountStop");
            _status.text = _statusText?.Invoke() ?? "";
        }

        /// <summary>Updates which buttons can be pressed; call when a frame gets solved or discarded.</summary>
        public void Refresh()
        {
            _check.SetEnabled(!_busy);
            _goTo.SetEnabled(!_busy);
            _sync.SetEnabled(!_busy && _solved() != null);
        }

        void ShowStatus(Func<string> text)
        {
            _statusText = text;
            _status.text = text();
        }

        /// <summary>Runs one mount operation, keeping the other buttons off meanwhile and reporting failures.</summary>
        async void Run(Func<SynScanClient, Task> operation)
        {
            if (_busy)
                return;
            SynScanClient client = Client();
            if (client == null)
                return;

            _busy = true;
            _stopRequested = false;
            Refresh();
            try
            {
                await operation(client);
            }
            catch (SynScanException e) when (e.NoAnswer)
            {
                string address = _clientAddress;
                ShowStatus(() => string.Format(Localization.Get("mountNoAnswer"), address));
            }
            catch (SynScanException e)
            {
                string reason = e.Message;
                ShowStatus(() => string.Format(Localization.Get("mountRefused"), reason));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                string reason = e.Message;
                ShowStatus(() => Localization.Get("error") + reason);
            }
            finally
            {
                _busy = false;
                Refresh();
            }
        }

        /// <summary>The client for the address in the field, recreated when the address changes.</summary>
        SynScanClient Client()
        {
            string address = _address.value.Trim();
            if (_client != null && address == _clientAddress)
                return _client;

            string host = address;
            int port = SynScanClient.DefaultPort;
            int colon = address.LastIndexOf(':');
            if (colon > 0)
            {
                host = address.Substring(0, colon);
                if (!int.TryParse(address.Substring(colon + 1), out port) || port <= 0 || port > 65535)
                    host = "";
            }

            try
            {
                if (host.Length == 0)
                    throw new FormatException();
                var client = new SynScanClient(host, port);
                _client?.Dispose();
                _client = client;
                _clientAddress = address;
                _equatorialSystem = null;
                return client;
            }
            catch (Exception)
            {
                ShowStatus(() => Localization.Get("mountBadAddress"));
                return null;
            }
        }

        async Task Check(SynScanClient client)
        {
            // Older app versions do not know these two; the position query below is the real test.
            string version = "";
            int connection = 1;
            try
            {
                version = await client.GetAppVersion();
                connection = await client.GetMountConnection();
            }
            catch (SynScanException e) when (!e.NoAnswer)
            {
            }

            if (connection != 1)
            {
                ShowStatus(() => Localization.Get("mountNotConnected"));
                return;
            }

            (double raHours, double decDeg) = await client.GetPosition();
            int system = await EquatorialSystem(client);
            Vec3 position = ToJ2000(Vec3.FromRaDecDegrees(raHours * 15, decDeg), system);
            ShowStatus(() => string.Format(Localization.Get("mountInfo"), version,
                SkyCoordinates.FormatRa(position.Ra * Angle.RadToDeg),
                SkyCoordinates.FormatDec(position.Dec * Angle.RadToDeg)));
        }

        async Task GoTo(SynScanClient client)
        {
            Vec3? target = _target();
            if (target == null)
            {
                ShowStatus(() => Localization.Get("mountNoTarget"));
                return;
            }

            Vec3 mount = FromJ2000(target.Value, await EquatorialSystem(client));
            await client.Slew(mount.Ra * Angle.RadToDeg / 15, mount.Dec * Angle.RadToDeg);
            _mountMoved();
            ShowStatus(() => Localization.Get("mountSlewing"));

            for (int waited = 0; waited < SlewTimeoutSeconds * 1000; waited += SlewPollMs)
            {
                await Task.Delay(SlewPollMs);
                if (_stopRequested)
                    return;
                if (!await client.IsSlewing())
                {
                    ShowStatus(() => Localization.Get("mountArrived"));
                    return;
                }
            }
        }

        async Task Sync(SynScanClient client)
        {
            Vec3? solved = _solved();
            if (solved == null)
            {
                ShowStatus(() => Localization.Get("mountNeedSolve"));
                return;
            }
            if (!await client.CanSync())
            {
                ShowStatus(() => Localization.Get("mountNoSync"));
                return;
            }

            Vec3 mount = FromJ2000(solved.Value, await EquatorialSystem(client));
            await client.Sync(mount.Ra * Angle.RadToDeg / 15, mount.Dec * Angle.RadToDeg);
            ShowStatus(() => Localization.Get("mountSynced"));
        }

        /// <summary>Stop works at any moment, including during another operation.</summary>
        async void Stop()
        {
            SynScanClient client = Client();
            if (client == null)
                return;
            _stopRequested = true;
            try
            {
                await client.Abort();
                ShowStatus(() => Localization.Get("mountStopped"));
            }
            catch (SynScanException e)
            {
                string address = _clientAddress, reason = e.Message;
                ShowStatus(() => e.NoAnswer
                    ? string.Format(Localization.Get("mountNoAnswer"), address)
                    : string.Format(Localization.Get("mountRefused"), reason));
            }
        }

        /// <summary>Asks once per connection which coordinates the app works in; assumes "of date" if it will not say.</summary>
        async Task<int> EquatorialSystem(SynScanClient client)
        {
            if (_equatorialSystem == null)
            {
                try
                {
                    _equatorialSystem = await client.GetEquatorialSystem();
                }
                catch (SynScanException e) when (!e.NoAnswer)
                {
                    _equatorialSystem = SynScanClient.EquatorialOfDate;
                }
            }
            return _equatorialSystem.Value;
        }

        static Vec3 FromJ2000(Vec3 direction, int system)
        {
            return system == SynScanClient.EquatorialJ2000
                ? direction
                : Precession.FromJ2000(direction, Precession.JulianYear(DateTime.UtcNow));
        }

        static Vec3 ToJ2000(Vec3 direction, int system)
        {
            return system == SynScanClient.EquatorialJ2000
                ? direction
                : Precession.ToJ2000(direction, Precession.JulianYear(DateTime.UtcNow));
        }
    }
}
