using System;
using System.Linq;
using Xunit;

namespace SirDiorama.Tests
{
    public class FocusTests
    {
        [Fact]
        public void WhatIsLookedAtIsSharp()
        {
            // An LCD panel at 1.5 m, looked at: no blur at all, even with its
            // edges a few percent further or nearer.
            Assert.Equal(0f, Focus.BlurRadius(1.5f, 1.5f, 20f));
            Assert.Equal(0f, Focus.BlurRadius(1.55f, 1.5f, 20f));
            Assert.Equal(0f, Focus.BlurRadius(1.45f, 1.5f, 20f));
        }

        [Fact]
        public void TheForegroundAndTheDistanceAreBlurred()
        {
            const float max = 20f;
            var focus = 5f;
            Assert.True(Focus.BlurRadius(2.5f, focus, max) > 0.9f * max);
            Assert.True(Focus.BlurRadius(10f, focus, max) > 0.5f * max);
            Assert.Equal(max, Focus.BlurRadius(Focus.Far, focus, max), 3);
        }

        [Fact]
        public void TheBlurGrowsWithTheGapToTheFocus()
        {
            var previous = 0f;
            foreach (var d in new[] { 5f, 6f, 8f, 12f, 30f, 1000f })
            {
                var r = Focus.BlurRadius(d, 5f, 20f);
                Assert.True(r >= previous);
                previous = r;
            }
        }

        [Fact]
        public void OnlyRatiosOfDistancesCount()
        {
            // The same scene at one metre or at ten kilometres looks the same.
            Assert.Equal(Focus.BlurRadius(2f, 1f, 20f), Focus.BlurRadius(20000f, 10000f, 20f), 4);
            Assert.Equal(Focus.BlurRadius(0.5f, 1f, 20f), Focus.BlurRadius(5000f, 10000f, 20f), 4);
        }

        [Fact]
        public void TheFocusFollowsTheViewWithoutAJump()
        {
            // From 2 m to 1 km: every frame at 60 fps moves only part of the
            // way, and the focus gets there within about two seconds.
            var focus = (float)Math.Log(2, 2);
            var target = (float)Math.Log(1000, 2);
            var largestStep = 0f;
            for (var frame = 0; frame < 120; frame++)
            {
                var next = Focus.Follow(focus, true, target, 1 / 60f);
                largestStep = Math.Max(largestStep, Math.Abs(next - focus));
                Assert.True(next <= target);
                focus = next;
            }
            Assert.True(largestStep < 0.1f * (target - (float)Math.Log(2, 2)));
            Assert.True(target - focus < 0.05f);
        }

        [Fact]
        public void TheFocusSpeedDoesNotDependOnTheFrameRate()
        {
            var at30 = 0f;
            var at120 = 0f;
            for (var i = 0; i < 15; i++) at30 = Focus.Follow(at30, true, 10f, 1 / 30f);
            for (var i = 0; i < 60; i++) at120 = Focus.Follow(at120, true, 10f, 1 / 120f);
            Assert.Equal(at30, at120, 3);
        }

        [Fact]
        public void WithoutAPreviousFocusItGoesStraightToTheTarget()
        {
            Assert.Equal(7f, Focus.Follow(0f, false, 7f, 1 / 60f));
            Assert.Equal(7f, Focus.Follow(float.NaN, true, 7f, 1 / 60f));
            Assert.Equal(3f, Focus.Follow(3f, true, 7f, 0f));
        }

        [Fact]
        public void TheMedianOfFiveIsRight()
        {
            var random = new Random(12);
            for (var i = 0; i < 2000; i++)
            {
                var v = Enumerable.Range(0, 5).Select(_ => (float)random.NextDouble()).ToArray();
                var expected = v.OrderBy(x => x).ElementAt(2);
                Assert.Equal(expected, Focus.Median5(v[0], v[1], v[2], v[3], v[4]));
            }
        }

        [Fact]
        public void AStrongerBlurHasLargerDiscsAndMoreTaps()
        {
            Assert.Equal(0f, Focus.MaxRadiusShare(0));
            Assert.True(Focus.MaxRadiusShare(100) > Focus.MaxRadiusShare(50));
            Assert.True(Focus.Taps(100) > Focus.Taps(50));
            Assert.Equal(60, Focus.Taps(DioramaSettings.BlurStrengthDefault));
        }
    }
}
