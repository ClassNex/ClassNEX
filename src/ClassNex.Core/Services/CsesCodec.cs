using System.Text;
using ClassNex.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ClassNex.Services;

/// <summary>CSES 课表的 UTF-8 YAML 编解码器；字段使用下划线命名并容忍未知字段。</summary>
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

    /// <summary>从文件读取并解析整个档案；I/O 或格式错误交给调用方处理。</summary>
    public static ScheduleProfile Load(string path) =>
        Parse(File.ReadAllText(path, Encoding.UTF8));

    /// <summary>从 YAML 文本构造档案；空文档得到空档案。</summary>
    public static ScheduleProfile Parse(string yaml) =>
        Deserializer.Deserialize<ScheduleProfile>(yaml) ?? new ScheduleProfile();

    /// <summary>生成适于导出和集控传输的 CSES YAML 文本。</summary>
    public static string Serialize(ScheduleProfile profile) =>
        Serializer.Serialize(profile);

    /// <summary>先写入同目录临时文件，再替换目标，避免部分写入破坏旧档案。</summary>
    public static void Save(ScheduleProfile profile, string path)
    {
        AtomicFile.Write(path, Serialize(profile));
    }

    /// <summary>便携载荷内可选示例档案的位置；发行安装不保证此文件存在。</summary>
    public static string SamplePath => Path.Combine(AppContext.BaseDirectory, "Assets", "timetable.yaml");
}
