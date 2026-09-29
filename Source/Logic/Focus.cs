using System;

namespace SirDiorama
{
    // The miniature look, as the shader computes it (ShaderSource), copied here
    // so the tests can check it. Everything is a ratio of distances, never a
    // distance in metres: in Space Engineers the scene goes from one metre to
    // tens of kilometres.
    public static class Focus
    {
        // The sky and anything without depth count as this far, in metres.
        public const float Far = 1.0e7f;

        public const float Near = 0.05f;

        // Within 6 % of the focus distance, nothing is blurred at all: an LCD
        // panel that is looked at stays perfectly sharp.
        public const float SharpBand = 0.06f;

        // How fast the blur grows outside the sharp band. At twice the focus
        // distance the blur reaches two thirds of its largest size; at half of
        // it, and beyond, the largest size.
        public const float BlurGain = 1.5f;

        // Time constant of the focus following the view, in seconds.
        public const float FollowTime = 0.3f;

        // Largest blur radius at 100 % strength, as a share of the screen
        // height (so the look is the same at any resolution).
        public const float RadiusAtFullStrength = 0.03f;

        // Circle of confusion radius, in the unit of maxRadius.
        public static float BlurRadius(float distance, float focus, float maxRadius)
        {
            var ratio = Math.Abs(1f - focus / distance);
            var x = (ratio - SharpBand) * BlurGain;
            return maxRadius * Math.Max(0f, Math.Min(1f, x));
        }

        // Next focus, as log2 of the distance. Without a valid previous value,
        // the focus goes straight to the target; otherwise it eases towards
        // it, without any jump, whatever the frame rate.
        public static float Follow(float previousLog2, bool previousValid, float targetLog2, float frameSeconds)
        {
            if (!previousValid || !(Math.Abs(previousLog2) < 40f))
                return targetLog2;

            var t = 1f - (float)Math.Exp(-Math.Max(frameSeconds, 0f) / FollowTime);
            return previousLog2 + (targetLog2 - previousLog2) * t;
        }

        // Median of five depth samples around the centre of the screen: a
        // single pixel on the edge of an object does not pull the focus away.
        public static float Median5(float a, float b, float c, float d, float e)
        {
            return Median3(e, Math.Max(Math.Min(a, b), Math.Min(c, d)), Math.Min(Math.Max(a, b), Math.Max(c, d)));
        }

        public static float Median3(float a, float b, float c)
        {
            return Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));
        }

        // Number of taps of the blur for a given strength: enough for the
        // discs of the lights to look filled at any strength.
        public static int Taps(int blurStrength)
        {
            var s = DioramaSettings.Clamp(blurStrength, DioramaSettings.BlurStrengthMin, DioramaSettings.BlurStrengthMax);
            return 24 + (int)Math.Round(0.72 * s);
        }

        public static float MaxRadiusShare(int blurStrength)
        {
            var s = DioramaSettings.Clamp(blurStrength, DioramaSettings.BlurStrengthMin, DioramaSettings.BlurStrengthMax);
            return RadiusAtFullStrength * s / 100f;
        }
    }
}
