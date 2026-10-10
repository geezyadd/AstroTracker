using System;
using System.Globalization;

namespace AstroTracker
{
    public static class SkyCoordinates
    {
        /// <summary>
        /// Reads an angle written as a decimal ("9.93", "9,93") or as up to three parts in any
        /// punctuation ("9 55 52", "9h55m52s", "+69°40'47", "-12:30").
        /// </summary>
        public static bool TryParse(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = text.Trim().Replace(',', '.');
            bool negative = text[0] == '-' || text[0] == '−';

            double scale = 1;
            int parts = 0, start = -1;
            for (int i = 0; i <= text.Length; i++)
            {
                bool numeric = i < text.Length && (char.IsDigit(text[i]) || text[i] == '.');
                if (numeric)
                {
                    if (start < 0)
                        start = i;
                    continue;
                }
                if (start < 0)
                    continue;

                if (parts == 3 || !double.TryParse(text.Substring(start, i - start), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double part))
                    return false;
                value += part * scale;
                scale /= 60;
                parts++;
                start = -1;
            }

            if (negative)
                value = -value;
            return parts > 0;
        }

        /// <summary>Right ascension given in degrees, written as hours: "10h 24m 22.2s".</summary>
        public static string FormatRa(double degrees)
        {
            Split(((degrees % 360 + 360) % 360) / 15, 10, out int hours, out int minutes, out double seconds);
            return string.Format(CultureInfo.InvariantCulture, "{0:00}h {1:00}m {2:00.0}s", hours, minutes, seconds);
        }

        /// <summary>Declination in degrees: "+64° 08′ 30″".</summary>
        public static string FormatDec(double degrees)
        {
            Split(Math.Abs(degrees), 1, out int whole, out int minutes, out double seconds);
            return string.Format(CultureInfo.InvariantCulture, "{0}{1:00}° {2:00}′ {3:00}″",
                degrees < 0 ? "−" : "+", whole, minutes, seconds);
        }

        /// <summary>Right ascension in the form the input fields take: "9 55 52.7" (hours).</summary>
        public static string RaFieldText(double degrees)
        {
            Split(((degrees % 360 + 360) % 360) / 15, 10, out int hours, out int minutes, out double seconds);
            return string.Format(CultureInfo.InvariantCulture, "{0} {1:00} {2:00.0}", hours, minutes, seconds);
        }

        /// <summary>Declination in the form the input fields take: "+69 40 46".</summary>
        public static string DecFieldText(double degrees)
        {
            Split(Math.Abs(degrees), 1, out int whole, out int minutes, out double seconds);
            return string.Format(CultureInfo.InvariantCulture, "{0}{1} {2:00} {3:00}",
                degrees < 0 ? "-" : "+", whole, minutes, seconds);
        }

        /// <summary>Splits into whole, minutes and seconds rounded to 1/<paramref name="secondParts"/> without ever showing 60.</summary>
        static void Split(double value, int secondParts, out int whole, out int minutes, out double seconds)
        {
            long total = (long)Math.Round(value * 3600 * secondParts);
            whole = (int)(total / (3600L * secondParts));
            total -= whole * 3600L * secondParts;
            minutes = (int)(total / (60L * secondParts));
            seconds = (total - minutes * 60L * secondParts) / (double)secondParts;
        }
    }
}
