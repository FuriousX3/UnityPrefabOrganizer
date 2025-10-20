using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// An editor window to organize the assets (dependencies) of a prefab.
/// It copies materials, textures, meshes, and animations into subfolders
/// next to the prefab and updates the prefab to use these new copies.
/// </summary>
public class PrefabCleanUpEditor : EditorWindow
{
    private GameObject prefabToClean;

    // Creates the menu item that opens this window
    [MenuItem("Tools/FuriousX/Prefab CleanUp")]
    public static void ShowWindow()
    {
        // Get existing open window or if none, make a new one.
        PrefabCleanUpEditor window = GetWindow<PrefabCleanUpEditor>("Prefab Organizer");
        window.minSize = new Vector2(300, 400);
        window.Show();
    }

    /// <summary>
    /// Renders the GUI for the editor window.
    /// </summary>
    private void OnGUI()
    {
        EditorGUILayout.LabelField("Prefab Asset Organizer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Drag a prefab from your Project files into the field below. " +
            "The 'Organize' button will copy all of its dependent assets (materials, textures, meshes, etc.) " +
            "into neatly named subfolders right next to the prefab asset. Shaders and scripts will not be copied. " +
            "The prefab will be updated to reference the new copies.",
            MessageType.Info);

        EditorGUILayout.Space();

        // Object field for the user to drop the prefab
        GameObject newPrefab = (GameObject)EditorGUILayout.ObjectField("Prefab To Organize", prefabToClean, typeof(GameObject), false);

        // Validate the selection
        if (newPrefab != prefabToClean)
        {
            if (newPrefab != null && PrefabUtility.IsPartOfPrefabAsset(newPrefab))
            {
                // It's a valid prefab asset
                prefabToClean = newPrefab;
            }
            else if (newPrefab != null)
            {
                // It's a scene object or something else, which is invalid
                Debug.LogWarning("Invalid object. Please drag a prefab asset from the Project window.");
                prefabToClean = null;
            }
            else
            {
                // Field was cleared
                prefabToClean = null;
            }
        }

        EditorGUILayout.Space(20);

        // The "Organize" button is only enabled if a valid prefab is selected
        GUI.enabled = prefabToClean != null;
        if (GUILayout.Button("Organize Prefab Assets", GUILayout.Height(40)))
        {
            OrganizePrefab();
        }
        GUI.enabled = true; // Always re-enable GUI
    }

    /// <summary>
    /// The core logic for organizing the selected prefab.
    /// </summary>
    private void OrganizePrefab()
    {
        if (prefabToClean == null)
        {
            EditorUtility.DisplayDialog("Error", "No prefab selected.", "OK");
            return;
        }

        // Get the file path and directory of the selected prefab
        string prefabPath = AssetDatabase.GetAssetPath(prefabToClean);
        string prefabDirectory = Path.GetDirectoryName(prefabPath);

        // Get all dependencies of the prefab
        string[] dependencyPaths = AssetDatabase.GetDependencies(prefabPath, true);
        var assetMap = new Dictionary<Object, Object>();

        // Start progress bar
        EditorUtility.DisplayProgressBar("Organizing Prefab", "Copying assets...", 0f);

        try
        {
            // --- PASS 1: COPY ASSETS and BUILD a map from old assets (and sub-assets) to new assets ---
            for (int i = 0; i < dependencyPaths.Length; i++)
            {
                string path = dependencyPaths[i];
                EditorUtility.DisplayProgressBar("Organizing Prefab", $"Processing: {Path.GetFileName(path)}", (float)i / dependencyPaths.Length);

                if (path == prefabPath) continue;

                Object asset = AssetDatabase.LoadAssetAtPath<Object>(path);

                if (asset == null || asset is MonoScript || asset is Shader || path.StartsWith("Packages/")) continue;

                string subfolder = GetSubfolderForAsset(asset);
                if (string.IsNullOrEmpty(subfolder))
                {
                    if (AssetImporter.GetAtPath(path) is ModelImporter)
                    {
                        subfolder = "Meshes";
                    }
                }

                if (string.IsNullOrEmpty(subfolder) || Path.GetDirectoryName(path).EndsWith(subfolder)) continue;

                string targetFolder = Path.Combine(prefabDirectory, subfolder);
                if (!Directory.Exists(targetFolder))
                {
                    Directory.CreateDirectory(targetFolder);
                }

                string newPath = Path.Combine(targetFolder, Path.GetFileName(path));
                newPath = AssetDatabase.GenerateUniqueAssetPath(newPath);

                if (AssetDatabase.CopyAsset(path, newPath))
                {
                    MapAssetAndSubAssets(path, newPath, assetMap);
                }
                else
                {
                    Debug.LogError($"Failed to copy asset from '{path}' to '{newPath}'.");
                }
            }

            // --- PASS 2: REMAP dependencies WITHIN the newly copied assets (e.g., textures in materials, animations in controllers) ---
            EditorUtility.DisplayProgressBar("Organizing Prefab", "Remapping cross-asset references...", 0.9f);
            foreach (Object newAsset in assetMap.Values)
            {
                if (EditorUtility.IsPersistent(newAsset))
                {
                    RemapObjectReferences(newAsset, assetMap);
                }
            }

            // --- PASS 3: REMAP all references ON the prefab instance ---
            EditorUtility.DisplayProgressBar("Organizing Prefab", "Updating prefab references...", 0.95f);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabToClean);
            Component[] allComponents = instance.GetComponentsInChildren<Component>(true);

            foreach (var component in allComponents)
            {
                if (component == null) continue;
                RemapObjectReferences(component, assetMap);
            }

            // --- PASS 4: SAVE the modified prefab and CLEAN UP ---
            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            DestroyImmediate(instance);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Success", "Prefab organized successfully!", "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }
    
