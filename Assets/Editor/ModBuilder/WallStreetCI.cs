#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace BAModTemplate.Editor
{
    /// <summary>Headless entry points so the mod can be imported and compiled from the CLI.</summary>
    public static class WallStreetCI
    {
        private const string InstallPath = @"C:\Program Files (x86)\Steam\steamapps\common\Big Ambitions";

        public static void ImportGameDlls()
        {
            Debug.Log("[CI] Importing game DLLs from: " + InstallPath);
            GameDllImporter.SetConfiguredInstallPath(InstallPath);
            GameDllImporter.Import(InstallPath);
            AssetDatabase.Refresh();
            Debug.Log("[CI] Import call completed.");
        }

        public static void ReportCompile()
        {
            Debug.Log("[CI] Compile report requested.");
        }

        /// <summary>Build + install the WallStreet mod headlessly. Run WITHOUT -quit.</summary>
        public static void BuildAndInstall()
        {
            DiscoveredMod? target = null;
            foreach (var m in ModDiscovery.DiscoverAll())
            {
                Debug.Log("[CI] discovered mod: " + m.Manifest.ModId);
                if (m.Manifest.ModId == "WallStreet") target = m;
            }

            if (target == null)
            {
                Debug.Log("[CI] ERROR WallStreet mod was not discovered.");
                EditorApplication.Exit(2);
                return;
            }

            ModPackager.JobChanged += job =>
            {
                Debug.Log("[CI] job " + job.State + " :: " + job.StatusText);
                if (job.State == BuildState.Done)
                {
                    Debug.Log("[CI] BUILD DONE");
                    EditorApplication.Exit(0);
                }
                else if (job.State == BuildState.Failed)
                {
                    Debug.Log("[CI] BUILD FAILED :: " + job.StatusText);
                    EditorApplication.Exit(3);
                }
            };

            ModPackager.Enqueue(target, installAfterBuild: true);
            Debug.Log("[CI] enqueued build job");
        }
    }
}
#endif
