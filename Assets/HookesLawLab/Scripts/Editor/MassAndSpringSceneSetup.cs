#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using UnityEngine.XR.Interaction.Toolkit.UI;
using TMPro;
using HookesLawLab.Lab;
using HookesLawLab.Narration;

namespace HookesLawLab.Editor
{
    /// <summary>
    /// Automatically configures 'Assets/Scenes/mass and spring.unity' with the Anam.glb environment,
    /// XR Origin rig, EventSystem (XR + Mouse), and HookesLawLabBootstrap once glTFast has imported Anam.glb.
    /// Also accessible manually via Tools -> Hooke's Law Lab -> Setup Mass and Spring Scene.
    /// </summary>
    [InitializeOnLoad]
    public static class MassAndSpringSceneSetup
    {
        private const string TargetScenePath = "Assets/Scenes/mass and spring.unity";
        private const string AnamModelPath = "Assets/HookesLawLab/Models/Anam.glb";
        private const string VideoClipPath = "Assets/HookesLawLab/Videos/hookes_law_demo.webm";
        private const string FontAssetPath = "Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular SDF.asset";
        private const string CatalogueAssetPath = "Assets/HookesLawLab/Resources/GuidedNarrationCatalogue.asset";
        private const string XrOriginHandsPrefabPath = "Assets/VRTemplateAssets/Prefabs/Setup/Complete XR Origin Set Up Hands Variant.prefab";
        private const string XrOriginFallbackPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";

        static MassAndSpringSceneSetup()
        {
            EditorApplication.delayCall += TryAutoSetupIfNeeded;
        }

        private class ModelImportWatcher : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
            {
                foreach (string path in importedAssets)
                {
                    if (path == AnamModelPath)
                    {
                        EditorApplication.delayCall += TryAutoSetupIfNeeded;
                        break;
                    }
                }
            }
        }

        private static void TryAutoSetupIfNeeded()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            // Always ensure the serialized GuidedNarrationCatalogue exists under Resources
            // so Unity includes assigned offline Quest AudioClips in Android builds.
            GuidedNarrationCatalogue catalogue = EnsureNarrationCatalogueAsset();

            if (!File.Exists(TargetScenePath))
                return;

            string sceneYaml = File.ReadAllText(TargetScenePath);
            if (sceneYaml.Contains("HookesLawLabManager"))
            {
                // If scene is currently open and bootstrap lacks catalogue ref, wire it non-destructively
                Scene activeScene = SceneManager.GetActiveScene();
                if (activeScene.path == TargetScenePath)
                {
                    HookesLawLabBootstrap existingBootstrap = Object.FindFirstObjectByType<HookesLawLabBootstrap>();
                    if (existingBootstrap != null && existingBootstrap.guidedNarrationCatalogue == null && catalogue != null)
                    {
                        existingBootstrap.guidedNarrationCatalogue = catalogue;
                        EditorUtility.SetDirty(existingBootstrap);
                        EditorSceneManager.MarkSceneDirty(activeScene);
                        EditorSceneManager.SaveScene(activeScene);
                    }
                }
                return;
            }

