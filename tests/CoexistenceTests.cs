using System.Collections.Generic;
using Xunit;

namespace SirDiorama.Tests
{
    public class CohabitationTests
    {
        private const string Own = "sir-diorama";
        private const string Step = "MyToneMapping.Run";

        // Another plugin (here Sir Cel Shading) is already hooked on the final
        // colour pass: Sir Diorama yields, stops for the session without
        // touching anything, writes one line to the game log, and tells the
        // player which plugin keeps the step.
        [Fact]
        public void CedeLaPlaceQuandUnAutreGreffonTientLEtape()
        {
            var log = new List<string>();
            var stop = new SessionStop(log.Add);
            var coexistence = new Coexistence(Own, stop);

            Assert.True(coexistence.YieldIfTaken(new[] { "sir-cel-shading" }, Step));

            Assert.True(stop.IsStopped);
            Assert.Single(log);
            Assert.Contains("sir-cel-shading", log[0]);

            var shown = new List<string>();
            stop.Deliver(true, shown.Add);
            var notice = Assert.Single(shown);
            Assert.StartsWith(Texts.StopPrefix, notice);
            Assert.Contains("sir-cel-shading", notice);
            Assert.Contains("yields", notice);
        }

        [Fact]
        public void AFreeStepIsTaken()
        {
            var stop = new SessionStop(null);
            var coexistence = new Coexistence(Own, stop);

            Assert.False(coexistence.YieldIfTaken(null, Step));
            Assert.False(coexistence.YieldIfTaken(new string[0], Step));
            Assert.False(stop.IsStopped);
        }

        [Fact]
        public void OurOwnPatchIsNotAnotherPlugin()
        {
            var stop = new SessionStop(null);
            var coexistence = new Coexistence(Own, stop);

            Assert.False(coexistence.YieldIfTaken(new[] { Own, "", null }, Step));
            Assert.False(stop.IsStopped);
        }

        [Fact]
        public void EachOtherOwnerIsNamedOnce()
        {
            var others = Coexistence.OtherOwners(new[] { Own, "other.plugin", "other.plugin", "", null, "third" }, Own);
            Assert.Equal(new[] { "other.plugin", "third" }, others);
        }

        [Fact]
        public void ThePlayerIsWarnedOnceEvenIfTheCheckRepeats()
        {
            var stop = new SessionStop(null);
            var coexistence = new Coexistence(Own, stop);

            coexistence.YieldIfTaken(new[] { "sir-cel-shading" }, Step);
            coexistence.YieldIfTaken(new[] { "sir-cel-shading" }, Step);

            var shown = new List<string>();
            stop.Deliver(true, shown.Add);
            Assert.Single(shown);
        }
    }

    public class SessionStopTests
    {
        [Fact]
        public void AStopWritesToTheLogAndWarnsThePlayer()
        {
            var log = new List<string>();
            var stop = new SessionStop(log.Add);

            Assert.True(stop.Stop("technical detail", Texts.StopVariantRefused));

            Assert.True(stop.IsStopped);
            Assert.Equal(new[] { "technical detail" }, log);
            Assert.Equal(Texts.StopVariantRefused, stop.Reason);
        }

        [Fact]
        public void InTheMenuNotificationsWaitForAWorld()
        {
            var stop = new SessionStop(null);
            stop.Stop("detail", "message");

            var shown = new List<string>();
            Assert.Equal(0, stop.Deliver(false, shown.Add));
            Assert.Empty(shown);
            Assert.Equal(1, stop.Deliver(true, shown.Add));
            Assert.Equal(0, stop.Deliver(true, shown.Add));
        }

        [Fact]
        public void ASimpleNoticeStopsNothing()
        {
            var stop = new SessionStop(null);
            stop.Notify(Texts.On);

            Assert.False(stop.IsStopped);
            var shown = new List<string>();
            stop.Deliver(true, shown.Add);
            Assert.Equal(new[] { Texts.On }, shown);
        }
    }
}
