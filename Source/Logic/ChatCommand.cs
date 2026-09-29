using System;

namespace SirDiorama
{
    public enum CommandAction
    {
        // The message is not for the plugin: it is sent normally.
        None,
        Toggle,
        On,
        Off,
        Status,
        SetHotkey,
        Unknown,
    }

    // The player's command in the chat window. It stays on his machine:
    // nothing goes to the server nor to the other players.
    public static class ChatCommand
    {
        public const string Prefix = "/diorama";

        public static CommandAction Parse(string text, out string argument)
        {
            argument = null;
            if (text == null)
                return CommandAction.None;

            var t = text.Trim();
            if (!t.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                return CommandAction.None;

            var rest = t.Substring(Prefix.Length);
            if (rest.Length > 0 && !char.IsWhiteSpace(rest[0]))
                return CommandAction.None; // "/dioramas" is not our command

            rest = rest.Trim();
            var space = rest.IndexOf(' ');
            var verb = (space < 0 ? rest : rest.Substring(0, space)).ToLowerInvariant();
            var tail = space < 0 ? "" : rest.Substring(space + 1).Trim();

            switch (verb)
            {
                case "":
                    return CommandAction.Toggle;
                case "on":
                    return CommandAction.On;
                case "off":
                    return CommandAction.Off;
                case "status":
                    return CommandAction.Status;
                case "key":
                    argument = tail;
                    return CommandAction.SetHotkey;
                default:
                    return CommandAction.Unknown;
            }
        }
    }
}
