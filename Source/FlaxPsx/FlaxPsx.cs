using System;
using FlaxEditor.GUI.ContextMenu;
using FlaxEngine;

namespace AcidicVoid.FlaxPsx
{
    /// <summary>
    /// The sample game plugin.
    /// </summary>
    /// <seealso cref="FlaxEngine.GamePlugin" />
    public class FlaxPsx : GamePlugin
    {
        private ContextMenuButton _toolsButton;
        private static PluginDescription __description;
        
        /// <inheritdoc />
        public FlaxPsx()
        {
            _description = new PluginDescription
            {
                Name = "FlaxPSX",
                Category = "Graphics",
                Author = "AcidicVoid",
                AuthorUrl = "www.acidicvoid.com",
                HomepageUrl = null,
                RepositoryUrl = "https://github.com/AcidicVoid/Flax-PSX",
                Description = "Plugin that brings PSX-like visuals to your Flax Engine project ",
                Version = new Version(0, 1),
                IsAlpha = true,
                IsBeta = false,
            };
            // Set static description
            __description = _description;
        }

        public static PluginDescription GetDescription()
        {
            return __description;
        }

        /// <inheritdoc />
        public override void Initialize()
        {
            base.Initialize();
            Debug.Log("Initializing Flax PSX plugin ...");
        }

        /// <inheritdoc />
        public override void Deinitialize()
        {
            _toolsButton?.Dispose();
            _toolsButton = null;
            
            Debug.Log("Plugin cleanup!");
            base.Deinitialize();
        }
    }
}
