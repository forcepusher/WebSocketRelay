using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BananaParty.WebSocketRelay.SoakTest.Editor
{
    /// <summary>
    /// Batch mode entry points for building the soak client, used by Tools/SoakTest/build.mjs.
    /// </summary>
    public static class SoakBuild
    {
        private const string Scene = "Assets/PlaceholderScene.unity";

        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "SoakClient.exe");

        public static void BuildWebGL() => Build(BuildTarget.WebGL, null);

        private static void Build(BuildTarget target, string executableName)
        {
            string outputDirectory = GetArgument("-soakOutput") ?? Path.Combine("Builds", "Soak", target.ToString());
            WebGLCompressionFormat previousCompressionFormat = PlayerSettings.WebGL.compressionFormat;

            // Uncompressed, so any static file server can host the WebGL build.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            try
            {
                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { Scene },
                    locationPathName = executableName == null ? outputDirectory : Path.Combine(outputDirectory, executableName),
                    target = target,
                    options = BuildOptions.None,
                });

                Debug.Log($"Soak build for {target} finished: {report.summary.result}, {report.summary.totalErrors} errors, {report.summary.totalTime}.");
                if (report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException($"Soak build for {target} failed: {report.summary.result}");
            }
            finally
            {
                PlayerSettings.WebGL.compressionFormat = previousCompressionFormat;
            }
        }

        private static string GetArgument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, name);
            return index >= 0 && index < arguments.Length - 1 ? arguments[index + 1] : null;
        }
    }
}
