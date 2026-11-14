namespace ClassNex.Models;


public sealed class Schedule
{
    public string Name { get; set; } = "";

    
    public int EnableDay { get; set; } = 1;

    
    public string Weeks { get; set; } = "all";

    public List<Course> Classes { get; set; } = new();
}
