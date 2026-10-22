namespace ClassNex.Models;


public sealed record CourseRef(int EnableDay, string Weeks, Schedule Schedule, Course Course)
{
    public string WeeksText => Weeks switch
    {
        "odd" => "单周",
        "even" => "双周",
        _ => "每周",
    };
}


public sealed record CellTarget(int EnableDay, string Start, string End);
