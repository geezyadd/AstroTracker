using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
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
        const string ActiveLanguageClass = "language-active";

        Button _pickButton, _solveButton;
        Button[] _languageButtons;
        Label _fileLabel, _resultLabel;
        TextField _searchField, _raField, _decField, _radiusField, _scaleField;
        VisualElement _matches;
        ObjectCatalog _objects;

        string _imagePath, _imageName;
        bool _busy;
        StarCatalog _catalog;
        SolverIndex _index;
        /// <summary>Produces the text under the Solve button, so it can be rewritten in another language.</summary>
        Func<string> _result;

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

            _objects ??= new ObjectCatalog(Resources.Load<TextAsset>("ObjectCatalog/objects").text);
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
            _searchField.UnregisterValueChangedCallback(OnSearchChanged);
            _pickButton.clicked -= PickImage;
            _solveButton.clicked -= Solve;
        }

        /// <summary>Rewrites every text on the screen in the current language.</summary>
        void ApplyLanguage()
        {
            _pickButton.text = Localization.Get("pick");
            _solveButton.text = Localization.Get("solve");
            _searchField.label = Localization.Get("object");
            _raField.label = Localization.Get("targetRa");
            _decField.label = Localization.Get("targetDec");
            _radiusField.label = Localization.Get("radius");
            _scaleField.label = Localization.Get("scale");
            _fileLabel.text = _imageName ?? Localization.Get("noPhoto");
            _resultLabel.text = _result?.Invoke() ?? "";

            for (int i = 0; i < _languageButtons.Length; i++)
                _languageButtons[i].EnableInClassList(ActiveLanguageClass, i == (int)Localization.Current);
            if (_matches.childCount > 0)
                ShowMatches(_searchField.value);
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
            ImagePicker.Pick(
                (path, displayName) =>
                {
                    _imagePath = path;
                    _imageName = displayName;
                    _fileLabel.text = displayName;
                    ShowResult(null);
                    UpdateButtons();
                },
                message => ShowResult(() => Localization.Get("photoError") + message));
        }

        void UpdateButtons()
        {
            _pickButton.SetEnabled(!_busy);
            _solveButton.SetEnabled(!_busy && !string.IsNullOrEmpty(_imagePath));
        }

        async void Solve()
        {
            if (!SkyCoordinates.TryParse(_raField.value, out double raHours) || raHours < 0 || raHours >= 24)
            {
                ShowResult(() => Localization.Get("raError"));
                return;
            }
            if (!SkyCoordinates.TryParse(_decField.value, out double decDeg) || Math.Abs(decDeg) > 90)
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
            ShowResult(() => Localization.Get("solving"));
            try
            {
                var clock = Stopwatch.StartNew();
                string path = _imagePath;
                byte[] encoded = await Task.Run(() => File.ReadAllBytes(path));
                GrayImage image = FrameImage.Decode(encoded, Binning, out int width, out int height);

                _catalog ??= new StarCatalog(Resources.Load<TextAsset>("StarCatalog/tycho2").bytes);
                StarCatalog catalog = _catalog;
                Vec3 target = Vec3.FromRaDecDegrees(raHours * 15, decDeg);

                // The index is kept while the target stays the same; building it is the slow part.
                SolverIndex index = _index;
                if (index != null && (index.RadiusDeg != radiusDeg || index.Center.AngleTo(target) > 1e-9))
                    index = null;

                var options = new PlateSolverOptions { PixelScaleArcsec = pixelScale };
                List<DetectedStar> stars = null;
                PlateSolution solution = await Task.Run(() =>
                {
                    stars = StarDetector.Detect(image);
                    index ??= new SolverIndex(catalog, target, radiusDeg);
                    return PlateSolver.Solve(stars, width, height, index, options);
                });

                if (this == null)
                    return;
                _index = index;
                int detected = stars.Count;
                double seconds = clock.Elapsed.TotalSeconds;
                ShowResult(() => Describe(solution, target, detected, seconds));
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

        static string Describe(PlateSolution solution, Vec3 target, int detected, double seconds)
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

            Vec3 center = Vec3.FromRaDecDegrees(solution.RaDeg, solution.DecDeg);
            double targetRa = target.Ra * Angle.RadToDeg, targetDec = target.Dec * Angle.RadToDeg;
            double deltaRa = ((targetRa - solution.RaDeg + 540) % 360) - 180;
            double deltaDec = targetDec - solution.DecDeg;
            double offset = center.AngleTo(target) * Angle.RadToDeg;

            text.AppendLine(Localization.Get("frameCentre"));
            text.AppendLine($"RA {SkyCoordinates.FormatRa(solution.RaDeg)}    Dec {SkyCoordinates.FormatDec(solution.DecDeg)}");
            text.AppendLine();
            text.AppendLine(Localization.Get("toTarget") + FormatOffset(offset));
            text.AppendLine(string.Format(plain, Localization.Get("toTargetAxes"), deltaRa, deltaDec));
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
