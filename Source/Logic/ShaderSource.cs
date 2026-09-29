namespace SirDiorama
{
    // The plugin's variant of the game's final colour pass
    // (Postprocess/Tonemapping/Main.hlsl), written by the plugin into the
    // player's folder and compiled by the game itself (MyShaderCompiler.Compile,
    // then MyComputeShaders.Create), with the game's own headers.
    //
    // Why here: it is the last step that sees the scene in HDR, so a light can
    // still shine through the blur and spread into a disc; and it comes before
    // the selection highlight, the billboards and FXAA. The interface (HUD,
    // menus, texts) is drawn much later, by RenderMainSprites: it stays sharp.
    public static class ShaderSource
    {
        public const string FileName = "Diorama.hlsl";

        // Scene depth. No file of the game's Content/Shaders declares t30 or
        // t31, and the game's pass only uses t0 to t3.
        public const int DepthSlot = 31;

        // Focus of the previous frame (read) and of this frame (written): two
        // tiny buffers swapped every frame, so that the focus eases smoothly
        // from one frame to the next.
        public const int FocusInSlot = 30;
        public const int FocusOutSlot = 1;

        // Game headers the variant depends on, relative to Content/Shaders.
        // Included between angle brackets: the game's compiler then looks for
        // them in its own shader folder, wherever this file was written.
        public static readonly string[] GameHeaders =
        {
            "Postprocess/Tonemapping/Filters.hlsli",
            "Postprocess/Tonemapping/Defines.hlsli",
            "Random.hlsli",
        };

        public static string Text
        {
            get
            {
                return Header
                    + "#include <" + GameHeaders[0] + ">\n"
                    + "#include <" + GameHeaders[1] + ">\n"
                    + "#include <" + GameHeaders[2] + ">\n"
                    + "\n"
                    + "Texture2D<float> DioramaDepth : register(t" + DepthSlot + ");\n"
                    + "StructuredBuffer<float2> DioramaFocusIn : register(t" + FocusInSlot + ");\n"
                    + "RWStructuredBuffer<float2> DioramaFocusOut : register(u" + FocusOutSlot + ");\n"
                    + "\n"
                    + "#define DIORAMA_FAR " + ShaderVariants.Float(Focus.Far) + "\n"
                    + "#define DIORAMA_NEAR " + ShaderVariants.Float(Focus.Near) + "\n"
                    + "#define DIORAMA_SHARP_BAND " + ShaderVariants.Float(Focus.SharpBand) + "\n"
                    + "#define DIORAMA_BLUR_GAIN " + ShaderVariants.Float(Focus.BlurGain) + "\n"
                    + "#define DIORAMA_FOLLOW_TIME " + ShaderVariants.Float(Focus.FollowTime) + "\n"
                    + Body;
            }
        }

        private const string Header =
@"// Sir Diorama: big pixels and miniature look, in the manner of The Touryst.
// Variant of Postprocess/Tonemapping/Main.hlsl, written by the plugin at every
// game start and compiled by the game itself. Do not edit by hand.
//
// For every big pixel: miniature blur in HDR (lights spread into discs), then
// the game's own final colours (exposure, bloom, filmic curve, colour
// filters), then the reduced palette with its ordered dither, in sRGB.

";

        private const string Body =
@"
#ifndef DIORAMA_PIXELS
#define DIORAMA_PIXELS 1
#endif
#ifndef DIORAMA_PIXEL_SIZE
#define DIORAMA_PIXEL_SIZE 4
#endif
#ifndef DIORAMA_LEVELS_R
#define DIORAMA_LEVELS_R 3
#endif
#ifndef DIORAMA_LEVELS_G
#define DIORAMA_LEVELS_G 3
#endif
#ifndef DIORAMA_LEVELS_B
#define DIORAMA_LEVELS_B 3
#endif
#ifndef DIORAMA_MINIATURE
#define DIORAMA_MINIATURE 1
#endif
#ifndef DIORAMA_BLUR_RADIUS
#define DIORAMA_BLUR_RADIUS 0.015f
#endif
#ifndef DIORAMA_TAPS
#define DIORAMA_TAPS 60
#endif

#if DIORAMA_PIXELS
#define DIORAMA_N DIORAMA_PIXEL_SIZE
#else
#define DIORAMA_N 1
#endif

#define DIORAMA_GOLDEN_ANGLE 2.39996323f
#define DIORAMA_GROUP_SIZE (NUMTHREADS_X * NUMTHREADS_Y)

// One entry per big pixel of the group: a group of 8x8 screen pixels covers
// at most 8x8 big pixels (size 1) and 4x4 of them for any larger size.
groupshared float4 DioramaTile[DIORAMA_GROUP_SIZE];
groupshared float DioramaFocusShared;

// Area that the source, the depth and the screen all cover, in pixels.
struct DioramaView
{
    float2 size;
    float2 sourceInverse;
};

DioramaView DioramaGetView()
{
    uint sourceWidth, sourceHeight, depthWidth, depthHeight;
    Source.GetDimensions(sourceWidth, sourceHeight);
    DioramaDepth.GetDimensions(depthWidth, depthHeight);

    DioramaView view;
    view.size = min(min(float2(sourceWidth, sourceHeight), float2(depthWidth, depthHeight)), frame_.Screen.resolution);
    view.sourceInverse = 1.0f / float2(sourceWidth, sourceHeight);
    return view;
}

float2 DioramaClamp(float2 position, DioramaView view)
{
    return clamp(position, 0.5f, view.size - 0.5f);
}

// Distance along the view, in metres. The sky has no depth: it is as far as
// anything can be.
float DioramaDistance(float2 position, DioramaView view)
{
    float hw = DioramaDepth[uint2(DioramaClamp(position, view))];
    if (!IsDepthForeground(hw))
        return DIORAMA_FAR;
    return clamp(compute_depth(hw), DIORAMA_NEAR, DIORAMA_FAR);
}

// The HDR scene, bilinear, before any exposure.
float3 DioramaSample(float2 position, DioramaView view)
{
    return Source.SampleLevel(BilinearSampler, DioramaClamp(position, view) * view.sourceInverse, 0).xyz;
}

// Average of one big pixel. For sizes 2 and 4 the four bilinear taps cover it
// exactly; for the other sizes they cover it evenly.
float3 DioramaBlock(float2 centre, DioramaView view)
{
#if DIORAMA_N == 1
    return DioramaSample(centre, view);
#else
    const float o = DIORAMA_N * 0.25f;
    return 0.25f * (DioramaSample(centre + float2(-o, -o), view) + DioramaSample(centre + float2(o, -o), view)
        + DioramaSample(centre + float2(-o, o), view) + DioramaSample(centre + float2(o, o), view));
#endif
}

// Circle of confusion, in pixels. A ratio of distances, never metres: the
// look is the same whether the focus is at one metre or at ten kilometres.
float DioramaBlurRadius(float viewDistance, float focus, float maxRadius)
{
    float ratio = abs(1.0f - focus / viewDistance);
    return maxRadius * saturate((ratio - DIORAMA_SHARP_BAND) * DIORAMA_BLUR_GAIN);
}

float DioramaMedian3(float a, float b, float c)
{
    return max(min(a, b), min(max(a, b), c));
}

// Distance at the centre of the screen, as log2: the median of five samples,
// so that a single pixel on the edge of an object does not pull the focus.
float DioramaFocusTarget(DioramaView view)
{
    float2 c = view.size * 0.5f;
    float o = 0.01f * view.size.y;
    float a = log2(DioramaDistance(c + float2(-o, 0), view));
    float b = log2(DioramaDistance(c + float2(o, 0), view));
    float d = log2(DioramaDistance(c + float2(0, -o), view));
    float e = log2(DioramaDistance(c + float2(0, o), view));
    float m = log2(DioramaDistance(c, view));
    return DioramaMedian3(m, max(min(a, b), min(d, e)), min(max(a, b), max(d, e)));
}

// Eases from the previous frame's focus towards the target, whatever the
// frame rate. Without a valid previous value, straight to the target.
float DioramaFollow(float2 previous, float target)
{
    if (!(previous.y > 0.5f) || !(abs(previous.x) < 40.0f))
        return target;
    float t = 1.0f - exp(-max(frame_.frameTimeDelta, 0.0f) / DIORAMA_FOLLOW_TIME);
    return previous.x + (target - previous.x) * t;
}

// Miniature blur, gathered in HDR around the centre of the big pixel. The
// taps sit on a golden angle spiral that fills a disc evenly. A tap counts
// when its own blur reaches the centre: a blurred foreground spreads over a
// sharp background, and a background never bleeds onto a sharper foreground.
// A bright light seen out of focus is reached from everywhere within its own
// blur radius: it spreads into a disc.
float3 DioramaGather(float2 centre, float focus, DioramaView view)
{
    float3 sharp = DioramaBlock(centre, view);
#if DIORAMA_MINIATURE
    float maxRadius = DIORAMA_BLUR_RADIUS * frame_.Screen.resolution.y;
    if (maxRadius < 0.5f)
        return sharp;

    float centreDistance = DioramaDistance(centre, view);
    float centreRadius = DioramaBlurRadius(centreDistance, focus, maxRadius);
    float spacing = maxRadius * rsqrt((float)DIORAMA_TAPS);

    float3 sum = sharp;
    float count = 1.0f;
    float2 direction = float2(1.0f, 0.0f);
    const float2 turn = float2(cos(DIORAMA_GOLDEN_ANGLE), sin(DIORAMA_GOLDEN_ANGLE));

    [loop]
    for (int k = 0; k < DIORAMA_TAPS; k++)
    {
        float r = maxRadius * sqrt((k + 0.5f) / DIORAMA_TAPS);
        float2 position = centre + direction * r;
        direction = float2(direction.x * turn.x - direction.y * turn.y, direction.x * turn.y + direction.y * turn.x);

        float tapDistance = DioramaDistance(position, view);
        float radius = DioramaBlurRadius(tapDistance, focus, maxRadius);
        if (tapDistance > centreDistance)
            radius = min(radius, centreRadius * 2.0f);

        float weight = smoothstep(r - spacing, r + spacing, radius);
        sum += lerp(sum / count, DioramaSample(position, view), weight);
        count += 1.0f;
    }
    return sum / count;
#else
    return sharp;
#endif
}

// Linear light of one sRGB channel, as Math/Color.hlsli computes it.
float DioramaLinear(float s)
{
    return s <= 0.04045f ? s / 12.92f : pow((s + 0.055f) / 1.055f, 2.4f);
}

// One sRGB channel on evenly spaced levels. Between the level below and the
// level above, the Bayer threshold (1/16 to 16/16) picks one: the share of big
// pixels taking the upper level is rounded DOWN, in linear light, so an area
// is never brighter than its original colour. Black stays black.
float DioramaQuantize(float value, float levels, float threshold)
{
    float steps = levels - 1.0f;
    float lower = floor(saturate(value) * steps);
    if (lower >= steps)
        return 1.0f;
    float low = lower / steps;
    float high = (lower + 1.0f) / steps;
    float share = (DioramaLinear(saturate(value)) - DioramaLinear(low)) / (DioramaLinear(high) - DioramaLinear(low));
    return share >= threshold ? high : low;
}

static const float DioramaBayer[16] =
{
    0, 8, 2, 10,
    12, 4, 14, 6,
    3, 11, 1, 9,
    15, 7, 13, 5
};

// The threshold depends on the position of the big pixel alone, never on
// time: the dither does not flicker while nothing moves.
float3 DioramaPalette(float3 srgb, uint2 block)
{
    float threshold = (DioramaBayer[(block.y & 3) * 4 + (block.x & 3)] + 1.0f) / 16.0f;
    return float3(
        DioramaQuantize(srgb.r, DIORAMA_LEVELS_R, threshold),
        DioramaQuantize(srgb.g, DIORAMA_LEVELS_G, threshold),
        DioramaQuantize(srgb.b, DIORAMA_LEVELS_B, threshold));
}

// Final colour of one big pixel, as the game computes a pixel
// (Postprocess/Tonemapping/Main.hlsl), from the blurred HDR colour.
float4 DioramaShade(uint2 block, float focus, DioramaView view)
{
    float2 centre = (float2(block) + 0.5f) * DIORAMA_N;
    float2 uv = centre / frame_.Screen.resolution;

    float3 sourceSample = DioramaGather(centre, focus, view);

#if !DIORAMA_PIXELS
    // The game's film grain, only without big pixels: it changes every frame,
    // and would make the dither flicker.
    uint2 texel = block;
    if (frame_.Post.GrainStrength > 0)
    {
        RandomGenerator random;
        float grainRounding = 1;
        if (frame_.Post.GrainSize > 0)
        {
            int gs = frame_.Post.GrainSize * 2 + 1;
            float2 grainDist = (float2)(texel % gs) - frame_.Post.GrainSize;
            grainRounding = 1 - dot(grainDist, grainDist) / (frame_.Post.GrainSize * frame_.Post.GrainSize * 2.0f);
            random.SetSeed(((texel.x + gs) / gs)*((texel.y + gs) / gs)*int(frame_.frameTime*1000));
        }
        else random.SetSeed(texel.x * texel.y * int(frame_.frameTime*1000));
        sourceSample -= saturate(frame_.Post.GrainAmount - random.GetFloat()) * grainRounding * frame_.Post.GrainStrength;
    }
#endif

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

    color = saturate(color);
    color = rgb_to_srgb(color);

#if DIORAMA_PIXELS
    color = DioramaPalette(color, block);
#endif

#ifdef FILL_ALPHA_LUMINANCE
    return float4(color, GetRelativeLuminance(color));
#else
    return float4(color, 1);
#endif
}

[numthreads(NUMTHREADS_X, NUMTHREADS_Y, 1)]
void __compute_shader(uint3 dispatchThreadID : SV_DispatchThreadID, uint3 groupID : SV_GroupID, uint3 groupThreadID : SV_GroupThreadID)
{
    DioramaView view = DioramaGetView();
    uint localIndex = groupThreadID.y * NUMTHREADS_X + groupThreadID.x;

    // Focus, once per group; the first group keeps it for the next frame.
    float focus = DIORAMA_FAR;
#if DIORAMA_MINIATURE
    if (localIndex == 0)
    {
        float next = DioramaFollow(DioramaFocusIn[0], DioramaFocusTarget(view));
        DioramaFocusShared = next;
        if (groupID.x == 0 && groupID.y == 0)
            DioramaFocusOut[0] = float2(next, 1.0f);
    }
    GroupMemoryBarrierWithGroupSync();
    focus = exp2(DioramaFocusShared);
#endif

    // Each big pixel touched by this group is computed once, by one thread.
    uint2 tileOrigin = groupID.xy * uint2(NUMTHREADS_X, NUMTHREADS_Y);
    uint2 firstBlock = tileOrigin / DIORAMA_N;
    uint2 lastBlock = (tileOrigin + uint2(NUMTHREADS_X, NUMTHREADS_Y) - 1) / DIORAMA_N;
    uint2 blocks = lastBlock - firstBlock + 1;
    if (localIndex < blocks.x * blocks.y)
    {
        uint2 block = firstBlock + uint2(localIndex % blocks.x, localIndex / blocks.x);
        DioramaTile[localIndex] = DioramaShade(block, focus, view);
    }
    GroupMemoryBarrierWithGroupSync();

    uint2 texel = dispatchThreadID.xy;
    uint2 mine = texel / DIORAMA_N - firstBlock;
    Destination[texel] = DioramaTile[mine.y * blocks.x + mine.x];
}
";
    }
}
