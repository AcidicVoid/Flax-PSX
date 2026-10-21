using System;
using FlaxEditor;
using FlaxEditor.GUI;
using FlaxEditor.SceneGraph;
using FlaxEditor.Windows.Assets;
using FlaxEngine;
using AcidicVoid.FlaxPsx.Lighting;
using ContextMenu = FlaxEditor.GUI.ContextMenu.ContextMenu;

namespace AcidicVoid.FlaxPsx
{
	/// <summary>
	/// The editor plugin using <see cref="FlaxPsx"/>.
	/// </summary>
	/// <seealso cref="FlaxEditor.EditorPlugin" />
	public class FlaxPsxEditor : EditorPlugin
    {
        private ToolStripButton _button;
        private static readonly VertexLightManager.ReferenceViewDelegate EditorViewProvider = TryGetEditorView;

        // Vertex light "New" context menu entries (one actor class, one entry per light type)
        private static readonly (PsxLightType Type, string Name)[] LightMenuEntries =
        {
            (PsxLightType.Directional, "Directional Vertex Light"),
            (PsxLightType.Point, "Point Vertex Light"),
            (PsxLightType.Spot, "Spot Vertex Light"),
            (PsxLightType.Area, "Area Vertex Light"),
        };

        /// <inheritdoc />
        public override Type GamePluginType => typeof(FlaxPsx);

/*
        /// <inheritdoc />
        public override void InitializeEditor()
        {
            base.InitializeEditor();
            _button = Editor.UI.ToolStrip.AddButton("My Plugin");
            _button.Clicked += () => MessageBox.Show("Button clicked!");
        }
*/

        /// <inheritdoc />
        public override void InitializeEditor()
        {
            base.InitializeEditor();

            // Vertex lights: cull/sort against the editor viewport camera in edit mode
            VertexLightManager.ReferenceViewProvider = EditorViewProvider;

            // Vertex lights: the editor owns the manager so it runs in edit and play mode
            VertexLightManager.Initialize();

            // Vertex lights: "New/Flax PSX" context menu entries per light type (scene tree, viewport, prefab windows)
            if (Editor.Windows.SceneWin != null)
                Editor.Windows.SceneWin.ContextMenuShow += OnSceneContextMenuShow;
            PrefabWindow.ContextMenuShow += OnPrefabContextMenuShow;
        }

        /// <inheritdoc />
        public override void Deinitialize()
        {
            if (Editor?.Windows?.SceneWin != null)
                Editor.Windows.SceneWin.ContextMenuShow -= OnSceneContextMenuShow;
            PrefabWindow.ContextMenuShow -= OnPrefabContextMenuShow;

            if (VertexLightManager.ReferenceViewProvider == EditorViewProvider)
                VertexLightManager.ReferenceViewProvider = null;
            VertexLightManager.Shutdown();

            if (_button != null)
            {
                _button.Dispose();
                _button = null;
            }

            base.Deinitialize();
        }

        private void OnSceneContextMenuShow(ContextMenu contextMenu)
        {
            AddLightMenuEntries(contextMenu, SpawnLightInScene);
        }

        private void OnPrefabContextMenuShow(ContextMenu contextMenu)
        {
            // The event is shared by all prefab windows: use the one the menu was opened in (it has the focus)
            PrefabWindow window = null;
            var windows = Editor.Windows.Windows;
            for (int i = 0; i < windows.Count; i++)
            {
                if (windows[i] is PrefabWindow prefabWindow && prefabWindow.ContainsFocus)
                {
                    window = prefabWindow;
                    break;
                }
            }
            if (window == null)
                return;

            AddLightMenuEntries(contextMenu, (type, name) =>
            {
                window.Spawn(CreateLight(type, name));
                window.RenameSelection();
            });
        }

        private static void AddLightMenuEntries(ContextMenu contextMenu, Action<PsxLightType, string> spawn)
        {
            var psxMenu = contextMenu.GetOrAddChildMenu("New").ContextMenu.GetOrAddChildMenu("Flax PSX").ContextMenu;
            foreach (var entry in LightMenuEntries)
                psxMenu.AddButton(entry.Name, () => spawn(entry.Type, entry.Name));
        }

        private static PsxVertexLight CreateLight(PsxLightType type, string name)
        {
            return new PsxVertexLight
            {
                LightType = type,
                Name = name,
            };
        }

        /// <summary>
        /// Spawns like the scene tree's own "New" entries: under the selected actor, otherwise under the last loaded scene.
        /// </summary>
        private void SpawnLightInScene(PsxLightType type, string name)
        {
            Actor parent = null;
            var selection = Editor.SceneEditing.Selection;
            if (selection.Count > 0 && selection[0] is ActorNode actorNode)
            {
                parent = actorNode.Actor;
                actorNode.TreeNode.Expand();
            }
            if (parent == null)
            {
                var scenes = Level.Scenes;
                if (scenes.Length > 0)
                    parent = scenes[scenes.Length - 1];
            }
            if (parent == null)
                return;

            var light = CreateLight(type, name);
            light.Transform = parent.Transform;
            light.Name = FlaxEditor.Utilities.Utils.IncrementNameNumber(light.Name, x => parent.GetChild(x) == null);

            var sceneWin = Editor.Windows.SceneWin;
            sceneWin.Spawn(light, parent);
            sceneWin.RenameSelection();
        }

        /// <summary>
        /// Reference view for vertex light culling: the editor viewport camera while not in play mode.
        /// </summary>
        private static bool TryGetEditorView(out Vector3 position, out BoundingFrustum frustum)
        {
            position = Vector3.Zero;
            frustum = default;
            if (FlaxEditor.Editor.IsPlayMode)
                return false;
            var viewport = FlaxEditor.Editor.Instance?.Windows?.EditWin?.Viewport;
            if (viewport == null || viewport.Task == null)
                return false;
            position = viewport.ViewPosition;
            frustum = viewport.ViewFrustum;
            return true;
        }
    }
}
