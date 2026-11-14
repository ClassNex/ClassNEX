using YamlDotNet.Serialization;

namespace ClassNex.Models;


public sealed class Subject
{
    
    public string Name { get; set; } = "";

    
    public string? SimplifiedName { get; set; }

    
    public string? Teacher { get; set; }

    
    public string? Room { get; set; }

    
    [YamlIgnore]
    public bool IsOutDoor { get; set; }

    
    [YamlIgnore]
    public string? Color { get; set; }

    
    [YamlIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(SimplifiedName) ? Name : SimplifiedName!;
}
