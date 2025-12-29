# Flax PSX
## A Flax Engine plugin that brings PSX-like visuals to your project

This plugin uses a custom post-processing effect that fully bypasses the engine’s internal scene rendering. Instead, it renders the scene directly into a GPU texture at the exact resolution you specify.
Because the scene is actually rendered at a low resolution — rather than rendered at full size and later pixelated — performance improves significantly. This approach is ideal for projects that rely on extremely low resolutions, delivering both visual accuracy and efficiency.

## Feature Status
- [x] PSX-Style post processing
  - [x] dithering
  - [x] integer scaling
  - [x] depth-based fog
  - [ ] additional fog techniques
- [x] additional post processing (optional)
  - [x] basic implementation of CRT-like features
  - [ ] CRT / NTSC signal emulation
- [x] PSX-Style materials
  - [x] 5bpc color precision
  - [x] vertex lighting
  - [x] screenspace vertex snapping (the mind-boggle wobble)
  - [x] PSX-style water caustics (Tomb Raider style)
    - [x] material with caustics limited to local lights
  - [ ] lighting effects for fire (torches, etc.)

### Notes:  
* You can also just use the materials, without all post-processing  
* Using the additional post processing **only** should work but is not tested - I will probably test it properly in the future and make adjustments if necessary  
* This plugin has currently been tested with Windows 11 **only**.

## Installation

1. go to Tools → Plugins to open the Plugins window
2. click **Clone Project**
3. enter ``Flax PSX`` as *Plugin Name* and this repository as *Git Path*: `git@github.com:AcidicVoid/Flax-PSX.git`
4. create and empty actor in your project
    *  it's highly reccomended to disable all advanced graphics features like anti aliasing, camera artifacts, etc. if you're aiming for authentic retro visuals
5. add a **Flax PSX/PostProcessingResources** script to your actor
    * add your custom camera (present in the scene)
      * if using [CineBlend](https://github.com/GasimoCodes/CineBlend), you can either reference the Custom Camera that contains the CineBlendMaster, or use the camera assigned to Camera.MainCamera, which also contains the CineBlendMaster — don't worry about the virtual cameras.
6. add a **Flax PSX/PostProcessing** script to your actor (doesn't need to be same actor as from step 5)
    * add reference to the *PostProcessingResources* to the Resources slot
    * add the *FlaxPsxPostProcessing* shader to the Shader slot
    * play around with the settings
7. **OPTIONAL** add a **Flax PSX/AdditionalPostProcessing** script to your actor (doesn't need to be same actor as from step 5 or step 6)
    * add reference to the *PostProcessingResources* to the Resources slot
    * add the *FlaxPsxAdditionalPostProcessing* shader to the Shader 
    * if you want to use a slot-mask, add one to the texture slot - CC0 slotmask images from [MAME](https://github.com/mamedev/mame) included
    * play around with the settings

It now should look something like this:  

![Scripts of actor](.github/media/scripts_of_actor.png)

If you're testing the plugin with the standard basic scene, you now should see something like this:

![Basic Scene Screenshot, 4:3 aspect](.github/media/standard_scene_screenshot_43.png)

You also can use some other aspect ratio, just change *RenderSize* parameter

![Basic Scene Screenshot, 4:3 aspect](.github/media/standard_scene_screenshot_16.png)

## Post Processing Options

| Parameter Name                     | Type                    | Description                                                                                                  |  
|------------------------------------|-------------------------|--------------------------------------------------------------------------------------------------------------|  
| Render Size                        | Int2                    | Size of internal GPU texture                                                                                 |
| Integer Scaling                    | bool                    | enables pixel-perfect scaling, can cause pillar/letter-boxing                                                |
| Use Custom Viewport                | bool                    | Calculates a custom viewport - use if your desired aspect ratio differs from actual game window aspect ratio |
| Recalculate Viewport Size On Change | bool                    | Detects changes and re-calculates the viewport if needed                                                     |
| Resources                          | PostProcessingResources | Actor with PostProcessingResources script attached - stores and handles GPU textures                         |
| Fog Style                          | int                     | `0: no fog` `1: SH1 style fog`                                                                               |
| Fog Color                          | Color                   | Sets fog color - use alpha to adjsut fog density                                                             |
| Fog Boost                          | float                   | Makes fog appear nearer or more dense, depending on fog style                                                |
| Use dithering                      | bool                    | Enable PSX style dithering effect                                                                            |
| Dither strength                    | float                   | Amount of PSX style dithering effect                                                                         |
| Dither blend                       | float                   | blends dithered scene with original                                                                          |
| Use PSX Color Precision            | bool                    | Truncate to 5bpc precision via bitwise AND operator - subtile effect for more authentic PSX colors           |
| Use High Color                     | bool                    | Switches GPU texture format to 16-bit colors (otherwise uses 8-bit colors) - currently breaks dithering      |
| Scanline Strength                  | float                   | Strength of currently very basic scanline effect - works best with integer scaling                           |

## Support

You'll find more on my projects [on Bluesky](https://bsky.app/hashtag/AcidicDev?author=acidicvoid.com)

If you like this project, please consider supporting me: **[DONATE](https://dono.acidicvoid.com/)**

### Sources

* PSX-Style dithering is designed according to Psy-Q documentation, page 424:  
https://psx.arthus.net/sdk/Psy-Q/DOCS/LIBREF46.PDF

* This repo was once forked from the example plugin project for Flax Engine. To learn more see the related documentation [here](https://docs.flaxengine.com/manual/scripting/plugins/index.html).
