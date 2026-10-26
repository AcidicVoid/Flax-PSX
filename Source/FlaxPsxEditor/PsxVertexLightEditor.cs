// www.acidicvoid.com

using FlaxEditor;
using FlaxEditor.CustomEditors;
using FlaxEditor.CustomEditors.Dedicated;
using FlaxEngine;
using AcidicVoid.FlaxPsx.Lighting;
using FlaxEditor.CustomEditors.Elements;
using FlaxEngine.GUI;

namespace AcidicVoid.FlaxPsx
{
    /// <summary>
    /// Properties panel editor for <see cref="PsxVertexLight"/>. Shows a notice and a "Set Active" button
    /// on directional lights that are not the active directional light of their scene.
    /// </summary>
    [CustomEditor(typeof(PsxVertexLight))]
    public class PsxVertexLightEditor : ActorEditor
    {
        private bool _showsInactiveNotice;

        /// <inheritdoc />
        public override void Initialize(LayoutElementsContainer layout)
        {
            _showsInactiveNotice = GetInactiveDirectional() != null;
            if (_showsInactiveNotice)
            {
                // In case we have more than one directional light per scene, we deactivate additional lights
                // on creation. A warning is shown together with a button that allows setting the additional
                // light as the active one.
                var warningText = "Inactive: only one directional light per scene is used.";
                var group = layout.Group("Directional Light");
                var warningContainer = group.ContainerControl;
                warningContainer.BackgroundColor = Color.DarkRed;
                var label = warningContainer.AddChild<Label>();
                label.AutoHeight = true;
                label.Text = warningText;
                label.Font.Size = 16;
                label.TextColor = Color.Yellow;
                var button = group.Button("Set Active Directional Light", "Makes this light the active directional light of its scene.");
                button.Button.Clicked += OnSetActiveClicked;
            }

            base.Initialize(layout);
        }

        /// <inheritdoc />
        public override void Refresh()
        {
            base.Refresh();

            // Show or hide the notice when the type or the active light changes.
            // RebuildLayoutOnRefresh() is ignored for the root object editor (RefreshRootChild doesn't process it),
            // so request a presenter rebuild, which runs at the start of the next update.
            if ((GetInactiveDirectional() != null) != _showsInactiveNotice)
            {
                _showsInactiveNotice = !_showsInactiveNotice;
                Presenter?.BuildLayoutOnUpdate();
            }
        }

        /// <summary>
        /// The edited light if exactly one directional light is selected and it isn't the active one, otherwise null.
        /// </summary>
        private PsxVertexLight GetInactiveDirectional()
        {
            if (Values == null || Values.Count != 1)
                return null;
            var light = Values[0] as PsxVertexLight;
            if (!light || light.LightType != PsxLightType.Directional || light.IsActiveDirectional)
                return null;
            return light;
        }

        private void OnSetActiveClicked()
        {
            var light = GetInactiveDirectional();
            if (light == null)
                return;

            // Records the light's stamp change, so undo restores the previously active light
            using (new UndoBlock(Editor.Instance.Undo, light, "Set Active Directional Light"))
            {
                VertexLightManager.SetActiveDirectional(light);
            }
        }
    }
}
