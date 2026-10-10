using System;
using System.Collections.Generic;
using System.Globalization;

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

    /// <summary>Deep-sky objects from Tools/ObjectCatalog/convert_openngc.py, searchable by designation or name.</summary>
    public sealed class ObjectCatalog
    {
        readonly List<SkyObject> _objects = new List<SkyObject>();

        public int Count => _objects.Count;

        public ObjectCatalog(string text)
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
                    // A bare number is compared with the numeric part of the designations.
                    if (numeric)
                    {
                        if (!isDesignation)
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
