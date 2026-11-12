using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Dodgeball.EditorTools
{
    /// <summary>
    /// Builds the game for the browser into "dodgeball/WebBuild".
    /// From Unity: Tools > Dodgeball > 6. Build Web Version.
    /// From the command line: Unity.exe -batchmode -projectPath dodgeball -buildTarget WebGL
    ///     -executeMethod Dodgeball.EditorTools.WebBuilder.BuildWeb -logFile build.log
    /// </summary>
    public static class WebBuilder
    {
        [MenuItem("Tools/Dodgeball/6. Build Web Version")]
        public static void BuildWeb()
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "WebBuild"));

            // Gzip + decompression fallback: small files that still load on any host
            // (itch.io, GitHub Pages, a simple local server) without special server settings.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;

            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"[Dodgeball] Web build {summary.result}: {outDir} " +
                      $"({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalMinutes:0.0} min, {summary.totalErrors} errors)");

            if (Application.isBatchMode)
                EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
