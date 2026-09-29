using System;
using System.IO;
using System.Xml.Serialization;

namespace SirDiorama
{
    // Reads and writes the settings. A missing or unreadable file never blocks
    // the game: the defaults are used instead.
    public static class SettingsFile
    {
        public const string FileName = "settings.xml";

        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(LookSettings));

        public static LookSettings Load(string path, out string problem)
        {
            problem = null;
            if (!File.Exists(path))
                return new LookSettings();

            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var read = Serializer.Deserialize(stream) as LookSettings;
                    if (read == null)
                    {
                        problem = "empty settings file";
                        return new LookSettings();
                    }
                    return read.Normalized();
                }
            }
            catch (Exception e)
            {
                problem = "unreadable settings file (" + e.Message + ")";
                return new LookSettings();
            }
        }

        public static void Save(string path, LookSettings settings)
        {
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            // Written next to the file, then swapped in: a crash of the game
            // never leaves a half written settings file behind.
            var temporary = path + ".tmp";
            using (var stream = File.Create(temporary))
                Serializer.Serialize(stream, settings.Normalized());

            if (File.Exists(path))
                File.Delete(path);
            File.Move(temporary, path);
        }
    }
}
