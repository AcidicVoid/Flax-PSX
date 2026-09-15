#if FLAX_EDITOR
using System.Collections.Generic;
using FlaxEditor;
using FlaxEditor.Content;
using FlaxEditor.GUI.ContextMenu;
using FlaxEngine;

namespace AcidicVoid.FlaxPsxEditor;

public class FlaxPsxEditor : EditorPlugin
{
    private ContextMenuButton _toolsButton;

    public override void InitializeEditor()
    {
        Debug.Log("Initializing Flax PSX Editor plugin ...");
        base.InitializeEditor();

        // This hooks us up to the Content Window's context menu
        // We use this to implement custom logic
        Editor.Windows.ContentWin.ContextMenuShow += OnContentWinContextMenuShow;
    }

    public override void DeinitializeEditor()
    {
        // Cleanup
        Editor.Windows.ContentWin.ContextMenuShow -= OnContentWinContextMenuShow;

        _toolsButton?.Dispose();
        _toolsButton = null;

        base.DeinitializeEditor();
    }

    private void OnContentWinContextMenuShow(ContextMenu contextMenu, ContentItem contentItem)
    {
        // Check if selection contains materials, add them to List
        var selectedMaterials = new List<AssetItem>();
        var selection = Editor.Windows.ContentWin.View.Selection; // List<ContentItem>, the full multi-selection
        foreach (var item in selection)
        {
            if (item is AssetItem assetItem && assetItem.IsOfType(typeof(MaterialBase)))
                selectedMaterials.Add(assetItem);
        }

        // If no materials are selected, return
        // No new content menu entry will be shown
        if (selectedMaterials.Count == 0)
            return;
        
        // Create entry for Flax-PSX Material Conversion
        var window = new PsxMaterialConversionWindow(selectedMaterials);
        contextMenu.AddSeparator();
        contextMenu.AddButton("Convert to Flax-PSX material", () => window.Show());
        contextMenu.AddSeparator();
    }

}
#endif