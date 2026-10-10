using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using AstroTracker.PlateSolving;
using UnityEditor;
using UnityEngine;

namespace AstroTracker.EditorTools
{
    /// <summary>Solves a camera frame from disk, for checking the plate solver without the telescope.</summary>
    public sealed class PlateSolveWindow : EditorWindow
    {
        const string CatalogPath = "Assets/StarCatalog/tycho2.bytes";

        string _imagePath = "";
        double _hintRaHours = 9.93;
        double _hintDecDeg = 69.7;
        double _radiusDeg = 10;
        double _pixelScale = 1.37;
        int _binning = 2;
        string _report = "";

        [MenuItem("AstroTracker/Plate Solve Image")]
        static void Open() => GetWindow<PlateSolveWindow>("Plate Solve");

        void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            _imagePath = EditorGUILayout.TextField("Image", _imagePath);
            if (GUILayout.Button("Browse", GUILayout.Width(70)))
            {
                string picked = EditorUtility.OpenFilePanel("Camera frame", "", "jpg,jpeg,png");
                if (!string.IsNullOrEmpty(picked))
                    _imagePath = picked;
            }
            EditorGUILayout.EndHorizontal();

            _hintRaHours = EditorGUILayout.DoubleField("Expected RA, hours", _hintRaHours);
            _hintDecDeg = EditorGUILayout.DoubleField("Expected Dec, degrees", _hintDecDeg);
            _radiusDeg = EditorGUILayout.DoubleField("Search radius, degrees", _radiusDeg);
            _pixelScale = EditorGUILayout.DoubleField("Pixel scale, arcsec", _pixelScale);
            _binning = EditorGUILayout.IntSlider("Binning", _binning, 1, 4);

            using (new EditorGUI.DisabledScope(!File.Exists(_imagePath)))
            {
                if (GUILayout.Button("Solve"))
                    _report = Solve();
            }
            EditorGUILayout.SelectableLabel(_report, EditorStyles.textArea, GUILayout.ExpandHeight(true));
        }

        string Solve()
        {
            var catalogAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(CatalogPath);
            if (catalogAsset == null)
                return $"Star catalogue not found at {CatalogPath}";

            try
            {
                var report = new StringBuilder();
                var clock = Stopwatch.StartNew();

                GrayImage image = FrameImage.Decode(File.ReadAllBytes(_imagePath), _binning, out int width, out int height);
                report.AppendLine($"Frame {width} x {height}, decoded in {Lap(clock)} ms");

                List<DetectedStar> stars = StarDetector.Detect(image);
                report.AppendLine($"Stars detected: {stars.Count}, {Lap(clock)} ms");

                var catalog = new StarCatalog(catalogAsset.bytes);
                Vec3 hint = Vec3.FromRaDecDegrees(_hintRaHours * 15, _hintDecDeg);
                var index = new SolverIndex(catalog, hint, _radiusDeg);
                report.AppendLine($"Index: {index.StarCount} stars, {index.QuadCount} quads, {Lap(clock)} ms");

                PlateSolution solution = PlateSolver.Solve(stars, width, height, index,
                    new PlateSolverOptions { PixelScaleArcsec = _pixelScale });
                report.AppendLine($"Solve: {Lap(clock)} ms");
                report.AppendLine();

                if (solution == null)
                {
                    report.AppendLine("No solution.");
                    return report.ToString();
                }

                Vec3 center = Vec3.FromRaDecDegrees(solution.RaDeg, solution.DecDeg);
                report.AppendLine($"Centre RA   {FormatSexagesimal(solution.RaDeg / 15, "h")}   ({solution.RaDeg:F5} deg)");
                report.AppendLine($"Centre Dec  {FormatSexagesimal(solution.DecDeg, "d")}   ({solution.DecDeg:F5} deg)");
                report.AppendLine($"Epoch       J{catalog.Epoch:F2}");
                report.AppendLine($"Scale       {solution.PixelScaleArcsec:F4} arcsec/pixel");
                report.AppendLine($"Up is       {solution.RotationDeg:F2} deg east of north{(solution.Mirrored ? ", mirrored" : "")}");
                report.AppendLine($"Matched     {solution.MatchedStars} stars, rms {solution.RmsArcsec:F2} arcsec");
                report.AppendLine($"Off target  {center.AngleTo(hint) * Angle.RadToDeg:F3} deg");
                return report.ToString();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
                return e.Message;
            }
        }

        static long Lap(Stopwatch clock)
        {
            long ms = clock.ElapsedMilliseconds;
            clock.Restart();
            return ms;
        }

        static string FormatSexagesimal(double value, string unit)
        {
            string sign = value < 0 ? "-" : "";
            value = Math.Abs(value);
            int whole = (int)value;
            double minutes = (value - whole) * 60;
            int wholeMinutes = (int)minutes;
            return $"{sign}{whole}{unit} {wholeMinutes:00}m {(minutes - wholeMinutes) * 60:00.0}s";
        }
    }
}
