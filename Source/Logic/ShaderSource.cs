namespace SirDiorama
{
    // The plugin's variant of the game's final colour pass
    // (Postprocess/Tonemapping/Main.hlsl), written by the plugin into the
    // player's folder and compiled by the game itself (MyShaderCompiler.Compile,
    // then MyComputeShaders.Create), with the game's own headers.
    //
    // Why here: it is the last step that sees the scene in HDR, so a texel
    // takes the true light of the surface before the game's exposure and
    // filmic curve; and it comes before the selection highlight, the
    // billboards and FXAA. The interface (HUD, menus, texts) is drawn much
    // later, by RenderMainSprites: it stays sharp.
    public static class ShaderSource
    {
        public const string FileName = "Blocky.hlsl";

        // Scene depth. No file of the game's Content/Shaders declares t31, and
        // the game's pass only uses t0 to t3.
        public const int DepthSlot = 31;

        // The plugin's constant buffer (LookConstants). The game's pass only
        // binds b0 (frame constants); the compute stage has eight slots.
        public const int ConstantsSlot = 6;

        // Game headers the variant depends on, relative to Content/Shaders.
        // Included between angle brackets: the game's compiler then looks for
        // them in its own shader folder, wherever this file was written.
        public static readonly string[] GameHeaders =
        {
            "Postprocess/Tonemapping/Filters.hlsli",
            "Postprocess/Tonemapping/Defines.hlsli",
        };

        public static string Text
        {
            get
            {
                return Header
                    + "#include <" + GameHeaders[0] + ">\n"
                    + "#include <" + GameHeaders[1] + ">\n"
                    + "\n"
                    + "Texture2D<float> LookDepth : register(t" + DepthSlot + ");\n"
                    + "\n"
                    + "cbuffer LookConstants : register(b" + ConstantsSlot + ")\n"
                    + "{\n"
                    + "    float4 LookAxisX;        // grid X axis, in world axes; w: texel size (m)\n"
                    + "    float4 LookAxisY;        // grid Y axis; w: smallest texel on screen (px)\n"
                    + "    float4 LookAxisZ;        // grid Z axis; w: colour boost (0 to 1)\n"
                    + "    float4 LookCamera;       // camera in the grid frame, wrapped; w: near limit (m)\n"
                    + "    float4 LookBoxMin;       // grid box, relative to the camera, in grid axes; w: 1 if a grid\n"
                    + "    float4 LookBoxMax;       // grid box, relative to the camera, in grid axes\n"
                    + "    float4 LookAroundX;      // surroundings X axis (gravity or world), in world axes\n"
                    + "    float4 LookAroundY;      // surroundings Y axis\n"
                    + "    float4 LookAroundZ;      // surroundings Z axis\n"
                    + "    float4 LookAroundCamera; // camera in the surroundings frame, wrapped\n"
                    + "};\n"
                    + "\n"
                    + "#define LOOK_MAX_LEVEL " + TexelGrid.MaxLevel + "\n"
                    + Body;
            }
        }

        private const string Header =
@"// Sir Diorama: the world in the manner of Minecraft.
// Variant of Postprocess/Tonemapping/Main.hlsl, written by the plugin at every
// game start and compiled by the game itself. Do not edit by hand.
//
// Every surface is cut into square texels fastened to the world (inside the
// box of the ship or station nearby, to that grid, block edges on texel
// edges; everywhere else, to the planet's vertical or the world axes): each
// texel shows one plain
// colour, the average light of the surface over it. Then the game's own final
// colours, and a touch of extra saturation.

";

        private const string Body =
@"
// Sizes of the pass: the last pixel that the source and the depth both hold.
struct LookView
{
    int2 last;
};

LookView LookGetView()
{
    uint sourceWidth, sourceHeight, depthWidth, depthHeight;
    Source.GetDimensions(sourceWidth, sourceHeight);
    LookDepth.GetDimensions(depthWidth, depthHeight);

    LookView view;
    view.last = int2(
        min(min(sourceWidth, depthWidth), (uint)frame_.Screen.resolution.x),
        min(min(sourceHeight, depthHeight), (uint)frame_.Screen.resolution.y)) - 1;
    return view;
}

// Distance along the view, in metres. Zero for the sky, which has no depth.
float LookDepthAt(int2 pixel, LookView view)
{
    float hw = LookDepth[clamp(pixel, int2(0, 0), view.last)];
    return IsDepthForeground(hw) ? compute_depth(hw) : 0.0f;
}

// Position relative to the camera, in world axes, of the point seen at this
// screen position and at this view distance.
float3 LookPosition(float2 screen, float depth)
{
    float3 ray = compute_screen_ray(screen / frame_.Screen.resolution);
    return depth * view_to_world(ray);
}

// Screen position (xy, in pixels) and view distance (z) of a position
// relative to the camera. z is zero behind the camera.
float3 LookProject(float3 position)
{
    float4 clip = mul(float4(position, 1.0f), frame_.Environment.view_projection_matrix);
    if (!(clip.w > 0.0f))
        return float3(-1.0f, -1.0f, 0.0f);
    float2 uv = clip.xy / clip.w * float2(0.5f, -0.5f) + 0.5f;
    return float3(uv * frame_.Screen.resolution, clip.w);
}

// Plain colour of the texel that holds this pixel, in HDR. The original
// colour is kept for the sky, and wherever the texel cannot be read.
float3 LookTexel(uint2 texel, LookView view, float3 original)
{
    int2 pixel = int2(texel);
    float depth = LookDepthAt(pixel, view);
    if (!(depth > 0.0f))
        return original;

    float2 centre = float2(texel) + 0.5f;
    float3 position = LookPosition(centre, depth);
    float distance = length(position);

    // Normal of the surface, from the neighbouring depths: on each axis the
    // side where the depth changes least, so that no edge is crossed.
    float dl = LookDepthAt(pixel - int2(1, 0), view);
    float dr = LookDepthAt(pixel + int2(1, 0), view);
    float du = LookDepthAt(pixel - int2(0, 1), view);
    float dd = LookDepthAt(pixel + int2(0, 1), view);
    float gapL = dl > 0.0f ? abs(dl - depth) : 1e30f;
    float gapR = dr > 0.0f ? abs(dr - depth) : 1e30f;
    float gapU = du > 0.0f ? abs(du - depth) : 1e30f;
    float gapD = dd > 0.0f ? abs(dd - depth) : 1e30f;
    float stepX = min(gapL, gapR);
    float stepY = min(gapU, gapD);
    if (stepX >= 1e30f || stepY >= 1e30f)
        return original;

    float3 alongX = gapR < gapL
        ? LookPosition(centre + float2(1.0f, 0.0f), dr) - position
        : position - LookPosition(centre - float2(1.0f, 0.0f), dl);
    float3 alongY = gapD < gapU
        ? LookPosition(centre + float2(0.0f, 1.0f), dd) - position
        : position - LookPosition(centre - float2(0.0f, 1.0f), du);
    float3 normal = cross(alongX, alongY);
    float normalLength = length(normal);
    if (!(normalLength > 0.0f))
        return original;
    normal /= normalLength;

    // The frame the texels are fastened to: the camera itself very close to
    // it (the tool in hand); the grid near the player for the points inside
    // its box (one block of margin included); the surroundings (gravity or
    // world) for everything else, so that the ground and the asteroids keep
    // their texels while the ship flies.
    float3x3 axes;
    float3 camera;
    float3x3 gridAxes = float3x3(LookAxisX.xyz, LookAxisY.xyz, LookAxisZ.xyz);
    float3 inGrid = mul(gridAxes, position);
    bool onGrid = LookBoxMin.w > 0.5f && all(inGrid >= LookBoxMin.xyz) && all(inGrid <= LookBoxMax.xyz);
    if (distance < LookCamera.w)
    {
        axes = transpose((float3x3)frame_.Environment.view_matrix);
        camera = float3(0.0f, 0.0f, 0.0f);
    }
    else if (onGrid)
    {
        axes = gridAxes;
        camera = LookCamera.xyz;
    }
    else
    {
        axes = float3x3(LookAroundX.xyz, LookAroundY.xyz, LookAroundZ.xyz);
        camera = LookAroundCamera.xyz;
    }
    float3 n = mul(axes, normal);
    float3 a = mul(axes, position) + camera;

    // The texels lie along the two frame axes closest to the surface. Near a
    // tie (a 45 degree slope), up first, then X: never a mix of both.
    float3 weight = abs(n) + float3(0.02f, 0.05f, 0.0f);
    float3 en, eu, ev;
    if (weight.y >= weight.x && weight.y >= weight.z)
    {
        en = float3(0.0f, 1.0f, 0.0f); eu = float3(1.0f, 0.0f, 0.0f); ev = float3(0.0f, 0.0f, 1.0f);
    }
    else if (weight.x >= weight.z)
    {
        en = float3(1.0f, 0.0f, 0.0f); eu = float3(0.0f, 1.0f, 0.0f); ev = float3(0.0f, 0.0f, 1.0f);
    }
    else
    {
        en = float3(0.0f, 0.0f, 1.0f); eu = float3(1.0f, 0.0f, 0.0f); ev = float3(0.0f, 1.0f, 0.0f);
    }
    float an = dot(a, en), au = dot(a, eu), av = dot(a, ev);
    float nn = dot(n, en), nu = dot(n, eu), nv = dot(n, ev);

    // Texel size: doubled far away until a texel covers at least the
    // smallest size on screen (a surface seen at a grazing angle counts as
    // farther). Powers of two: coarse texels fall exactly on fine ones.
    float pixelAngle = 2.0f / (abs(frame_.Environment.projection_matrix._22) * frame_.Screen.resolution.y);
    float facing = max(abs(dot(normal, position)) / max(distance, 1e-6f), 0.3f);
    float needed = LookAxisY.w * distance * pixelAngle / facing;
    float level = clamp(ceil(log2(max(needed / LookAxisX.w, 1.0f))), 0.0f, (float)LOOK_MAX_LEVEL);
    float size = LookAxisX.w * exp2(level);

    // Centre of the texel, then nine taps over it (3 x 3), all on the plane
    // of the surface. A tap counts if what is really seen there lies on that plane
    // (not something in front, not past an edge): the texel shows the
    // average of those. Half a texel of relief is allowed (rough ground).
    float cu = (floor(au / size) + 0.5f) * size;
    float cv = (floor(av / size) + 0.5f) * size;
    float third = size / 3.0f;
    float tolerance = 0.25f * size + 0.001f * distance;

    float3 sum = float3(0.0f, 0.0f, 0.0f);
    float count = 0.0f;
    [unroll]
    for (int i = 0; i < 9; i++)
    {
        float tu = cu + (float)(i % 3 - 1) * third;
        float tv = cv + (float)(i / 3 - 1) * third;
        float tn = an - (nu * (tu - au) + nv * (tv - av)) / nn;
        float3 tap = en * tn + eu * tu + ev * tv;
        float3 screen = LookProject(mul(tap - camera, axes));
        if (!(screen.z > 0.0f))
            continue;
        if (screen.x < 0.0f || screen.y < 0.0f || screen.x >= view.last.x + 1.0f || screen.y >= view.last.y + 1.0f)
            continue;
        int2 hit = int2(screen.xy);
        float tapDepth = LookDepthAt(hit, view);
        if (!(tapDepth > 0.0f))
            continue;
        float3 seen = LookPosition(float2(hit) + 0.5f, tapDepth);
        if (abs(dot(seen - position, normal)) > tolerance)
            continue;
        sum += Source[hit].xyz;
        count += 1.0f;
    }
    return count > 0.0f ? sum / count : original;
}

[numthreads(NUMTHREADS_X, NUMTHREADS_Y, 1)]
void __compute_shader(uint3 dispatchThreadID : SV_DispatchThreadID)
{
    uint2 texel = dispatchThreadID.xy;
    float2 uv = (texel + 0.5f) / frame_.Screen.resolution;
    LookView view = LookGetView();

    float3 sourceSample = Source[texel].xyz;
    if (all(int2(texel) <= view.last))
        sourceSample = LookTexel(texel, view, sourceSample);

    // No film grain: it changes every frame and would stir the plain texels.
    float3 color = sourceSample;

#ifndef DISABLE_TONEMAPPING
    float3 exposed_color = ExposedColor(sourceSample, 0);
    float dirt = Dirt.SampleLevel(BilinearSampler, uv, 0) * frame_.Post.BloomDirtRatio + (1 - frame_.Post.BloomDirtRatio);
    color = exposed_color + Bloom.SampleLevel(BilinearSampler, uv, 0).xyz * frame_.Post.BloomMult * dirt;
    color = ToneMapFilmic_Hable(color, frame_.Post.WhitePoint);
#endif

#ifndef DISABLE_COLOR_FILTERS
    color = ApplyBasicFilters(color);
    color = VibranceFilter(color);
    color = SepiaFilter(color);
#endif

    // Extra saturation around the luminance: plain, bright colours. The
    // luminance itself is unchanged.
    float luminance = dot(color, float3(0.2126f, 0.7152f, 0.0722f));
    color = max(luminance + (color - luminance) * (1.0f + LookAxisZ.w), 0.0f);

    color = saturate(color);
    color = rgb_to_srgb(color);

#ifdef FILL_ALPHA_LUMINANCE
    Destination[texel] = float4(color, GetRelativeLuminance(color));
#else
    Destination[texel] = float4(color, 1);
#endif
}
";
    }
}
