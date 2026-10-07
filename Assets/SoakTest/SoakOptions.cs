using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace BananaParty.WebSocketRelay.SoakTest
{
    /// <summary>
    /// Soak run settings, read from "-soakName value" command line pairs or from "soakName=value" URL query parameters on WebGL.
    /// </summary>
    public sealed class SoakOptions
    {
        private readonly Dictionary<string, string> _values;

        private SoakOptions(Dictionary<string, string> values)
        {
            _values = values;
        }

        public string RelayAddress => _values["relay"];

        public string CollectorUrl => GetString("collector", null);

        public int Index => GetInt("index", 0);

        public int TargetFrameRate => GetInt("fps", 60);

        public bool UseBinary => GetInt("binary", 0) != 0;

        public float RpcRate => GetFloat("rpcrate", 5f);

        public int PaddingBytes => GetInt("padding", 256);

        public float PlayerTimeoutSeconds => GetFloat("playertimeout", 10f);

        /// <returns>Null when the process was not started for a soak run.</returns>
        public static SoakOptions Parse()
        {
            Dictionary<string, string> values = new();
            ReadCommandLine(values);
            ReadUrlQuery(values);
            return values.ContainsKey("relay") ? new SoakOptions(values) : null;
        }

        private static void ReadCommandLine(Dictionary<string, string> values)
        {
            string[] arguments;
            try
            {
                arguments = Environment.GetCommandLineArgs();
            }
            catch (Exception)
            {
                return;
            }

            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (arguments[index].StartsWith("-soak", StringComparison.Ordinal))
                    values[arguments[index].Substring(5).ToLowerInvariant()] = arguments[index + 1];
            }
        }

        private static void ReadUrlQuery(Dictionary<string, string> values)
        {
            string url = Application.absoluteURL;
            int queryStart = string.IsNullOrEmpty(url) ? -1 : url.IndexOf('?');
            if (queryStart < 0)
                return;

            foreach (string pair in url.Substring(queryStart + 1).Split('&'))
            {
                int separator = pair.IndexOf('=');
                if (separator <= 0 || !pair.StartsWith("soak", StringComparison.Ordinal))
                    continue;

                values[pair.Substring(4, separator - 4).ToLowerInvariant()] = Uri.UnescapeDataString(pair.Substring(separator + 1));
            }
        }

        private string GetString(string key, string defaultValue) => _values.TryGetValue(key, out string value) ? value : defaultValue;

        private int GetInt(string key, int defaultValue) =>
            _values.TryGetValue(key, out string value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : defaultValue;

        private float GetFloat(string key, float defaultValue) =>
            _values.TryGetValue(key, out string value) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) ? result : defaultValue;
    }
}
