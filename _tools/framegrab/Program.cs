using Windows.Media.Editing;
using Windows.Storage;

// 用法: framegrab <mp4> <输出目录> <要抽的帧数>
var path = args[0];
var outDir = args[1];
var frames = int.Parse(args[2]);

Directory.CreateDirectory(outDir);

var file = await StorageFile.GetFileFromPathAsync(path);
var clip = await MediaClip.CreateFromFileAsync(file);
var composition = new MediaComposition();
composition.Clips.Add(clip);

var duration = composition.Duration;
Console.WriteLine($"视频时长: {duration.TotalSeconds:0.0}s");

for (var i = 0; i < frames; i++)
{
    var t = duration.TotalSeconds * (i + 0.5) / frames;
    var outPath = System.IO.Path.Combine(outDir, $"frame_{i + 1}_{t:0.0}s.png");

    try
    {
        using var thumb = await composition.GetThumbnailAsync(
            TimeSpan.FromSeconds(t), 0, 0, VideoFramePrecision.NearestFrame);
        await using var fs = File.Create(outPath);
        await thumb.AsStreamForRead().CopyToAsync(fs);
        Console.WriteLine($"OK  {outPath}  {new FileInfo(outPath).Length} 字节");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL {t:0.0}s: {ex.Message}");
    }
}
