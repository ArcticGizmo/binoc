using SkiaSharp;
using Svg.Skia;

// Generates every raster icon asset from the single source-of-truth SVG (binoc.svg).
//
//   src/Binoc.App/Assets/binoc.png   256x256 PNG — window icon and the in-app logo
//   src/Binoc.App/Assets/binoc.ico   multi-resolution ICO (16..256) — window + .exe ApplicationIcon
//   landing-icon.png                  512x512 PNG — the README header logo
//
// Re-run after editing binoc.svg:  dotnet run --project tools/IconGen   (or tools/gen-icons.ps1),
// then commit the regenerated assets.
//
// Rendered with Svg.Skia (SkiaSharp). Skia's edge anti-aliasing is much cleaner than GDI+ at small
// sizes, which matters here: binoc's logo is hard-edged concentric circles with thin white rings, and
// GDI+ (System.Drawing, what emuwren's IconGen used) leaves those rings muddy at 16..256px.

// Resolve the repo root from this tool's location so it works regardless of CWD.
string repoRoot = FindRepoRoot(AppContext.BaseDirectory);
string svgPath = Path.Combine(repoRoot, "binoc.svg");
if (!File.Exists(svgPath))
{
    Console.Error.WriteLine($"Source SVG not found: {svgPath}");
    return 1;
}

Console.WriteLine($"Source: {svgPath}");
using var svg = new SKSvg();
if (svg.Load(svgPath) is not { } pic)
{
    Console.Error.WriteLine("Failed to parse SVG.");
    return 1;
}

// The artwork rarely fills its viewBox exactly — there's transparent margin, and (as with binoc) it may
// be off-square. Crop to the actual drawn content, re-center it in a square, and scale that to fill the
// frame so every icon size uses the whole canvas. PAD keeps a sliver of breathing room at the edges.
const float PAD = 0.02f;
var fit = ComputeFit(pic, PAD);
Console.WriteLine($"Fit: content {fit.W:0.0}x{fit.H:0.0}, square side {fit.Side:0.0}");

// The .ico ships a true frame at each size so Windows never downscales at runtime (the taskbar asks
// for small frames; large surfaces ask for 256).
int[] icoSizes = { 16, 24, 32, 48, 64, 128, 256 };

string assetsDir = Path.Combine(repoRoot, "src", "Binoc.App", "Assets");
WritePng(pic, fit, Path.Combine(assetsDir, "binoc.png"), 256);
WritePng(pic, fit, Path.Combine(repoRoot, "landing-icon.png"), 512);
WriteIco(pic, fit, Path.Combine(assetsDir, "binoc.ico"), icoSizes);

Console.WriteLine("Done.");
return 0;

// Renders the cropped, re-centered content square at the given pixel size with a transparent background.
static byte[] RenderPng(SKPicture pic, Fit fit, int size)
{
    var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var surface = SKSurface.Create(info);
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent);
    // Map the padded content square onto the pixel canvas: scale to fill, then shift its top-left to 0,0.
    float scale = size / fit.Side;
    canvas.Scale(scale);
    canvas.Translate(-fit.OriginX, -fit.OriginY);
    canvas.DrawPicture(pic);
    canvas.Flush();
    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

static void WritePng(SKPicture pic, Fit fit, string path, int size)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, RenderPng(pic, fit, size));
    Console.WriteLine($"  {Path.GetFileName(path)}  {size}x{size}");
}

// Writes a Vista+ ICO whose frames are PNG-compressed (keeps the file small and supports 256px).
static void WriteIco(SKPicture pic, Fit fit, string path, int[] sizes)
{
    var frames = sizes.Select(s => RenderPng(pic, fit, s)).ToArray();

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var fs = File.Create(path);
    using var w = new BinaryWriter(fs);

    // ICONDIR header
    w.Write((ushort)0);             // reserved
    w.Write((ushort)1);             // type = icon
    w.Write((ushort)sizes.Length);  // image count

    // Each ICONDIRENTRY is 16 bytes; image data follows the full directory.
    int offset = 6 + sizes.Length * 16;
    for (int i = 0; i < sizes.Length; i++)
    {
        int size = sizes[i];
        w.Write((byte)(size >= 256 ? 0 : size)); // width  (0 = 256)
        w.Write((byte)(size >= 256 ? 0 : size)); // height (0 = 256)
        w.Write((byte)0);                        // palette count
        w.Write((byte)0);                        // reserved
        w.Write((ushort)1);                      // colour planes
        w.Write((ushort)32);                     // bits per pixel
        w.Write(frames[i].Length);               // bytes of image data
        w.Write(offset);                         // offset of image data
        offset += frames[i].Length;
    }

    foreach (var frame in frames)
        w.Write(frame);

    Console.WriteLine($"  {Path.GetFileName(path)}  [{string.Join(", ", sizes)}]");
}

// Walks up from the tool's binary location to the directory that holds binoc.svg.
static string FindRepoRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "binoc.svg")))
            return dir.FullName;
        dir = dir.Parent;
    }
    // Fall back to five-up from bin/<cfg>/<tfm> if the marker walk fails.
    return Path.GetFullPath(Path.Combine(start, "..", "..", "..", "..", ".."));
}

// Computes the square crop that tightly frames the SVG's drawn content. Skia's SKPicture.CullRect is the
// SVG viewport, not the drawn content, so we find the true content bounds by rendering once at a reference
// resolution and scanning for the alpha bounding box. The square is the larger content dimension, centered
// on the content, grown by `pad` on every side.
static Fit ComputeFit(SKPicture pic, float pad)
{
    var cull = pic.CullRect;
    const int Ref = 1024;
    float refScale = Ref / Math.Max(cull.Width, cull.Height);

    var info = new SKImageInfo(Ref, Ref, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var surface = SKSurface.Create(info);
    surface.Canvas.Clear(SKColors.Transparent);
    surface.Canvas.Scale(refScale);
    surface.Canvas.Translate(-cull.Left, -cull.Top);
    surface.Canvas.DrawPicture(pic);
    surface.Canvas.Flush();
    using var image = surface.Snapshot();
    using var bmp = SKBitmap.FromImage(image);

    var px = bmp.Bytes; // RGBA8888: alpha at index*4 + 3
    int minX = Ref, minY = Ref, maxX = -1, maxY = -1;
    for (int y = 0; y < Ref; y++)
        for (int x = 0; x < Ref; x++)
            if (px[(y * Ref + x) * 4 + 3] > 8)
            {
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

    if (maxX < 0) // nothing drawn — fall back to the viewport
        return new Fit(cull.Width, cull.Height, cull.Width, cull.Left, cull.Top);

    // Back to picture units.
    float bx = cull.Left + minX / refScale, by = cull.Top + minY / refScale;
    float bw = (maxX - minX + 1) / refScale, bh = (maxY - minY + 1) / refScale;

    float side = Math.Max(bw, bh);
    side += side * pad * 2f;
    float originX = bx + bw / 2f - side / 2f;
    float originY = by + bh / 2f - side / 2f;
    return new Fit(bw, bh, side, originX, originY);
}

// The crop geometry: the content's width/height, the side of the padded square that frames it, and that
// square's top-left corner in SVG user units.
readonly record struct Fit(float W, float H, float Side, float OriginX, float OriginY);
