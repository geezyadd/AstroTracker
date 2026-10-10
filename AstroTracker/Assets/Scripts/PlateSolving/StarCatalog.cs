using System;
using System.Collections.Generic;

namespace AstroTracker.PlateSolving
{
    public struct CatalogStar
    {
        public Vec3 Direction;
        public float Magnitude;
        /// <summary>Distance to the nearest catalogue star in 0.1 arcsec, 999 when far.</summary>
        public ushort Proximity;
    }

    /// <summary>Reader for the binary catalogue written by Tools/StarCatalog/convert_tycho2.py.</summary>
    public sealed class StarCatalog
    {
        const int HeaderSize = 24;
        const int StarSize = 12;
        const double RaScale = 2 * Math.PI / 4294967296.0;
        const double DecScale = Math.PI / 2 / 2147483647.0;

        readonly byte[] _data;
        readonly int[] _bandStart;
        readonly int _starsOffset;
        readonly int _bandCount;

        public int StarCount { get; }
        /// <summary>Julian year the positions refer to.</summary>
        public float Epoch { get; }
        public float MaxMagnitude { get; }

        public StarCatalog(byte[] data)
        {
            if (data == null || data.Length < HeaderSize ||
                data[0] != 'A' || data[1] != 'T' || data[2] != 'S' || data[3] != 'C')
                throw new ArgumentException("Not a star catalogue file");
            if (BitConverter.ToUInt32(data, 4) != 1)
                throw new ArgumentException("Unsupported star catalogue version");

            _data = data;
            StarCount = (int)BitConverter.ToUInt32(data, 8);
            Epoch = BitConverter.ToSingle(data, 12);
            MaxMagnitude = BitConverter.ToSingle(data, 16);
            _bandCount = (int)BitConverter.ToUInt32(data, 20);

            _bandStart = new int[_bandCount + 1];
            for (int i = 0; i <= _bandCount; i++)
                _bandStart[i] = (int)BitConverter.ToUInt32(data, HeaderSize + 4 * i);
            _starsOffset = HeaderSize + 4 * (_bandCount + 1);

            if (_bandStart[_bandCount] != StarCount || data.Length < _starsOffset + (long)StarCount * StarSize)
                throw new ArgumentException("Star catalogue file is truncated");
        }

        /// <summary>Appends every star within <paramref name="radiusRad"/> of <paramref name="center"/>.</summary>
        public void Query(Vec3 center, double radiusRad, List<CatalogStar> results)
        {
            double dec0 = center.Dec, ra0 = center.Ra;
            double decMin = dec0 - radiusRad, decMax = dec0 + radiusRad;
            double cosRadius = Math.Cos(radiusRad);

            int first = BandOf(decMin), last = BandOf(decMax);
            bool allRa = radiusRad >= Math.PI / 2 || decMax >= Math.PI / 2 || decMin <= -Math.PI / 2;
            double halfWidth = allRa ? Math.PI : Math.Asin(Math.Min(1.0, Math.Sin(radiusRad) / Math.Cos(dec0)));

            for (int band = first; band <= last; band++)
            {
                int start = _bandStart[band], end = _bandStart[band + 1];
                if (allRa)
                {
                    Scan(start, end, center, cosRadius, results);
                    continue;
                }

                double lo = ra0 - halfWidth, hi = ra0 + halfWidth;
                if (lo < 0)
                {
                    Scan(LowerBound(start, end, lo + 2 * Math.PI), end, center, cosRadius, results);
                    Scan(start, LowerBound(start, end, hi), center, cosRadius, results);
                }
                else if (hi > 2 * Math.PI)
                {
                    Scan(LowerBound(start, end, lo), end, center, cosRadius, results);
                    Scan(start, LowerBound(start, end, hi - 2 * Math.PI), center, cosRadius, results);
                }
                else
                {
                    Scan(LowerBound(start, end, lo), LowerBound(start, end, hi), center, cosRadius, results);
                }
            }
        }

        int BandOf(double decRad)
        {
            int band = (int)Math.Floor(decRad * Angle.RadToDeg + 90.0);
            return Math.Max(0, Math.Min(_bandCount - 1, band));
        }

        /// <summary>First star in [start, end) whose RA is not below <paramref name="raRad"/>.</summary>
        int LowerBound(int start, int end, double raRad)
        {
            double scaled = raRad / RaScale;
            uint key = scaled <= 0 ? 0u : scaled >= uint.MaxValue ? uint.MaxValue : (uint)scaled;
            while (start < end)
            {
                int mid = start + (end - start) / 2;
                if (BitConverter.ToUInt32(_data, _starsOffset + mid * StarSize) < key)
                    start = mid + 1;
                else
                    end = mid;
            }
            return start;
        }

        void Scan(int start, int end, Vec3 center, double cosRadius, List<CatalogStar> results)
        {
            for (int i = start; i < end; i++)
            {
                int offset = _starsOffset + i * StarSize;
                double ra = BitConverter.ToUInt32(_data, offset) * RaScale;
                double dec = BitConverter.ToInt32(_data, offset + 4) * DecScale;
                Vec3 direction = Vec3.FromRaDec(ra, dec);
                if (direction.Dot(center) < cosRadius)
                    continue;

                results.Add(new CatalogStar
                {
                    Direction = direction,
                    Magnitude = BitConverter.ToInt16(_data, offset + 8) / 1000f,
                    Proximity = BitConverter.ToUInt16(_data, offset + 10),
                });
            }
        }
    }
}
