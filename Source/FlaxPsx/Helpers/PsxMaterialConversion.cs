#if FLAX_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using FlaxEditor;
using FlaxEditor.Content;
using FlaxEditor.SceneGraph;
using FlaxEngine;

namespace FlaxPsx;

/// <summary>
/// PsxMaterialConversion class.
/// </summary>
public class PsxMaterialConversion
{
    private const string DiffuseParameterName = "Diffuse";
    public static void Convert(List<AssetItem> materials, MaterialBase baseMaterial, bool autoAssign)
    {
        int converted = 0;
        int reassigned = 0;
 
        foreach (var item in materials)
        {
            var original = Content.Load<MaterialBase>(item.ID);
            if (original == null)
            {
                Debug.LogWarning($"Flax-PSX: could not load '{item.Path}', skipping.");
                continue;
            }

            var outputFolder = Path.GetDirectoryName(item.Path);
            if (string.IsNullOrEmpty(outputFolder))
            {
                Debug.LogWarning($"Flax-PSX: could not resolve output folder for '{item.Path}', skipping.");
                continue;
            }
            var outputPath = GetUniqueAssetPath(outputFolder, "MI_" + item.ShortName);

            // Create a real, disk-backed (non-virtual) empty Material Instance file first -
            // CreateVirtualAsset<MaterialInstance>() + Save(path) does NOT work: SetChunk()
            // is a no-op while the asset is still virtual (BinaryAsset::GetOrCreateChunk
            // returns null for virtual assets), so the saved file ends up with zero chunks.
            if (Editor.CreateAsset("MaterialInstance", outputPath))
            {
                Debug.LogWarning($"Flax-PSX: failed to create new material instance at '{outputPath}'.");
                continue;
            }

            var newInstance = Content.Load<MaterialInstance>(outputPath);
            if (newInstance == null)
            {
                Debug.LogWarning($"Flax-PSX: created '{outputPath}' but failed to load it back.");
                continue;
            }

            newInstance.BaseMaterial = baseMaterial;

            if (TryGetSingleTexture(original, out var texture))
            {
                newInstance.SetParameterValue(DiffuseParameterName, texture);
            }
            else
            {
                Debug.LogWarning($"Flax-PSX: '{item.Path}' doesn't have exactly one texture parameter... skipping.");
            }

            if (newInstance.Save())
            {
                Debug.LogWarning($"Flax-PSX: failed to save base material onto '{outputPath}'.");
                continue;
            }

            converted++;
            Debug.Log($"Flax-PSX: created '{outputPath}' from '{item.Path}'.");

            if (autoAssign)
                reassigned += AssignToUsers(original.ID, newInstance);
        }
 
        Debug.Log($"Flax-PSX: converted {converted} material(s), reassigned {reassigned} actor slot(s).");
    }
    
    private static bool TryGetSingleTexture(MaterialBase material, out Texture texture)
    {
        texture = null;
        int count = 0;
        foreach (var param in material.Parameters)
        {
            if (param.ParameterType != MaterialParameterType.Texture)
                continue;
            count++;
            texture = param.Value as Texture;
        }
        return count == 1 && texture != null;
    }
    
    private static int AssignToUsersInPrefabs(Guid originalMaterialId, MaterialInstance newInstance)
    {
        int count = 0;

        if (!Level.IsAnySceneLoaded)
        {
            Debug.LogWarning("Flax-PSX: no scene is loaded - skipping prefab-asset scan (PrefabManager.SpawnPrefab needs a loaded scene to spawn into).");
            return count;
        }

        var prefabIds = FlaxEngine.Content.GetAllAssetsByType(typeof(Prefab));
        foreach (var prefabId in prefabIds)
        {
            var prefab = FlaxEngine.Content.Load<Prefab>(prefabId);
            if (prefab == null)
                continue;

            var instance = PrefabManager.SpawnPrefab(prefab);
            if (instance == null)
            {
                Debug.LogWarning($"Flax-PSX: failed to spawn a temporary instance of prefab '{prefab.Path}'.");
                continue;
            }

            int before = count;
            AssignToUsersInActorTree(instance, originalMaterialId, newInstance, ref count);

            if (count > before)
            {
                if (PrefabManager.ApplyAll(instance))
                    Debug.LogWarning($"Flax-PSX: failed to apply material changes to prefab '{prefab.Path}'.");
                else
                    Debug.Log($"Flax-PSX: updated {count - before} slot(s) in prefab '{prefab.Path}'.");
            }

            FlaxEngine.Object.Destroy(instance);
        }

        return count;
    }

    private static void AssignToUsersInActorTree(Actor actor, Guid originalMaterialId, MaterialInstance newInstance, ref int count)
    {
        if (actor is ModelInstanceActor meshActor)
        {
            var slots = meshActor.MaterialSlots;
            for (int i = 0; i < slots.Length; i++)
            {
                var current = meshActor.GetMaterial(i);
                if (current != null && current.ID == originalMaterialId)
                {
                    meshActor.SetMaterial(i, newInstance);
                    count++;
                }
            }
        }

        for (int i = 0; i < actor.ChildrenCount; i++)
            AssignToUsersInActorTree(actor.GetChild(i), originalMaterialId, newInstance, ref count);
    }
    
    private static int AssignToUsers(Guid originalMaterialId, MaterialInstance newInstance)
    {
        int count = 0;
        var editedScenes = new HashSet<Scene>();
        AssignToUsers(Editor.Instance.Scene.Root, originalMaterialId, newInstance, editedScenes, ref count);

        foreach (var scene in editedScenes)
            Editor.Instance.Scene.MarkSceneEdited(scene);

        count += AssignToUsersInPrefabs(originalMaterialId, newInstance);

        return count;
    }
 
    private static void AssignToUsers(SceneGraphNode node, Guid originalMaterialId, MaterialInstance newInstance, HashSet<Scene> editedScenes, ref int count)
    {
        if (node is ActorNode actorNode && actorNode.Actor is ModelInstanceActor meshActor)
        {
            var slots = meshActor.MaterialSlots;
            for (int i = 0; i < slots.Length; i++)
            {
                var current = meshActor.GetMaterial(i);
                if (current != null && current.ID == originalMaterialId)
                {
                    meshActor.SetMaterial(i, newInstance);
                    count++;
                    if (actorNode.Actor.Scene != null)
                        editedScenes.Add(actorNode.Actor.Scene);
                }
            }
        }
 
        for (int i = 0; i < node.ChildNodes.Count; i++)
            AssignToUsers(node.ChildNodes[i], originalMaterialId, newInstance, editedScenes, ref count);
    }
 
    private static string GetUniqueAssetPath(string folder, string baseName)
    {
        var path = Path.Combine(folder, baseName + ".flax");
        int i = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(folder, $"{baseName}_{i}.flax");
            i++;
        }
        return path;
    }
}
#endif