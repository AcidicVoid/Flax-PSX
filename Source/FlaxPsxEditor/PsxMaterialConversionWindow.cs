#if FLAX_EDITOR
using System.Collections.Generic;
using System.IO;
using FlaxEditor;
using FlaxEditor.Content;
using FlaxEditor.CustomEditors;
using FlaxEditor.GUI;
using FlaxEditor.Scripting;
using FlaxEngine;
using FlaxEngine.GUI;
using FlaxPsx;

namespace AcidicVoid.FlaxPsxEditor;

/// <summary>
/// PsxMaterialConversionWindow Script.
/// </summary>
public class PsxMaterialConversionWindow(List<AssetItem> materials) : CustomEditorWindow
{
    private AssetPicker _picker;
    private CheckBox _autoAssignCheckBox;
    private Button _convertButton;
    
    private readonly IReadOnlyList<string> _allowedMaterials = new List<string>
    {
        "PSX Surface Opaque",
        "PSX Surface Opaque Two-Sided"
    };

    public override void Initialize(LayoutElementsContainer layout)
    {

        layout.Label("Flax-PSX Material Conversion");
        layout.Space(20);
        
        _picker = layout.Custom<AssetPicker>("Base Material or Material Instance").CustomControl;
        _picker.Validator.AssetType = new ScriptType(typeof(MaterialBase));
        _picker.Validator.CheckValid = IsValidBaseMaterial;
        _picker.SelectedItemChanged += SelectedMaterialChanged;

        _autoAssignCheckBox = layout.Checkbox("Auto assign new materials to users").CheckBox;
        _autoAssignCheckBox.Checked = true;

        layout.Space(10);
        _convertButton = layout.Button("Convert").Button;
        _convertButton.Clicked += OnConvertClicked;
        layout.Button("Cancel").Button.Clicked += OnCancelClicked;
        
        SelectedMaterialChanged(); // Run once to set initial button sate correctly
    }

    private void SelectedMaterialChanged()
    {
        _convertButton.Enabled = (_picker?.Validator?.SelectedItem != null) && _picker.Validator.IsValid(_picker.Validator.SelectedItem);
    }
    
    private bool IsValidBaseMaterial(ContentItem item)
    {
        if (item is not AssetItem assetItem || !assetItem.IsOfType(typeof(MaterialBase)) || !assetItem.IsOfType(typeof(MaterialInstance)))
            return false;

        var asset = Content.Load<MaterialBase>(assetItem.ID);
        if (asset == null)
            return false;

        // Walk the instance chain up to its root Material.
        var root = asset;
        while (root is MaterialInstance mi)
        {
            if (mi.BaseMaterial == null)
                return false;
            root = mi.BaseMaterial;
        }

        var allowed = false;
        foreach (var allowedMaterial in _allowedMaterials)
        {
            if (allowed)
                continue;
            allowed = root.Path.Contains(allowedMaterial);
        }
        return allowed;
    }

    private void OnConvertClicked()
    {
        var baseMaterial = _picker?.Validator.SelectedAsset as MaterialBase;
        var autoAssign = _autoAssignCheckBox?.Checked ?? true;
        Window.Close();
        PsxMaterialConversion.Convert(materials, baseMaterial, autoAssign);
    }

    private void OnCancelClicked()
    {
        Window.Close();
    }
}
#endif