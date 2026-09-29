using System;
using System.Collections.Generic;

namespace SirDiorama
{
    // Two plugins never fight over the same step of the game. Before replacing
    // what the final colour pass uses, and regularly afterwards, the plugin
    // looks at who is hooked on it (Harmony.GetPatchInfo): any owner other
    // than itself, and it yields.
    //
    // Yielding stops the effect for the session, through SessionStop: the game
    // keeps its own rendering (or the other plugin's), one line goes to the
    // game log and the player is told which plugin keeps the step.
    public sealed class Coexistence
    {
        private readonly string m_ownId;
        private readonly SessionStop m_stop;

        public Coexistence(string ownId, SessionStop stop)
        {
            m_ownId = ownId;
            m_stop = stop;
        }

        public static List<string> OtherOwners(IEnumerable<string> owners, string ownId)
        {
            var others = new List<string>();
            if (owners == null)
                return others;

            foreach (var owner in owners)
            {
                if (string.IsNullOrEmpty(owner))
                    continue;
                if (string.Equals(owner, ownId, StringComparison.Ordinal))
                    continue;
                if (!others.Contains(owner))
                    others.Add(owner);
            }
            return others;
        }

        // True if the step is held by another plugin: the effect has then been
        // stopped and the player warned. False when the step is free (or ours).
        public bool YieldIfTaken(IEnumerable<string> ownersOfTheStep, string stepName)
        {
            var others = OtherOwners(ownersOfTheStep, m_ownId);
            if (others.Count == 0)
                return false;

            var names = string.Join(", ", others.ToArray());
            m_stop.Stop(stepName + " is already patched by " + names + ": yielding",
                Texts.StopCoexistence(names));
            return true;
        }
    }
}