    /// <summary>
    /// Finds all assets and sub-assets at the old path, finds their counterparts
    /// at the new path, and populates the assetMap with the correlations.
    /// </summary>
    private void MapAssetAndSubAssets(string oldPath, string newPath, Dictionary<Object, Object> assetMap)
    {
        Object[] oldAssets = AssetDatabase.LoadAllAssetsAtPath(oldPath);
        Object[] newAssets = AssetDatabase.LoadAllAssetsAtPath(newPath);

        if (oldAssets.Length != newAssets.Length)
        {
            Debug.LogWarning($"Asset count mismatch for {Path.GetFileName(oldPath)}. Remapping may be incomplete.");
        }

        for (int i = 0; i < oldAssets.Length; i++)
        {
            // Find the corresponding new asset by type and name, as order is not guaranteed
            Object correspondingNewAsset = null;
            for(int j=0; j<newAssets.Length; j++)
            {
                if(oldAssets[i].GetType() == newAssets[j].GetType() && oldAssets[i].name == newAssets[j].name)
                {
                    correspondingNewAsset = newAssets[j];
                    break;
                }
            }

            if(correspondingNewAsset != null)
            {
                assetMap[oldAssets[i]] = correspondingNewAsset;
            }
        }
    }

    /// <summary>
    /// Iterates through all SerializedProperties of a target object and replaces any
    /// object references that are present in the assetMap with their new counterparts.
    /// </summary>
    private void RemapObjectReferences(Object targetObject, Dictionary<Object, Object> assetMap)
    {
        if (targetObject == null) return;

        SerializedObject so = new SerializedObject(targetObject);
        so.Update();

        SerializedProperty sp = so.GetIterator();
        // The 'true' argument enters into children properties
        while (sp.Next(true))
        {
            if (sp.propertyType == SerializedPropertyType.ObjectReference)
            {
                if (sp.objectReferenceValue != null && assetMap.TryGetValue(sp.objectReferenceValue, out Object newAsset))
                {
                    sp.objectReferenceValue = newAsset;
                }
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        // For materials, SerializedObject can miss texture properties on some shaders.
        // This public API is a more reliable second pass for them.
        if (targetObject is Material material)
        {
            RemapMaterialTextures(material, assetMap);
        }
    }

    /// <summary>
    /// A specific pass for materials to ensure textures are remapped, as SerializedObject can be unreliable.
    /// </summary>
    private void RemapMaterialTextures(Material newMaterial, Dictionary<Object, Object> assetMap)
    {
        if (newMaterial == null) return;

        string[] texturePropertyNames = newMaterial.GetTexturePropertyNames();
        foreach (string propertyName in texturePropertyNames)
        {
            Texture oldTexture = newMaterial.GetTexture(propertyName);
            if (oldTexture != null && assetMap.TryGetValue(oldTexture, out Object newAsset))
            {
                newMaterial.SetTexture(propertyName, newAsset as Texture);
            }
        }
        EditorUtility.SetDirty(newMaterial);
    }
    
    /// <summary>
    /// Returns the name of the subfolder an asset should be placed in, based on its type.
    /// </summary>
    private string GetSubfolderForAsset(Object asset)
    {
        if (asset is Material) return "Materials";
        if (asset is Texture) return "Textures";
        if (asset is Mesh) return "Meshes";
        if (asset is RuntimeAnimatorController) return "Animators";
        if (asset is AnimationClip) return "Animations";
        if (asset is AudioClip) return "Audio";
        if (asset is PhysicMaterial) return "Physics";
        if (asset is Font) return "Fonts";
        
        return null;
    }
}

