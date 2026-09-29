using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Xunit;

namespace SirDiorama.Tests
{
    // Stands for VRageRender.MyToneMapping, the game's final colour pass,
    // which the tests cannot load: same method name, same place in the story.
    public static class ToneMappingStandIn
    {
        public static int Calls;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Run()
        {
            Calls++;
        }
    }

    public static class OtherPluginPatch
    {
        public static void Prefix()
        {
        }
    }

    public class CoexistenceTests
    {
        private const string Own = "sir-diorama";
        private const string Step = "MyToneMapping.Run";

        // Another plugin (here Sir Cel Shading) is already hooked on the final
        // colour pass, through Harmony: Sir Diorama reads the owners of the
        // method as the game side does (Harmony.GetPatchInfo), yields, stops
        // for the session without touching anything, writes one line to the
        // game log, and tells the player which plugin keeps the step.
        [Fact]
        public void YieldsWhenAnotherPluginPatchedToneMapping()
        {
            var method = typeof(ToneMappingStandIn).GetMethod("Run");
            var other = new Harmony("sir-cel-shading");
            other.Patch(method, prefix: new HarmonyMethod(typeof(OtherPluginPatch).GetMethod("Prefix")));
            try
            {
                var log = new List<string>();
                var stop = new SessionStop(log.Add);
                var coexistence = new Coexistence(Own, stop);

                var info = Harmony.GetPatchInfo(method);
                Assert.NotNull(info);
                Assert.True(coexistence.YieldIfTaken(info.Owners, Step));

                Assert.True(stop.IsStopped);
                var line = Assert.Single(log);
                Assert.Contains("sir-cel-shading", line);
                Assert.Contains(Step, line);

                var shown = new List<string>();
                stop.Deliver(true, shown.Add);
                var notice = Assert.Single(shown);
                Assert.StartsWith(Texts.StopPrefix, notice);
                Assert.Contains("sir-cel-shading", notice);
                Assert.Contains("yields", notice);
                Assert.Equal(notice, stop.Reason);
            }
            finally
            {
                other.UnpatchAll("sir-cel-shading");
            }
        }

        // Our own patch alone is not another plugin: the step is ours.
        [Fact]
        public void KeepsTheStepWhenOnlyItsOwnPatchIsThere()
        {
            var method = typeof(ToneMappingStandIn).GetMethod("Run");
            var own = new Harmony(Own);
            own.Patch(method, prefix: new HarmonyMethod(typeof(OtherPluginPatch).GetMethod("Prefix")));
            try
            {
                var stop = new SessionStop(null);
                var coexistence = new Coexistence(Own, stop);
                Assert.False(coexistence.YieldIfTaken(Harmony.GetPatchInfo(method).Owners, Step));
                Assert.False(stop.IsStopped);
            }
            finally
            {
                own.UnpatchAll(Own);
            }
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
