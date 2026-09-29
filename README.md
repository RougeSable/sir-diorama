# Sir Diorama

Player side plugin for Space Engineers, loaded by Pulsar. It gives the game the
look of **Minecraft**: every surface drawn in big square texels of plain colour.

- **Texels fastened to the world.** Each surface is cut into squares. On the
  ship or station the player is in or next to, they follow its axes and texel
  edges fall exactly on block edges (16 texels per metre by default, as in
  Minecraft: 40 on a large block, 8 on a small one). Everywhere else, on a
  planet the ground carries square texels along its vertical, and in space
  they follow the world axes, even while the player's ship flies. The texels
  stay where they are on the surfaces: nothing flickers while nothing moves.
- **Big even far away.** With distance, texels double in size so that none is
  smaller than a few screen pixels (4 by default): planets, asteroids and far
  ships keep the blocky look.
- **Plain colours.** Each texel shows the average light of the surface over
  it, then the game's own final colours, with a touch of extra saturation.
- **Sharp everywhere.** No blur of any kind, at any distance.
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
  **Enable plugin** box. Then come the texels per metre (4, 8, 16 or 32), the
  smallest texel on screen (2 to 8 pixels), the colour boost (0 to 100 %) and
  the shortcut. Every change shows on the next frame; none compiles anything.
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
   `Storage\sir-diorama\Shaders\Blocky.hlsl`. It includes the game's headers
   between angle brackets, so those of the game's shader folder.
2. When on, a Harmony prefix has the game compile the variant in use, once per
   session (`MyShaderCompiler.Compile`, which refuses without crashing, then
   `MyComputeShaders.Create`), puts it in the game's static field, binds the
   scene depth (`MyGBuffer.Main.ResolvedDepthStencil.SrvDepth`) in `t31` and
   the plugin's constant buffer in `b6`, filled for the frame being drawn. The
   postfix gives the field its game shader back and unbinds both slots. A
   finalizer does the same if the pass fails.
3. Off, the prefix touches nothing.

For every pixel, the shader:

- rebuilds the point seen there from the depth, and the plane of the surface
  from the neighbouring depths (on each side, the one that crosses no edge);
- takes the frame the texels are fastened to and the two of its axes that lie
  best in the surface (a 45 degree slope always picks the same pair);
- finds the texel holding the point, doubling its size far away until it
  covers the smallest size on screen (sizes are powers of two, so coarse
  texels fall exactly on fine ones);
- averages the scene over nine taps spread on the texel, keeping only the taps
  where what is really seen lies on the plane of the surface (nothing in front,
  nothing past an edge). Every pixel of a texel finds the same taps: the texel
  is one plain colour;
- then applies the game's final colours (exposure, bloom, filmic curve, colour
  filters) and the colour boost, which saturates around the luminance and never
  changes it. The game's film grain is left out: it changes every frame.

**The frames.** Every few updates the main thread chooses two of them. The
frame of the surroundings: in gravity, a frame whose Y axis points up (kept
until the vertical turns by more than 3 degrees), else the world axes; its
origin is the world's, so the ground, the asteroids and everything that does
not move keep their texels, whatever flies. And the grid near the player: the
one of the seat or cockpit he is in, else the grid closest to the camera within
60 m (kept until another one is clearly closer), with its box (`LocalAABB`, in
the grid's axes). Only the points that fall inside that box, with one block of
margin, take the grid's frame; every other point, the ground seen through the
cockpit glass included, takes the frame of the surroundings. The render thread
reads the grid's matrix from its own copy of the scene
(`MyIDTracker<MyActor>`), for the very frame being drawn, so the ship's
surfaces keep their texels while it flies. Other ships that move still slide
under their texels: the depth does not say which object a pixel belongs to.
Positions reach
millions of metres, beyond float precision: the camera is given to the shader
in the frame's axes, in double precision, modulo a period that every texel size
divides. In first person on foot, what is closer than one metre (the tool in
hand) uses the camera's own axes, so it keeps still texels too.

**FXAA.** While the effect is on, the renderer works with a copy of the game's
debug overrides where FXAA is off, so texel edges stay crisp. The game's
settings are never changed; when the effect is off, the game's own overrides
come back and FXAA is as it was set.

**Cost.** Per pixel: five depth reads for the plane, then nine taps (a
projection, a depth read and a scene read each). No extra render target, no
extra pass: the work happens inside the game's own final colour pass.

**What is drawn after the effect.** The selection highlight and the billboards
drawn after the final colours stay at full resolution, on top of the texels.

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

The tests cover the pure logic (`Source/Logic`): settings, texel grid and
frames, shortcut, command, session stop, coexistence (with a real Harmony patch
standing for another plugin) and shader variants. They need neither the game
nor Pulsar. When the game's shader folder is found (`SE_SHADERS`, `SE_BIN64`,
or a copy of the game next to the repository), they also compile every variant
for real with the Windows HLSL compiler and the game's headers.
