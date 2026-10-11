using System;
using System.Collections.Generic;
using System.Globalization;
using AstroTracker.PlateSolving;

namespace AstroTracker
{
    public sealed class SkyObject
    {
        /// <summary>All designations, the best known first: "M 82", "NGC 3034".</summary>
        public string[] Designations;
        /// <summary>Common names in English, possibly none.</summary>
        public string[] Names;
        /// <summary>OpenNGC type code: G, OCl, GCl, PN, HII, ...</summary>
        public string Type;
        /// <summary>J2000 position in degrees.</summary>
        public double RaDeg, DecDeg;
        /// <summary>NaN when unknown.</summary>
        public float Magnitude;
        /// <summary>Major axis in arcminutes, NaN when unknown.</summary>
        public float SizeArcmin;

        internal string[] Keys;

        public string Title => Names.Length > 0 ? Designations[0] + " — " + Names[0] : Designations[0];
    }

    /// <summary>
    /// Targets searchable by designation or name: deep-sky objects and named stars from the lists written by
    /// Tools/ObjectCatalog/convert_openngc.py and convert_stars.py.
    /// </summary>
    public sealed class ObjectCatalog
    {
        readonly List<SkyObject> _objects = new List<SkyObject>();

        public int Count => _objects.Count;

        /// <summary>
        /// Objects whose position falls inside a solved frame, brightest first, with their distance from
        /// the frame centre in degrees.
        /// </summary>
        public List<(SkyObject item, double fromCentreDeg)> InFrame(PlateSolution solution, int width, int height)
        {
            Vec3 centre = Vec3.FromRaDecDegrees(solution.RaDeg, solution.DecDeg);
            var found = new List<(SkyObject, double)>();
            foreach (SkyObject item in _objects)
            {
                Vec3 direction = Vec3.FromRaDecDegrees(item.RaDeg, item.DecDeg);
                if (direction.Dot(centre) < 0.9 || !solution.SkyToPixel(direction, out double x, out double y))
                    continue;
                if (x >= 0 && x <= width - 1 && y >= 0 && y <= height - 1)
                    found.Add((item, direction.AngleTo(centre) * Angle.RadToDeg));
            }
            found.Sort((a, b) => Brightness(a.Item1).CompareTo(Brightness(b.Item1)));
            return found;
        }

        public ObjectCatalog(params string[] lists)
        {
            foreach (string list in lists)
                Load(list);
        }

        void Load(string text)
        {
            foreach (string line in text.Split('\n'))
            {
                string[] columns = line.TrimEnd('\r').Split('\t');
                if (columns.Length < 7)
                    continue;

                var item = new SkyObject
                {
                    Designations = columns[0].Split('|'),
                    Names = columns[1].Length == 0 ? Array.Empty<string>() : columns[1].Split('|'),
                    Type = columns[2],
                    RaDeg = double.Parse(columns[3], CultureInfo.InvariantCulture),
                    DecDeg = double.Parse(columns[4], CultureInfo.InvariantCulture),
                    Magnitude = ParseOptional(columns[5]),
                    SizeArcmin = ParseOptional(columns[6]),
                };

                item.Keys = new string[item.Designations.Length + item.Names.Length];
                for (int i = 0; i < item.Designations.Length; i++)
                    item.Keys[i] = Normalize(item.Designations[i]);
                for (int i = 0; i < item.Names.Length; i++)
                    item.Keys[item.Designations.Length + i] = Normalize(item.Names[i]);
                _objects.Add(item);
            }
        }

        /// <summary>
        /// Objects matching what the user typed, best first. "m82", "M 82" and "ngc3034" find a designation,
        /// "82" finds every catalogue's number 82, "cigar" finds a name.
        /// </summary>
        public List<SkyObject> Search(string query, int limit)
        {
            var results = new List<SkyObject>();
            string key = Normalize(query);
            if (key.Length == 0)
                return results;
            bool numeric = char.IsDigit(key[0]);

            var ranked = new List<(int rank, SkyObject item)>();
            foreach (SkyObject item in _objects)
            {
                int best = int.MaxValue;
                for (int i = 0; i < item.Keys.Length; i++)
                {
                    bool isDesignation = i < item.Designations.Length;
                    string candidate = item.Keys[i];
                    // A bare number is compared with the number in the deep-sky catalogues, not with
                    // the star catalogue numbers that would flood the result.
                    if (numeric)
                    {
                        if (!isDesignation || !IsNumberedCatalogue(candidate))
                            continue;
                        candidate = candidate.TrimStart(Letters);
                    }

                    int rank;
                    if (candidate == key)
                        rank = 0;
                    else if (candidate.StartsWith(key, StringComparison.Ordinal))
                        rank = 1;
                    else if (!isDesignation && candidate.Contains(key))
                        rank = 2;
                    else
                        continue;
                    best = Math.Min(best, rank);
                }
                if (best != int.MaxValue)
                    ranked.Add((best, item));
            }

            ranked.Sort((a, b) =>
            {
                if (a.rank != b.rank)
                    return a.rank.CompareTo(b.rank);
                bool messierA = a.item.Designations[0].StartsWith("M ", StringComparison.Ordinal);
                bool messierB = b.item.Designations[0].StartsWith("M ", StringComparison.Ordinal);
                if (messierA != messierB)
                    return messierA ? -1 : 1;
                return Brightness(a.item).CompareTo(Brightness(b.item));
            });

            for (int i = 0; i < ranked.Count && i < limit; i++)
                results.Add(ranked[i].item);
            return results;
        }

        static readonly char[] Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();

        /// <summary>True for normalized Messier, NGC, IC and Caldwell designations such as "M82" or "NGC3034".</summary>
        static bool IsNumberedCatalogue(string key)
        {
            string prefix = key.Substring(0, key.Length - key.TrimStart(Letters).Length);
            return prefix == "M" || prefix == "NGC" || prefix == "IC" || prefix == "C";
        }

        static float Brightness(SkyObject item) => float.IsNaN(item.Magnitude) ? 99f : item.Magnitude;

        static float ParseOptional(string text)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value
                : float.NaN;
        }

        /// <summary>Upper case with everything but letters and digits removed: "m 82" -> "M82".</summary>
        static string Normalize(string text)
        {
            var chars = new char[text.Length];
            int count = 0;
            foreach (char c in text)
                if (char.IsLetterOrDigit(c))
                    chars[count++] = char.ToUpperInvariant(c);
            return new string(chars, 0, count);
        }
    }
}