            GameObject anamPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AnamModelPath);
            if (anamPrefab == null)
            {
                // glTFast is still resolving/importing Anam.glb; ModelImportWatcher will re-trigger once imported.
                return;
            }

            SetupMassAndSpringScene(interactiveConfirm: false);
        }

        [MenuItem("Tools/Hooke's Law Lab/Create or Refresh Narration Catalogue")]
        public static void RefreshNarrationCatalogueFromMenu()
        {
            GuidedNarrationCatalogue catalogue = EnsureNarrationCatalogueAsset();
            if (catalogue != null)
            {
                Selection.activeObject = catalogue;
                EditorGUIUtility.PingObject(catalogue);
                var missing = catalogue.GetMissingClipIds();
                Debug.Log($"[HookesLawLab] GuidedNarrationCatalogue ready at {CatalogueAssetPath}. Missing audio clips: {missing.Count}/{catalogue.entries.Count}.");
            }
        }

        [MenuItem("Tools/Hooke's Law Lab/Test Narration/Log Audio & Clip Diagnostics")]
        public static void LogNarrationDiagnosticsFromMenu()
        {
            GuidedNarrationCatalogue catalogue = EnsureNarrationCatalogueAsset();
            if (catalogue != null)
            {
                var missing = catalogue.GetMissingClipIds();
                int present = catalogue.entries.Count - missing.Count;
                Debug.Log($"[HookesLawLab][TestNarration] Catalogue entries={catalogue.entries.Count}, Playable clips={present}, Missing={missing.Count}: {string.Join(", ", missing)}");
            }

            if (Application.isPlaying)
            {
                var mgr = Object.FindFirstObjectByType<GuidedExperimentManager>();
                if (mgr != null) mgr.LogFullDiagnostics();
                else Debug.LogWarning("[HookesLawLab][TestNarration] No GuidedExperimentManager found in playing scene.");
            }
        }

        [MenuItem("Tools/Hooke's Law Lab/Test Narration/Test Play First Assigned Guide Clip (Play Mode)")]
        public static void TestPlayFirstClipFromMenu()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[HookesLawLab][TestNarration] Enter Play Mode first to test audio playback through GuidedExperimentManager.");
                return;
            }
            var mgr = Object.FindFirstObjectByType<GuidedExperimentManager>();
            if (mgr == null)
            {
                Debug.LogWarning("[HookesLawLab][TestNarration] No GuidedExperimentManager found in active scene.");
                return;
            }
            mgr.TestPlayFirstAssignedClip();
        }

        [MenuItem("Tools/Hooke's Law Lab/Test Narration/Test Play Welcome Narration (Play Mode)")]
        public static void TestPlayWelcomeFromMenu()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[HookesLawLab][TestNarration] Enter Play Mode first to test Welcome narration.");
                return;
            }
            Object.FindFirstObjectByType<GuidedExperimentManager>()?.TestPlayNarrationById("welcome");
        }

        [MenuItem("Tools/Hooke's Law Lab/Test Narration/Test Play Apparatus (Table) Narration (Play Mode)")]
        public static void TestPlayApparatusFromMenu()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[HookesLawLab][TestNarration] Enter Play Mode first to test Apparatus narration.");
                return;
            }
            Object.FindFirstObjectByType<GuidedExperimentManager>()?.TestPlayNarrationById("apparatus");
        }

        [MenuItem("Tools/Hooke's Law Lab/Test Narration/Reset Narration Progress (Play Mode)")]
        public static void ResetNarrationFromMenu()
        {
            if (!Application.isPlaying) return;
            Object.FindFirstObjectByType<GuidedExperimentManager>()?.ResetGuideProgress();
        }

        public static GuidedNarrationCatalogue EnsureNarrationCatalogueAsset()
        {
            string resDir = Path.GetDirectoryName(CatalogueAssetPath);
            if (!string.IsNullOrEmpty(resDir) && !Directory.Exists(resDir))
                Directory.CreateDirectory(resDir);

            string guideAudioDir = "Assets/HookesLawLab/Resources/Guide";
            if (!Directory.Exists(guideAudioDir))
                Directory.CreateDirectory(guideAudioDir);

            GuidedNarrationCatalogue catalogue = AssetDatabase.LoadAssetAtPath<GuidedNarrationCatalogue>(CatalogueAssetPath);
            if (catalogue == null)
            {
                catalogue = ScriptableObject.CreateInstance<GuidedNarrationCatalogue>();
                GuidedNarrationCatalogue.PopulateDefaultEntries(catalogue);
                AssetDatabase.CreateAsset(catalogue, CatalogueAssetPath);
                AssetDatabase.SaveAssets();
            }
            else if (catalogue.entries == null || catalogue.entries.Count == 0)
            {
                GuidedNarrationCatalogue.PopulateDefaultEntries(catalogue);
                EditorUtility.SetDirty(catalogue);
                AssetDatabase.SaveAssets();
            }
            else
            {
                GuidedNarrationCatalogue.PopulateDefaultEntries(catalogue);
                EditorUtility.SetDirty(catalogue);
                AssetDatabase.SaveAssets();
            }

            return catalogue;
        }

        [MenuItem("Tools/Hooke's Law Lab/Setup Mass and Spring Scene")]
        public static void SetupFromMenu()
        {
            SetupMassAndSpringScene(interactiveConfirm: true);
        }

        public static void SetupMassAndSpringScene(bool interactiveConfirm)
        {
            if (!File.Exists(TargetScenePath))
            {
                Debug.LogError($"[HookesLawLab] Target scene not found: {TargetScenePath}");
                return;
            }

            GameObject anamPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AnamModelPath);
            if (anamPrefab == null)
            {
                Debug.LogWarning($"[HookesLawLab] Anam.glb not yet imported as a Prefab at {AnamModelPath}. Ensure com.unity.cloud.gltfast is installed and wait for import.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Scene scene = EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);

            // Clean previous default camera or previous lab objects if re-running
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Main Camera" ||
                    root.name == "Anam" ||
                    root.name == "HookesLawLabManager" ||
                    root.name.StartsWith("Complete XR Origin") ||
                    root.name.StartsWith("XR Origin") ||
                    root.name == "EventSystem")
                {
                    Object.DestroyImmediate(root);
                }
            }

            // 1. Instantiate Anam.glb Laboratory Environment at Origin
            GameObject anamInstance = (GameObject)PrefabUtility.InstantiatePrefab(anamPrefab, scene);
            anamInstance.name = "Anam";
            anamInstance.transform.position = Vector3.zero;
            anamInstance.transform.rotation = Quaternion.identity;
            anamInstance.transform.localScale = Vector3.one;

            // 2. Instantiate XR Origin at shared laboratory entrance spawn facing the main experiment table
            HookesLawLabBootstrap.TryComputeEntranceSpawn(anamInstance, out Vector3 spawnPos, out Vector3 lookAtTarget);
            Vector3 flatForward = new Vector3(lookAtTarget.x - spawnPos.x, 0f, lookAtTarget.z - spawnPos.z);
            if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.back;
            Quaternion spawnHeading = Quaternion.LookRotation(flatForward.normalized, Vector3.up);

            GameObject xrPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(XrOriginHandsPrefabPath)
                                  ?? AssetDatabase.LoadAssetAtPath<GameObject>(XrOriginFallbackPrefabPath);
            if (xrPrefab != null)
            {
                GameObject xrInstance = (GameObject)PrefabUtility.InstantiatePrefab(xrPrefab, scene);
                xrInstance.transform.position = spawnPos;
                xrInstance.transform.rotation = spawnHeading;
            }
            else
            {
                GameObject camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
                camGo.transform.position = spawnPos + Vector3.up * 1.72755f;
                camGo.transform.LookAt(lookAtTarget);
            }

            // 3. Ensure EventSystem with XRUIInputModule supports BOTH XR Rays and Desktop Mouse
            EventSystem ev = Object.FindFirstObjectByType<EventSystem>();
            if (ev == null)
            {
                GameObject evGo = new GameObject("EventSystem");
                ev = evGo.AddComponent<EventSystem>();
            }
            XRUIInputModule xrui = ev.GetComponent<XRUIInputModule>();
            if (xrui == null) xrui = ev.gameObject.AddComponent<XRUIInputModule>();
            xrui.enableXRInput = true;
            xrui.enableMouseInput = true;
            xrui.enableTouchInput = true;
            xrui.enableBuiltinActionsAsFallback = true;

            // 4. Create HookesLawLabManager with HookesLawLabBootstrap
            GameObject managerGo = new GameObject("HookesLawLabManager");
            HookesLawLabBootstrap bootstrap = managerGo.AddComponent<HookesLawLabBootstrap>();
            bootstrap.anamModelRoot = anamInstance;
            bootstrap.hookesLawVideoClip = AssetDatabase.LoadAssetAtPath<VideoClip>(VideoClipPath);
            bootstrap.uiFontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            bootstrap.guidedNarrationCatalogue = EnsureNarrationCatalogueAsset();

            // 5. Ensure 'mass and spring.unity' is first & enabled in Build Settings
            var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            buildScenes.RemoveAll(s => s.path == TargetScenePath);
            buildScenes.Insert(0, new EditorBuildSettingsScene(TargetScenePath, true));
            EditorBuildSettings.scenes = buildScenes.ToArray();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[HookesLawLab] 'mass and spring.unity' successfully built and saved with Physics Lab environment & guided narration!");
            if (interactiveConfirm)
            {
                EditorUtility.DisplayDialog(
                    "Physics Lab Ready",
                    "The 'mass and spring' scene has been configured with the Physics Lab environment, XR Origin, physics simulation, interactive masses, TV graph, Planet Gravity Tablet, Board Video Player, Physics Mentor, and Guided Experiment Narration!\n\nPress Play to test!",
                    "OK");
            }
        }
    }
}
#endif
