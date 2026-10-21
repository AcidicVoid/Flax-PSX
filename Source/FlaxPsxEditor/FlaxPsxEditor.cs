using System;
using FlaxEditor;
using FlaxEditor.GUI;
using FlaxEngine;
using AcidicVoid.FlaxPsx.Lighting;

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
        }

        /// <inheritdoc />
        public override void Deinitialize()
        {
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
