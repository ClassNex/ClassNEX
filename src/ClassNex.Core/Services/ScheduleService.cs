using ClassNex.Models;

namespace ClassNex.Services;





public interface IScheduleService
{
    
    ScheduleProfile Profile { get; }

    
    string FilePath { get; }

    
    string FileName { get; }

    
    event Action? ProfileChanged;

    
    void Load(string path);

    
    void Reload();

    
    void Save();

    

    
    void AddCourse(int enableDay, string weeks, Course course);

    
    void RemoveCourse(int enableDay, string weeks, Course course);

    
    void UpdateCourse(int enableDay, string weeks, Course original, Course updated);

    

    Subject AddSubject(string name);

    void RemoveSubject(Subject subject);

    

    
    void ImportFrom(string path);

    
    void ExportTo(string path);
}

public sealed class ScheduleService : IScheduleService
{
    private readonly Func<AppSettings> _settings;
    private ScheduleProfile _profile = new();

    public ScheduleService(Func<AppSettings> settingsAccessor) => _settings = settingsAccessor;

    public ScheduleProfile Profile => _profile;

    public string FilePath { get; private set; } = "";

    public string FileName => string.IsNullOrEmpty(FilePath) ? "（无）" : Path.GetFileName(FilePath);

    public event Action? ProfileChanged;

    public void Load(string path)
    {
        FilePath = path;
        try
        {
            _profile = File.Exists(path) ? CsesCodec.Load(path) : new ScheduleProfile();
        }
        catch
        {
            _profile = new ScheduleProfile();
        }

        Raise();
    }

    public void Reload() => Load(FilePath);

    public void Save()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
            return;

        try
        {
            CsesCodec.Save(_profile, FilePath);
        }
        catch
        {
            
        }

        Raise();
    }

    public void AddCourse(int enableDay, string weeks, Course course)
    {
        _profile.GetOrCreateSchedule(enableDay, weeks).Classes.Add(course);
        Save();
    }

    public void RemoveCourse(int enableDay, string weeks, Course course)
    {
        Find(enableDay, weeks)?.Classes.Remove(course);
        Save();
    }

    public void UpdateCourse(int enableDay, string weeks, Course original, Course updated)
    {
        var schedule = Find(enableDay, weeks);
        if (schedule is null)
            return;

        var index = schedule.Classes.IndexOf(original);
        if (index < 0)
            return;

        schedule.Classes[index] = updated;
        Save();
    }

    public Subject AddSubject(string name)
    {
        var subject = new Subject { Name = name };
        _profile.Subjects.Add(subject);
        Save();
        return subject;
    }

    public void RemoveSubject(Subject subject)
    {
        _profile.Subjects.Remove(subject);
        Save();
    }

    public void ImportFrom(string path)
    {
        _settings().TimetablePath = path;
        Load(path);
    }

    public void ExportTo(string path)
    {
        CsesCodec.Save(_profile, path);
    }

    private Schedule? Find(int enableDay, string weeks) =>
        _profile.Schedules.FirstOrDefault(s => s.EnableDay == enableDay && s.Weeks == weeks);

    private void Raise() => ProfileChanged?.Invoke();
}
