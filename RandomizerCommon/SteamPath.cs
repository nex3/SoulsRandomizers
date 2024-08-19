using Gameloop.Vdf;
using Gameloop.Vdf.Linq;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RandomizerCommon
{
    public class SteamPath
    {
        /// <summary>
        /// A list of all library directories in which the user might have games installed.
        /// </summary>
        private static Lazy<List<string>> libraries = new(() =>
        {
            var steam = (string)Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Valve\Steam",
                "SteamPath",
                null
            ) ?? throw new SteamPathException("Steam is not installed.");
            var libraries = new List<string>(new[] { steam });

            VProperty vdf;
            try
            {
                vdf = VdfConvert.Deserialize(
                    File.ReadAllText($@"{steam}\SteamApps\libraryfolders.vdf"));
            }
            catch (IOException e)
            {
                throw new SteamPathException(
                    "Steam installation doesn't contain libraryfolders.vdf.", e);
            }

            libraries.AddRange(
                vdf.Value.Cast<VProperty>().Select(child => child.Value["path"].Value<string>()));
            return libraries;
        });

        /// <param name="appID">
        /// The numeric ID of the app to find. You can find the ID for a given app on
        /// https://steamdb.info/apps/.
        /// </param>
        /// <returns>
        /// Returns the path on disk to the Steam application with the given
        /// <paramref name="appID"/>, or null if no such app is installed.
        /// </returns>
        public static string Find(string appID)
        {
            var manifest = FindManifest(appID);
            if (manifest == null) return null;

            var vdf = VdfConvert.Deserialize(File.ReadAllText(manifest));
            var appDir = Path.GetDirectoryName(manifest) + @"\common\" +
                vdf.Value["installdir"].Value<string>();
            if (!Directory.Exists(appDir)) return null;
            return appDir;
        }

        /// <param name="appID">
        /// The numeric ID of the app to find. You can find the ID for a given app on
        /// https://steamdb.info/apps/.
        /// </param>
        /// <returns>The manifest ACF file for the given <paramref name="appID"/>.</returns>
        private static string FindManifest(string appID)
        {
            var name = $@"appmanifest_{appID}.acf";
            foreach (var library in libraries.Value)
            {
                var path = $@"{library}\steamapps\{name}";
                if (File.Exists(path)) return path;
            }

            return null;
        }

        /// <summary>An exception thrown when discovering an application's path fails.</summary>
        public class SteamPathException : Exception
        {
            public SteamPathException() : base() { }
            public SteamPathException(string message) : base(message) { }
            public SteamPathException(string message, Exception wrapped)
                : base(message, wrapped) { }
        }
    }
}
