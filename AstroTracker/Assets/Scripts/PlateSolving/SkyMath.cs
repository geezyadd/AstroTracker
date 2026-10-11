using System;

namespace AstroTracker.PlateSolving
{
    public static class Angle
    {
        public const double DegToRad = Math.PI / 180.0;
        public const double RadToDeg = 180.0 / Math.PI;
        public const double ArcsecToRad = Math.PI / (180.0 * 3600.0);
        public const double RadToArcsec = 180.0 * 3600.0 / Math.PI;
    }

    /// <summary>Direction on the celestial sphere as a unit vector (X to RA 0, Z to the north pole).</summary>
    public readonly struct Vec3
    {
        public readonly double X, Y, Z;

        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vec3 FromRaDec(double raRad, double decRad)
        {
            double c = Math.Cos(decRad);
            return new Vec3(c * Math.Cos(raRad), c * Math.Sin(raRad), Math.Sin(decRad));
        }

        public static Vec3 FromRaDecDegrees(double raDeg, double decDeg)
        {
            return FromRaDec(raDeg * Angle.DegToRad, decDeg * Angle.DegToRad);
        }

        /// <summary>Right ascension in radians, 0..2π.</summary>
        public double Ra
        {
            get
            {
                double ra = Math.Atan2(Y, X);
                return ra < 0 ? ra + 2 * Math.PI : ra;
            }
        }

        /// <summary>Declination in radians.</summary>
        public double Dec => Math.Atan2(Z, Math.Sqrt(X * X + Y * Y));

        public double Dot(Vec3 o) => X * o.X + Y * o.Y + Z * o.Z;

        public Vec3 Cross(Vec3 o) => new Vec3(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);

        public Vec3 Normalized()
        {
            double n = Math.Sqrt(X * X + Y * Y + Z * Z);
            return new Vec3(X / n, Y / n, Z / n);
        }

        /// <summary>Angle between two unit vectors, radians.</summary>
        public double AngleTo(Vec3 o) => Math.Atan2(Cross(o).Length, Dot(o));

        double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator *(Vec3 a, double s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
    }

    /// <summary>Gnomonic projection: xi grows to the east, eta to the north, both in radians near the centre.</summary>
    public readonly struct TangentPlane
    {
        public readonly Vec3 Center, East, North;

        public TangentPlane(Vec3 center)
        {
            Center = center;
            double h = Math.Sqrt(center.X * center.X + center.Y * center.Y);
            East = h < 1e-12 ? new Vec3(0, 1, 0) : new Vec3(-center.Y / h, center.X / h, 0);
            North = center.Cross(East);
        }

        /// <summary>Returns false for directions too far from the centre to project.</summary>
        public bool Project(Vec3 direction, out double xi, out double eta)
        {
            double d = direction.Dot(Center);
            if (d < 0.05)
            {
                xi = eta = 0;
                return false;
            }
            xi = direction.Dot(East) / d;
            eta = direction.Dot(North) / d;
            return true;
        }

        public Vec3 Unproject(double xi, double eta)
        {
            return (Center + East * xi + North * eta).Normalized();
        }
    }

    /// <summary>
    /// Rotation between the J2000 frame and the mean equator and equinox of a date (IAU 1976 precession).
    /// Mount software usually works in coordinates "of date" (JNow); the catalogues here are J2000.
    /// </summary>
    public static class Precession
    {
        /// <summary>Julian year of a UTC moment, e.g. 2026.78.</summary>
        public static double JulianYear(DateTime utc)
        {
            double julianDate = utc.ToOADate() + 2415018.5;
            return 2000.0 + (julianDate - 2451545.0) / 365.25;
        }

        public static Vec3 FromJ2000(Vec3 direction, double year) => Rotate(direction, year, false);

        public static Vec3 ToJ2000(Vec3 direction, double year) => Rotate(direction, year, true);

        static Vec3 Rotate(Vec3 v, double year, bool inverse)
        {
            double t = (year - 2000.0) / 100.0;
            double zeta = (2306.2181 + (0.30188 + 0.017998 * t) * t) * t * Angle.ArcsecToRad;
            double z = (2306.2181 + (1.09468 + 0.018203 * t) * t) * t * Angle.ArcsecToRad;
            double theta = (2004.3109 - (0.42665 + 0.041833 * t) * t) * t * Angle.ArcsecToRad;

            double cz = Math.Cos(zeta), sz = Math.Sin(zeta);
            double cZ = Math.Cos(z), sZ = Math.Sin(z);
            double ct = Math.Cos(theta), st = Math.Sin(theta);

            double xx = cz * ct * cZ - sz * sZ, xy = -sz * ct * cZ - cz * sZ, xz = -st * cZ;
            double yx = cz * ct * sZ + sz * cZ, yy = -sz * ct * sZ + cz * cZ, yz = -st * sZ;
            double zx = cz * st, zy = -sz * st, zz = ct;

            // The matrix is a rotation, so its inverse is its transpose.
            return inverse
                ? new Vec3(xx * v.X + yx * v.Y + zx * v.Z, xy * v.X + yy * v.Y + zy * v.Z, xz * v.X + yz * v.Y + zz * v.Z)
                : new Vec3(xx * v.X + xy * v.Y + xz * v.Z, yx * v.X + yy * v.Y + yz * v.Z, zx * v.X + zy * v.Y + zz * v.Z);
        }
    }
}
