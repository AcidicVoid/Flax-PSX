using System;
using FlaxEngine;

namespace PsxPlugin
{
    /// <summary>
    /// The sample game plugin.
    /// </summary>
    /// <seealso cref="FlaxEngine.GamePlugin" />
    public class FlaxPsxPlugin : GamePlugin
    {
        /// <inheritdoc />
        public FlaxPsxPlugin()
        {
            _description = new PluginDescription
            {
                Name = "Flax PSX",
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
        }

        /// <inheritdoc />
        public override void Initialize()
        {
            base.Initialize();
            Debug.Log("Initializing Flax PSX plugin");
        }

        /// <inheritdoc />
        public override void Deinitialize()
        {
            Debug.Log("Plugin cleanup!");
            base.Deinitialize();
        }
    }
}
