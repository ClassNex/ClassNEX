using System.Text;
using ClassNex.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ClassNex.Services;

/// <summary>CSES 课表文件的加载与解析。</summary>
public static class CsesService
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>从文件路径加载 CSES 课表。</summary>
    public static CsesDocument Load(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        return Parse(text);
    }

    /// <summary>解析 CSES YAML 文本。</summary>
    public static CsesDocument Parse(string yaml) =>
        Deserializer.Deserialize<CsesDocument>(yaml) ?? new CsesDocument();

    /// <summary>内置示例课表文件的路径（随程序输出目录分发）。</summary>
    public static string DefaultSamplePath =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "timetable.yaml");
}
