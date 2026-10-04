using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ConquestusTerrarum.Client.Editor
{
    public static class MultiplayerBuild
    {
        [MenuItem("Conquestus Terrarum/Build Windows Client")]
        public static void BuildWindows()
        {
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            var output = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Builds", "Windows", "ConquestusTerrarum.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Windows client build failed. Check the Unity build log.");
            Debug.Log("Windows client build succeeded: " + output);
        }
    }
}
