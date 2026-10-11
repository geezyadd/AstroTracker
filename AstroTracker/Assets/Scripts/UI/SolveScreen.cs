using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AstroTracker.PlateSolving;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;
using Angle = AstroTracker.PlateSolving.Angle;

namespace AstroTracker
{
    /// <summary>
    /// The app's first screen: pick a camera frame, enter the target, see where the telescope really points.
    /// Creates itself on startup, so the scene needs no setup.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class SolveScreen : MonoBehaviour
    {
        const int Binning = 2;
        const int MaxMatches = 6;
        const double WideSearchRadiusDeg = 10;
        const int MaxObjectsInFrame = 8;
        const string ActiveLanguageClass = "language-active";

        Button _pickButton, _cameraButton, _solveButton;
        Button[] _languageButtons;
        Label _fileLabel, _resultLabel;
        TextField _searchField, _raField, _decField, _radiusField, _scaleField;
        VisualElement _matches;
        ObjectCatalog _objects;
        MountPanel _mount;
        CameraPanel _camera;

        string _imagePath, _imageName;
        bool _busy;
        /// <summary>Set while a whole-sky search runs; the Solve button then stops it.</summary>
        CancellationTokenSource _wideSearch;
        StarCatalog _catalog;
        SolverIndex _index;
        /// <summary>Produces the text under the Solve button, so it can be rewritten in another language.</summary>
        Func<string> _result;
        /// <summary>Centre of the last solved frame (J2000) while the telescope has not been moved since.</summary>
        Vec3? _solvedCenter;
        /// <summary>Centre of the most recent solved frame, kept as the place to start looking for the next one.</summary>
        Vec3? _lastSolved;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateOnStartup()
        {
            if (FindFirstObjectByType<SolveScreen>() != null)
                return;

            // UI Toolkit takes its input through an EventSystem when only the Input System package is active.
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem");
                events.SetActive(false);
                events.AddComponent<EventSystem>();
                events.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
                events.SetActive(true);
            }

            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/RuntimeTheme");
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1000, 700);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;

