using System;

namespace Sproutshot.MapGen
{
    // Local PRNG: never touches UnityEngine.Random/global game randomness.
    internal sealed class MapRandom
    {
        private uint state;
        public MapRandom(int seed) { state = unchecked((uint)seed) ^ 0x9e3779b9u; if (state == 0) state = 1; }
        public uint Next() { uint x = state; x ^= x << 13; x ^= x >> 17; x ^= x << 5; return state = x; }
        public double Unit() => (Next() >> 8) / 16777216.0;
    }

    /// <summary>Seeded 2D gradient Perlin noise; no Unity native calls, testable outside the engine.</summary>
    internal sealed class SeededNoise
    {
        private readonly int[] permutation = new int[512];
        public SeededNoise(int seed)
        {
            var rng = new MapRandom(seed);
            for (int i = 0; i < 256; i++) permutation[i] = i;
            for (int i = 255; i > 0; i--)
            {
                int j = (int)(rng.Next() % (uint)(i + 1));
                int t = permutation[i]; permutation[i] = permutation[j]; permutation[j] = t;
            }
            for (int i = 0; i < 256; i++) permutation[i + 256] = permutation[i];
        }
        private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
        private static double Gradient(int hash, double x, double y)
        {
            switch (hash & 7)
            {
                case 0: return x; case 1: return -x; case 2: return y; case 3: return -y;
                case 4: return (x + y) * 0.70710678118; case 5: return (x - y) * 0.70710678118;
                case 6: return (-x + y) * 0.70710678118; default: return (-x - y) * 0.70710678118;
            }
        }
        public double Sample(double x, double y)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            x -= ix; y -= iy; ix &= 255; iy &= 255;
            int a = permutation[ix] + iy, b = permutation[ix + 1] + iy;
            return Lerp(Lerp(Gradient(permutation[a], x, y), Gradient(permutation[b], x - 1, y), Fade(x)),
                Lerp(Gradient(permutation[a + 1], x, y - 1), Gradient(permutation[b + 1], x - 1, y - 1), Fade(x)), Fade(y));
        }
        public double Fractal(double x, double y, int octaves)
        {
            double sum = 0, weight = 1, weights = 0;
            for (int i = 0; i < octaves; i++)
            { sum += Sample(x, y) * weight; weights += weight; x *= 2; y *= 2; weight *= 0.5; }
            return Math.Max(0, Math.Min(1, 0.5 + sum / weights));
        }
    }
}
