using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BananaParty.WebSocketRelay.Editor
{
    public static class ServerExporter
    {
        private const string PackageName = "com.bananaparty.websocketrelay";

        [MenuItem("Tools/WebSocket Relay/Export Server")]
        public static void ExportServer()
        {
            string sourceDirectory = GetServerDirectory();
            if (!Directory.Exists(sourceDirectory))
            {
                EditorUtility.DisplayDialog(
                    "Export Server",
                    $"Server folder not found at:\n{sourceDirectory}",
                    "OK");
                return;
            }

            string destinationDirectory = EditorUtility.OpenFolderPanel("Export Server", "", "");
            if (string.IsNullOrEmpty(destinationDirectory))
                return;

            if (Directory.GetFileSystemEntries(destinationDirectory).Length > 0)
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "Export Server",
                    $"The selected folder is not empty:\n{destinationDirectory}\n\nOverwrite existing files?",
                    "Export",
                    "Cancel");

                if (!proceed)
                    return;
            }

            try
            {
                FileUtil.CopyFileOrDirectory(sourceDirectory, destinationDirectory);
                Debug.Log($"Exported relay server to: {destinationDirectory}");
                EditorUtility.DisplayDialog(
                    "Export Server",
                    $"Server exported successfully to:\n{destinationDirectory}",
                    "OK");
                EditorUtility.RevealInFinder(destinationDirectory);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Export Server",
                    $"Failed to export server:\n{exception.Message}",
                    "OK");
            }
        }

        private static string GetServerDirectory()
        {
            UnityEditor.PackageManager.PackageInfo packageInfo = UnityEditor.PackageManager.PackageInfo.FindForPackageName(PackageName);
            if (packageInfo == null)
                throw new InvalidOperationException($"Package not found: {PackageName}");

            return Path.Combine(packageInfo.resolvedPath, "Runtime", "Server~");
        }
    }
}