            var screen = new GameObject("SolveScreen");
            var document = screen.AddComponent<UIDocument>();
            document.panelSettings = panel;
            document.visualTreeAsset = Resources.Load<VisualTreeAsset>("UI/SolveScreen");
            screen.AddComponent<SolveScreen>();
        }

        void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            _pickButton = root.Q<Button>("pick");
            _cameraButton = root.Q<Button>("camera");
            _solveButton = root.Q<Button>("solve");
            _fileLabel = root.Q<Label>("file");
            _resultLabel = root.Q<Label>("result");
            _searchField = root.Q<TextField>("search");
            _matches = root.Q<VisualElement>("matches");
            _raField = root.Q<TextField>("ra");
            _decField = root.Q<TextField>("dec");
            _radiusField = root.Q<TextField>("radius");
            _scaleField = root.Q<TextField>("scale");

            // In the order of the Language enum.
            _languageButtons = new[]
            {
                root.Q<Button>("language-en"), root.Q<Button>("language-ru"), root.Q<Button>("language-uk"),
            };
            for (int i = 0; i < _languageButtons.Length; i++)
            {
                var language = (Language)i;
                _languageButtons[i].clickable = new Clickable(() => Localization.Current = language);
            }

            foreach (TextField field in new[] { _raField, _decField, _radiusField, _scaleField })
                field.keyboardType = TouchScreenKeyboardType.NumbersAndPunctuation;

            _objects ??= new ObjectCatalog(Resources.Load<TextAsset>("ObjectCatalog/objects").text,
                Resources.Load<TextAsset>("ObjectCatalog/stars").text);
            _mount = new MountPanel(root, CurrentTarget, () => _solvedCenter, () => _solvedCenter = null);
            _camera = new CameraPanel(root, SetImage);
            _cameraButton.clickable = new Clickable(_camera.Open);
            _cameraButton.style.display = CameraLink.Supported ? DisplayStyle.Flex : DisplayStyle.None;
            _searchField.RegisterValueChangedCallback(OnSearchChanged);
            _pickButton.clicked += PickImage;
            _solveButton.clicked += Solve;
            Localization.Changed += ApplyLanguage;
            ApplyLanguage();
            UpdateButtons();
        }

        void OnDisable()
        {
            Localization.Changed -= ApplyLanguage;
            _mount.Dispose();
            _searchField.UnregisterValueChangedCallback(OnSearchChanged);
            _pickButton.clicked -= PickImage;
            _solveButton.clicked -= Solve;
        }

        /// <summary>Rewrites every text on the screen in the current language.</summary>
        void ApplyLanguage()
        {
            _pickButton.text = Localization.Get("pick");
            _cameraButton.text = Localization.Get("fromCamera");
            _camera.ApplyLanguage();
            _solveButton.text = Localization.Get(_wideSearch != null ? "cancel" : "solve");
            _searchField.label = Localization.Get("object");
            _raField.label = Localization.Get("targetRa");
            _decField.label = Localization.Get("targetDec");
            _raField.textEdition.placeholder = Localization.Get("optional");
            _decField.textEdition.placeholder = Localization.Get("optional");
            _radiusField.label = Localization.Get("radius");
            _scaleField.label = Localization.Get("scale");
            _fileLabel.text = _imageName ?? Localization.Get("noPhoto");
            _resultLabel.text = _result?.Invoke() ?? "";
            _mount.ApplyLanguage();

            for (int i = 0; i < _languageButtons.Length; i++)
                _languageButtons[i].EnableInClassList(ActiveLanguageClass, i == (int)Localization.Current);
            if (_matches.childCount > 0)
                ShowMatches(_searchField.value);
        }

        /// <summary>The target in the coordinate fields as a J2000 direction, or null when they cannot be read.</summary>
        Vec3? CurrentTarget()
        {
            if (!SkyCoordinates.TryParse(_raField.value, out double raHours) || raHours < 0 || raHours >= 24 ||
                !SkyCoordinates.TryParse(_decField.value, out double decDeg) || Math.Abs(decDeg) > 90)
                return null;
            return Vec3.FromRaDecDegrees(raHours * 15, decDeg);
        }

        void SetSolvedCenter(Vec3? center)
        {
            _solvedCenter = center;
            _mount.Refresh();
        }

        void ShowResult(Func<string> result)
        {
            _result = result;
            _resultLabel.text = result?.Invoke() ?? "";
        }

        void OnSearchChanged(ChangeEvent<string> change)
        {
            // The field's own label reports text changes the same way; only typed text is a query.
            if (change.target == _searchField)
                ShowMatches(change.newValue);
        }

        void ShowMatches(string query)
        {
            _matches.Clear();
            foreach (SkyObject item in _objects.Search(query, MaxMatches))
            {
                SkyObject chosen = item;
                var button = new Button(() => ChooseObject(chosen)) { text = DescribeObject(item) };
                button.AddToClassList("match");
                _matches.Add(button);
            }
        }

        /// <summary>Puts the object's coordinates into the target fields, where they can still be edited by hand.</summary>
        void ChooseObject(SkyObject item)
        {
            _searchField.SetValueWithoutNotify(item.Title);
            _matches.Clear();
            _raField.value = SkyCoordinates.RaFieldText(item.RaDeg);
            _decField.value = SkyCoordinates.DecFieldText(item.DecDeg);
        }

        static string DescribeObject(SkyObject item)
        {
            var text = new StringBuilder(item.Title);
            string type = Localization.ObjectType(item.Type);
            if (type != null)
                text.Append(" · ").Append(type);
            if (!float.IsNaN(item.Magnitude))
            {
                text.Append(" · ").Append(Localization.Get("magnitude"))
                    .Append(item.Magnitude.ToString("F1", CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }

        void PickImage()
        {
            ImagePicker.Pick(SetImage, message => ShowResult(() => Localization.Get("photoError") + message));
        }

        /// <summary>Makes a photo from the gallery or from the camera the frame to solve.</summary>
        void SetImage(string path, string displayName)
        {
            _imagePath = path;
            _imageName = displayName;
            _fileLabel.text = displayName;
            SetSolvedCenter(null);
            ShowResult(null);
            UpdateButtons();
        }

        void UpdateButtons()
        {
            _pickButton.SetEnabled(!_busy);
            _cameraButton.SetEnabled(!_busy);
            _solveButton.SetEnabled(_wideSearch != null || (!_busy && !string.IsNullOrEmpty(_imagePath)));
            _solveButton.text = Localization.Get(_wideSearch != null ? "cancel" : "solve");
        }

        async void Solve()
        {
            if (_wideSearch != null)
            {
                _wideSearch.Cancel();
                return;
            }

            // The target is optional: with both fields empty the frame is simply located on the sky.
            bool hasTarget = !string.IsNullOrWhiteSpace(_raField.value) || !string.IsNullOrWhiteSpace(_decField.value);
            double raHours = 0, decDeg = 0;
            if (hasTarget && (!SkyCoordinates.TryParse(_raField.value, out raHours) || raHours < 0 || raHours >= 24))
            {
                ShowResult(() => Localization.Get("raError"));
                return;
            }
            if (hasTarget && (!SkyCoordinates.TryParse(_decField.value, out decDeg) || Math.Abs(decDeg) > 90))
            {
                ShowResult(() => Localization.Get("decError"));
                return;
            }
            if (!SkyCoordinates.TryParse(_radiusField.value, out double radiusDeg) || radiusDeg <= 0 || radiusDeg > 45)
            {
                ShowResult(() => Localization.Get("radiusError"));
                return;
            }
            if (!SkyCoordinates.TryParse(_scaleField.value, out double pixelScale) || pixelScale <= 0)
            {
                ShowResult(() => Localization.Get("scaleError"));
                return;
            }

            _busy = true;
            UpdateButtons();
            SetSolvedCenter(null);
            ShowResult(() => Localization.Get("solving"));
            try
            {
                var clock = Stopwatch.StartNew();
                string path = _imagePath;
                byte[] encoded = await Task.Run(() => File.ReadAllBytes(path));
                GrayImage image = FrameImage.Decode(encoded, Binning, out int width, out int height);

                _catalog ??= new StarCatalog(Resources.Load<TextAsset>("StarCatalog/tycho2").bytes);
                StarCatalog catalog = _catalog;
                Vec3? target = hasTarget ? Vec3.FromRaDecDegrees(raHours * 15, decDeg) : (Vec3?)null;
                // Without a target the best guess is where the previous frame was, else the north pole.
                Vec3 start = target ?? _lastSolved ?? Vec3.FromRaDecDegrees(0, 90);

                // The index is kept while the starting point stays the same; building it is the slow part.
                SolverIndex index = _index;
                if (index != null && (index.RadiusDeg != radiusDeg || index.Center.AngleTo(start) > 1e-9))
                    index = null;

                var options = new PlateSolverOptions { PixelScaleArcsec = pixelScale };
                List<DetectedStar> stars = null;
                PlateSolution solution = await Task.Run(() =>
                {
                    stars = StarDetector.Detect(image);
                    index ??= new SolverIndex(catalog, start, radiusDeg);
                    return PlateSolver.Solve(stars, width, height, index, options);
                });

                if (this == null)
                    return;
                _index = index;

                // Not near the starting point: the mount may be far off, or there was no target to start from.
                bool cancelled = false;
                if (solution == null)
                {
                    _wideSearch = new CancellationTokenSource();
                    UpdateButtons();
                    CancellationToken token = _wideSearch.Token;
                    List<Vec3> areas = SkyAreas.Around(start, WideSearchRadiusDeg);
                    string message = hasTarget ? "searchingSky" : "searchingSkyNoTarget";
                    var progress = new Progress<int>(done =>
                    {
                        if (_wideSearch != null)
                            ShowResult(() => string.Format(Localization.Get(message), done, areas.Count));
                    });
                    try
                    {
                        solution = await Task.Run(() => SearchAreas(catalog, areas, stars, width, height, options,
                            progress, token));
                    }
                    finally
                    {
                        cancelled = token.IsCancellationRequested;
                        _wideSearch.Dispose();
                        _wideSearch = null;
                    }
                    if (this == null)
                        return;
                }

                if (cancelled && solution == null)
                {
                    ShowResult(() => Localization.Get("searchStopped"));
                    return;
                }
                if (solution != null)
                {
                    _lastSolved = Vec3.FromRaDecDegrees(solution.RaDeg, solution.DecDeg);
                    SetSolvedCenter(_lastSolved);
                }
                int detected = stars.Count;
                double seconds = clock.Elapsed.TotalSeconds;
                var inFrame = solution == null ? null : _objects.InFrame(solution, width, height);
                ShowResult(() => Describe(solution, target, inFrame, detected, seconds));
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
                if (this != null)
                    ShowResult(() => Localization.Get("error") + e.Message);
            }
            finally
            {
                _busy = false;
                if (this != null)
                    UpdateButtons();
            }
        }

        /// <summary>Tries the areas one by one until the frame is found or the search is stopped.</summary>
        static PlateSolution SearchAreas(StarCatalog catalog, List<Vec3> areas, List<DetectedStar> stars,
            int width, int height, PlateSolverOptions options, IProgress<int> progress, CancellationToken token)
        {
            for (int i = 0; i < areas.Count && !token.IsCancellationRequested; i++)
            {
                progress.Report(i + 1);
                var index = new SolverIndex(catalog, areas[i], WideSearchRadiusDeg);
                PlateSolution solution = PlateSolver.Solve(stars, width, height, index, options);
                if (solution != null)
                    return solution;
            }
            return null;
        }

        static string Describe(PlateSolution solution, Vec3? target,
            List<(SkyObject item, double fromCentreDeg)> inFrame, int detected, double seconds)
        {
            CultureInfo plain = CultureInfo.InvariantCulture;
            var text = new StringBuilder();
            if (solution == null)
            {
                text.AppendLine(Localization.Get("noSolution"));
                text.AppendLine(string.Format(plain, Localization.Get("noSolutionStats"), detected, seconds));
                text.Append(Localization.Get("noSolutionHint"));
                return text.ToString();
            }

            text.AppendLine(Localization.Get("frameCentre"));
            text.AppendLine($"RA {SkyCoordinates.FormatRa(solution.RaDeg)}    Dec {SkyCoordinates.FormatDec(solution.DecDeg)}");
            text.AppendLine();
            if (target != null)
            {
                Vec3 center = Vec3.FromRaDecDegrees(solution.RaDeg, solution.DecDeg);
                double targetRa = target.Value.Ra * Angle.RadToDeg, targetDec = target.Value.Dec * Angle.RadToDeg;
                double deltaRa = ((targetRa - solution.RaDeg + 540) % 360) - 180;
                double deltaDec = targetDec - solution.DecDeg;
                double offset = center.AngleTo(target.Value) * Angle.RadToDeg;

                text.AppendLine(Localization.Get("toTarget") + FormatOffset(offset));
                text.AppendLine(string.Format(plain, Localization.Get("toTargetAxes"), deltaRa, deltaDec));
                text.AppendLine();
            }
            if (inFrame.Count == 0)
            {
                text.AppendLine(Localization.Get("inFrameNone"));
            }
            else
            {
                text.AppendLine(Localization.Get("inFrame"));
                for (int i = 0; i < inFrame.Count && i < MaxObjectsInFrame; i++)
                {
                    text.Append("   ").Append(DescribeObject(inFrame[i].item)).Append(" · ").AppendLine(
                        string.Format(Localization.Get("fromCentre"), FormatOffset(inFrame[i].fromCentreDeg)));
                }
                if (inFrame.Count > MaxObjectsInFrame)
                {
                    text.Append("   ").AppendLine(
                        string.Format(Localization.Get("andMore"), inFrame.Count - MaxObjectsInFrame));
                }
            }
            text.AppendLine();

            text.AppendLine(string.Format(plain, Localization.Get("scaleLine"), solution.PixelScaleArcsec,
                solution.RotationDeg, solution.Mirrored ? Localization.Get("mirrored") : ""));
            text.AppendLine(string.Format(plain, Localization.Get("starsLine"), detected, solution.MatchedStars,
                solution.RmsArcsec));
            text.Append(string.Format(plain, Localization.Get("time"), seconds));
            return text.ToString();
        }

        static string FormatOffset(double degrees)
        {
            CultureInfo plain = CultureInfo.InvariantCulture;
            if (degrees >= 1)
                return string.Format(plain, "{0:F2}°", degrees);
            if (degrees * 60 >= 1)
                return string.Format(plain, "{0:F1}′", degrees * 60);
            return string.Format(plain, "{0:F0}″", degrees * 3600);
        }
    }
}
