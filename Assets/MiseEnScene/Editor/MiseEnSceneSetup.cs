using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bauprobe.MiseEnScene.EditorTools
{
    /// <summary>
    /// Menu « Bauprobe » : génère la scène de l'application et le catalogue de décors.
    /// La scène est construite par code pour être reproductible (et facile à régénérer
    /// si quelqu'un la casse) : relancer le menu la recrée à l'identique.
    /// </summary>
    public static class MiseEnSceneSetup
    {
        const string Root = "Assets/MiseEnScene";
        const string ScenePath = Root + "/Scenes/MiseEnScene.unity";
        const string CatalogPath = Root + "/Resources/DecorCatalog.asset";
        const string MaterialsDir = Root + "/Materials";
        const string DecorPrefabsDir = "Assets/Prefab/ObjetsImportes";

        [MenuItem("Bauprobe/1. Créer la scène Mise en scène", priority = 1)]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var rigPrefab = FindPrefab("OVRCameraRig");
            if (rigPrefab == null)
            {
                EditorUtility.DisplayDialog("Bauprobe",
                    "Prefab OVRCameraRig introuvable. Le package Meta XR Core SDK (com.meta.xr.sdk.core) est-il installé ?",
                    "OK");
                return;
            }

            var unlit = CreateMaterial("MES_Lignes", "Universal Render Pipeline/Unlit", Color.white);
            var lit = CreateMaterial("MES_Decor", "Universal Render Pipeline/Lit", new Color(0.85f, 0.85f, 0.85f));
            var catalog = UpdateCatalog();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- Casque : caméra + passthrough (on voit la salle réelle) ---
            var rigGo = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);
            rigGo.name = "OVRCameraRig";
            var ovrManager = rigGo.GetComponent<OVRManager>();
            if (ovrManager == null) ovrManager = rigGo.AddComponent<OVRManager>();
            ovrManager.isInsightPassthroughEnabled = true;
            var passthrough = rigGo.AddComponent<OVRPassthroughLayer>();
            passthrough.overlayType = OVROverlay.OverlayType.Underlay;
            foreach (var cam in rigGo.GetComponentsInChildren<Camera>(true))
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            }
            var rig = rigGo.GetComponent<OVRCameraRig>();

            var lightGo = new GameObject("Lumière");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // --- Repère du plateau (le décor est rangé dessous) ---
            var stageGo = new GameObject("Plateau");
            var stage = stageGo.AddComponent<StageFrame>();
            var decorRoot = new GameObject("Décor").transform;
            decorRoot.SetParent(stageGo.transform, false);
            Set(stage, "decorRoot", decorRoot);
            var gizmo = stageGo.AddComponent<StageGizmo>();
            Set(gizmo, "lineMaterial", unlit);

            // --- Logique de l'application ---
            var app = new GameObject("MiseEnScene");
            var manager = app.AddComponent<DecorManager>();
            Set(manager, "catalog", catalog);
            Set(manager, "stage", stage);
            Set(manager, "defaultMaterial", lit);
            Set(manager, "lineMaterial", unlit);

            var persistence = app.AddComponent<DecorPersistence>();
            Set(persistence, "manager", manager);

            var calibrator = app.AddComponent<StageCalibrator>();
            Set(calibrator, "rig", rig);
            Set(calibrator, "stage", stage);
            Set(calibrator, "markerMaterial", unlit);

            var controller = app.AddComponent<DirectorController>();
            Set(controller, "rig", rig);
            Set(controller, "decor", manager);
            Set(controller, "stage", stage);
            Set(controller, "calibrator", calibrator);
            Set(controller, "gizmo", gizmo);
            Set(controller, "rayMaterial", unlit);

            var hud = app.AddComponent<DirectorHud>();
            Set(hud, "rig", rig);
            Set(hud, "controller", controller);
            Set(hud, "stage", stage);
            Set(hud, "calibrator", calibrator);
            Set(hud, "decor", manager);
            Set(hud, "backgroundMaterial", unlit);

            EnsureFolder(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();

            Debug.Log($"[Bauprobe] Scène créée : {ScenePath} (seule scène du build).");
        }

        /// <summary>
        /// Crée le catalogue s'il n'existe pas et y ajoute les primitives de base et les prefabs
        /// de Assets/Prefab/ObjetsImportes. Les entrées existantes (et leurs réglages) sont conservées.
        /// </summary>
        [MenuItem("Bauprobe/2. Mettre à jour le catalogue de décors", priority = 2)]
        public static DecorCatalog UpdateCatalog()
        {
            EnsureFolder(Path.GetDirectoryName(CatalogPath));
            var catalog = AssetDatabase.LoadAssetAtPath<DecorCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<DecorCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            AddPrimitive(catalog, "praticable", "Praticable 2 x 1 m", PrimitiveType.Cube, new Vector3(2f, 0.4f, 1f));
            AddPrimitive(catalog, "cube", "Cube 1 m", PrimitiveType.Cube, Vector3.one);
            AddPrimitive(catalog, "ferme", "Ferme / mur 3 x 2,5 m", PrimitiveType.Cube, new Vector3(3f, 2.5f, 0.1f));
            AddPrimitive(catalog, "colonne", "Colonne 3 m", PrimitiveType.Cylinder, new Vector3(0.4f, 1.5f, 0.4f));
            AddPrimitive(catalog, "sphere", "Sphère 1 m", PrimitiveType.Sphere, Vector3.one);

            if (AssetDatabase.IsValidFolder(DecorPrefabsDir))
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { DecorPrefabsDir }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    RemoveMissingScripts(path);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var id = "prefab-" + Path.GetFileNameWithoutExtension(path).ToLowerInvariant().Replace(' ', '-');
                    if (catalog.Find(id) != null) continue;
                    catalog.entries.Add(new DecorCatalog.Entry { id = id, displayName = prefab.name, prefab = prefab });
                }
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Bauprobe] Catalogue à jour : {catalog.Count} élément(s) dans {CatalogPath}.");
            return catalog;
        }

        static void AddPrimitive(DecorCatalog catalog, string id, string displayName, PrimitiveType type, Vector3 scale)
        {
            if (catalog.Find(id) != null) return;
            catalog.entries.Add(new DecorCatalog.Entry
            {
                id = id,
                displayName = displayName,
                primitive = type,
                primitiveScale = scale,
            });
        }

        // Les prefabs importés de l'ancien projet portaient des scripts XRI supprimés depuis.
        static void RemoveMissingScripts(string prefabPath)
        {
            var contents = PrefabUtility.LoadPrefabContents(prefabPath);
            var removed = 0;
            foreach (var t in contents.GetComponentsInChildren<Transform>(true))
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            if (removed > 0)
            {
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
                Debug.Log($"[Bauprobe] {removed} script(s) manquant(s) retiré(s) de {prefabPath}.");
            }
            PrefabUtility.UnloadPrefabContents(contents);
        }

        static Material CreateMaterial(string name, string shaderName, Color color)
        {
            EnsureFolder(MaterialsDir);
            var path = $"{MaterialsDir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[Bauprobe] Shader '{shaderName}' introuvable (URP est-il installé ?).");
                return null;
            }
            var material = new Material(shader);
            material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static GameObject FindPrefab(string prefabName)
        {
            foreach (var guid in AssetDatabase.FindAssets($"{prefabName} t:Prefab", new[] { "Assets", "Packages" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == prefabName)
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            return null;
        }

        static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"[Bauprobe] Champ '{field}' introuvable sur {target.GetType().Name}.");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureFolder(string folder)
        {
            folder = folder.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
