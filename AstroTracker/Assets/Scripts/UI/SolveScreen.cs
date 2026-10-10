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

        static readonly Dictionary<string, string> TypeNames = new Dictionary<string, string>
        {
            { "G", "galaxy" }, { "GPair", "galaxy pair" }, { "GTrpl", "galaxy triplet" },
            { "GGroup", "galaxy group" }, { "OCl", "open cluster" }, { "GCl", "globular cluster" },
            { "Cl+N", "cluster with nebula" }, { "*Ass", "stellar association" },
            { "PN", "planetary nebula" }, { "HII", "H II region" }, { "Neb", "nebula" },
            { "EmN", "emission nebula" }, { "RfN", "reflection nebula" },
            { "DrkN", "dark nebula" }, { "SNR", "supernova remnant" }, { "Nova", "nova" },
            { "*", "star" }, { "**", "double star" },
        };

        Button _pickButton, _solveButton;
        Label _fileLabel, _resultLabel;
        TextField _searchField, _raField, _decField, _radiusField, _scaleField;
        VisualElement _matches;
        ObjectCatalog _objects;

        string _imagePath;
        bool _busy;
        StarCatalog _catalog;
        SolverIndex _index;

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

            foreach (TextField field in new[] { _raField, _decField, _radiusField, _scaleField })
                field.keyboardType = TouchScreenKeyboardType.NumbersAndPunctuation;

            _objects ??= new ObjectCatalog(Resources.Load<TextAsset>("ObjectCatalog/objects").text);
            _searchField.RegisterValueChangedCallback(OnSearchChanged);
            _pickButton.clicked += PickImage;
            _solveButton.clicked += Solve;
            UpdateButtons();
        }

        void OnDisable()
        {
            _searchField.UnregisterValueChangedCallback(OnSearchChanged);
            _pickButton.clicked -= PickImage;
            _solveButton.clicked -= Solve;
        }

        void OnSearchChanged(ChangeEvent<string> change)
        {
            _matches.Clear();
            foreach (SkyObject item in _objects.Search(change.newValue, MaxMatches))
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
            if (TypeNames.TryGetValue(item.Type, out string type))
                text.Append(" · ").Append(type);
            if (!float.IsNaN(item.Magnitude))
                text.Append(" · mag ").Append(item.Magnitude.ToString("F1", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        void PickImage()
        {
            ImagePicker.Pick(
                (path, displayName) =>
                {
                    _imagePath = path;
                    _fileLabel.text = displayName;
                    _resultLabel.text = "";
                    UpdateButtons();
                },
                message => _resultLabel.text = "Could not open the photo: " + message);
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
                _resultLabel.text = "Target RA: hours from 0 to 24, for example \"9 55 52\" or \"9.93\".";
                return;
            }
            if (!SkyCoordinates.TryParse(_decField.value, out double decDeg) || Math.Abs(decDeg) > 90)
            {
                _resultLabel.text = "Target Dec: degrees from −90 to +90, for example \"+69 40 47\".";
                return;
            }
            if (!SkyCoordinates.TryParse(_radiusField.value, out double radiusDeg) || radiusDeg <= 0 || radiusDeg > 45)
            {
                _resultLabel.text = "Search radius: a number of degrees between 0 and 45.";
                return;
            }
            if (!SkyCoordinates.TryParse(_scaleField.value, out double pixelScale) || pixelScale <= 0)
            {
                _resultLabel.text = "Pixel scale: a positive number of arcseconds per pixel.";
                return;
            }

            _busy = true;
            UpdateButtons();
            _resultLabel.text = "Solving…";
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
                _resultLabel.text = Describe(solution, target, stars.Count, clock.Elapsed.TotalSeconds);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
                if (this != null)
                    _resultLabel.text = "Error: " + e.Message;
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
                text.AppendLine("No solution found.");
                text.AppendLine(string.Format(plain, "Stars in the frame: {0}. Time: {1:F1} s.", detected, seconds));
                text.Append("Check the target coordinates, the search radius and the pixel scale.");
                return text.ToString();
            }

            Vec3 center = Vec3.FromRaDecDegrees(solution.RaDeg, solution.DecDeg);
            double targetRa = target.Ra * Angle.RadToDeg, targetDec = target.Dec * Angle.RadToDeg;
            double deltaRa = ((targetRa - solution.RaDeg + 540) % 360) - 180;
            double deltaDec = targetDec - solution.DecDeg;
            double offset = center.AngleTo(target) * Angle.RadToDeg;

            text.AppendLine("Frame centre (J2000)");
            text.AppendLine($"RA {SkyCoordinates.FormatRa(solution.RaDeg)}    Dec {SkyCoordinates.FormatDec(solution.DecDeg)}");
            text.AppendLine();
            text.AppendLine("Distance to target: " + FormatOffset(offset));
            text.AppendLine(string.Format(plain, "RA {0:+0.000;−0.000}°, Dec {1:+0.000;−0.000}°", deltaRa, deltaDec));
            text.AppendLine();
            text.AppendLine(string.Format(plain, "Scale {0:F3}″/pixel, frame up is {1:F1}° east of north{2}",
                solution.PixelScaleArcsec, solution.RotationDeg, solution.Mirrored ? ", mirrored" : ""));
            text.AppendLine(string.Format(plain, "Stars in the frame {0}, matched to the catalogue {1}, rms {2:F2}″",
                detected, solution.MatchedStars, solution.RmsArcsec));
            text.Append(string.Format(plain, "Time: {0:F1} s", seconds));
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
