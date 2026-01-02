using System;
using FlaxEditor;
using FlaxEditor.GUI;
using FlaxEngine;

namespace AcidicVoid.FlaxPsx
{
	/// <summary>
	/// The editor plugin using <see cref="FlaxPsx"/>.
	/// </summary>
	/// <seealso cref="FlaxEditor.EditorPlugin" />
	public class FlaxPsxEditor : EditorPlugin
    {
        private ToolStripButton _button;

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
        public override void Deinitialize()
        {
            if (_button != null)
            {
                _button.Dispose();
                _button = null;
            }

            base.Deinitialize();
        }
    }
}
