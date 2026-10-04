using System.Text;
using ClassNex.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ClassNex.Services;

/// <summary>CSES（Course Schedule Exchange Schema）YAML 编解码。</summary>
public static class CsesCodec
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public static ScheduleProfile Load(string path) =>
        Parse(File.ReadAllText(path, Encoding.UTF8));

    public static ScheduleProfile Parse(string yaml) =>
        Deserializer.Deserialize<ScheduleProfile>(yaml) ?? new ScheduleProfile();

    public static void Save(ScheduleProfile profile, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, Serializer.Serialize(profile), new UTF8Encoding(false));
    }

    /// <summary>内置示例课表路径。</summary>
    public static string SamplePath => Path.Combine(AppContext.BaseDirectory, "Assets", "timetable.yaml");
}
