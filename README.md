# Sir Diorama

Player side plugin for Space Engineers, loaded by Pulsar. It gives the game the
look of **The Touryst**: the world in big pixels, like a miniature diorama.

- **Big pixels**: square pixels of 2 to 8 screen pixels (4 by default), a
  reduced palette of 8 to 64 colours (32 by default) and an ordered dither that
  does not flicker while nothing moves.
- **Miniature blur**: sharp at the distance of what sits in the middle of the
  screen, blurred in front and behind. The focus follows the view smoothly.
  Out of focus lights spread into small discs.
- Both effects are on together; each one has its own switch, and the blur its
  own strength.
- The interface (HUD, chat, menus, terminal, texts) stays sharp; screenshots
  taken with the game's key show the effect.
- Off, the game draws with its own shaders: the picture is exactly the game's.
- Everything happens on the player's machine. Nothing goes through the server,
  and a player without the plugin sees the game's rendering.

## Use

- **Shortcut**: `Alt+F2` in play switches the look on and off. It differs from
  Sir Cel Shading's, and modifiers must match exactly (`Ctrl+F2` does not
  trigger it). It can be changed in the settings or with `/diorama key Ctrl+F9`.
- **Settings**: Pulsar, Sir Diorama, settings button. The first setting is the
  **Enable plugin** box. Then come big pixels (on/off, pixel size, palette
  colours), miniature blur (on/off, blur strength) and the shortcut. Every
  change shows on the next frame.
- **Chat**: `/diorama` toggles the look; `/diorama on`, `/diorama off`,
  `/diorama status`, `/diorama key <shortcut>`. The command is not sent to the
  other players.
- **Saved settings**: `%AppData%\SpaceEngineers\Storage\sir-diorama\settings.xml`.

The defaults are a starting point, to be tuned in game.

## How it works

The game ships its image effects as source (`Content/Shaders`) and compiles them
itself. Sir Diorama hooks the final colour pass, `MyToneMapping.Run`, a compute
shader (`Postprocess/Tonemapping/Main.hlsl`) in three variants: `m_cs`,
`m_csAlphaLuminance`, `m_csSkip`. It is the last step that sees the scene in HDR,
before the selection highlight, the billboards, FXAA and the interface.

1. At start, the plugin writes its variant of that shader,
   `Storage\sir-diorama\Shaders\Diorama.hlsl`. It includes the game's headers
   between angle brackets, so those of the game's shader folder.
2. When on, a Harmony prefix has the game compile the variant in use
   (`MyShaderCompiler.Compile`, which refuses without crashing, then
   `MyComputeShaders.Create`), puts it in the game's static field, and binds
   the scene depth (`MyGBuffer.Main.ResolvedDepthStencil.SrvDepth`) in `t31`
   and the two focus buffers in `t30` and `u1`. The postfix gives the field
   its game shader back and unbinds the three slots. A finalizer does the same
   if the pass fails.
3. Off, the prefix touches nothing.

For every big pixel, one thread of the group computes, in this order:

- **Miniature blur, in HDR.** The circle of confusion comes from the ratio
  between the distance of the pixel and the focus distance, never from metres:
  the look is the same at one metre or at ten kilometres. Within 6 % of the
  focus distance nothing is blurred, so an LCD panel that is looked at stays
  sharp. The blur is gathered on a golden angle spiral; a tap counts when its
  own blur reaches the pixel, so a blurred foreground spreads over a sharp
  background and a background never bleeds onto a sharper foreground. Done
  before the final colours, a bright light keeps its strength and spreads into
  a disc.
- **Focus.** The median of five depth samples at the centre of the screen,
  eased from frame to frame (time constant 0.3 s, whatever the frame rate).
  The value is kept on the GPU in two tiny buffers swapped every frame: the
  CPU never waits for the GPU.
- **The game's final colours**, as is: exposure, bloom, filmic curve, colour
  filters. The game's film grain is left out with big pixels (it changes every
  frame and would make the dither flicker).
- **Palette.** Each channel is quantized in sRGB on evenly spaced levels (the
  colour count is split into balanced levels: 8 is 2x2x2, 32 is 3x3x3, 64 is
  4x4x4). The 4x4 Bayer threshold of the big pixel picks the level below or
  above; the share of big pixels taking the upper level is rounded down, in
  linear light. Over its dither cell an area is therefore never brighter than
  the original colour, and black stays black. The threshold depends on the
  position alone, never on time.

**FXAA.** While the effect is on, the renderer works with a copy of the game's
debug overrides where FXAA is off, so the edges of the big pixels stay crisp.
The game's settings are never changed; when the effect is off, the game's own
overrides come back and FXAA is as it was set.

**Cost.** Each big pixel is computed once (not once per screen pixel): at the
default size, one screen pixel in sixteen runs the blur, 60 taps. Nothing is
added to the frame but the tiny focus buffers.

**What is drawn after the effect.** The selection highlight and the billboards
drawn after the final colours stay at full resolution, on top of the big
pixels.

## Stops and coexistence

Every internal name of the render engine is resolved by reflection at start.
Several cases stop the effect for the whole session:

- a missing name;
- a missing game header;
- a variant refused by the game's compiler;
- a fault on the render thread.

All go through the same path (`SessionStop`). The game keeps its rendering, a
`[sir-diorama]` line goes to the game log, and the player gets a notification as
soon as a world is open.

Two plugins never fight over the same step. Before hooking in, then every ten
seconds, Sir Diorama looks at who is hooked on `MyToneMapping.Run`
(`Harmony.GetPatchInfo`). If it finds another owner (Sir Cel Shading, for
instance), it yields and tells the player. The plugin loaded second yields.

## Pulsar card

Pulsar reads the displayed name from the plugin card copied into its catalogue,
not from this repository. The card is `PluginHub/sir-diorama.xml`, with
`FriendlyName` set to "Sir Diorama". Its `Commit` field is filled at release.
`SourceDirectories` limits the build to the `Source` folder.

## Build and test

    dotnet build sir-diorama.csproj
    dotnet test tests/tests.csproj

The build looks for the game in the `Bin64` property, then in the `SE_BIN64`
environment variable, then in the most common Steam libraries. For another
place, see `Directory.Build.props.example`.

The tests cover the pure logic (`Source/Logic`): settings, palette, focus,
shortcut, command, session stop, coexistence, shader variants and source. They
need neither the game nor Pulsar. When the game's shader folder is found
(`SE_SHADERS`, or `SE_BIN64`), they also compile every variant for real with
the Windows HLSL compiler and the game's headers.
