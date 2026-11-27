using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClassNex.Models;

namespace ClassNex.Services;

internal sealed class CentralEditJournal
{
    internal sealed record Entry(string Binding, bool Timetable, long BaseRevision, string Value);
    private readonly object _gate = new();
    private string _binding = "";
    private readonly Entry?[] _edits = new Entry?[2];
    private readonly long[] _acceptedRevisions = new long[2];
    private static string Folder => Path.Combine(SettingsService.DataDirectory, "CentralDrafts");
    private static string FileFor(string binding, bool timetable) => Path.Combine(Folder, binding + (timetable ? "-t" : "-s") + ".pending.json");

    public bool Pending { get { lock (_gate) return _edits[0] != null || _edits[1] != null; } }

    public void Bind(CentralManagementSettings s)
    {
        var binding = !s.IsEnabled || string.IsNullOrEmpty(s.ClientId) || string.IsNullOrEmpty(s.SelectedClassId) ? "" :
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s.ServerUrl.TrimEnd('/') + "\n" + s.ClientId + "\n" + s.SelectedClassId)));
        lock (_gate)
        {
            if (binding == _binding) return;
            Entry? Read(bool timetable)
            {
                if (binding.Length == 0) return null;
                try
                {
                    var draft = JsonSerializer.Deserialize<Entry>(File.ReadAllText(FileFor(binding, timetable)));
                    return draft is { Value: not null, BaseRevision: >= 0 } && draft.Binding == binding && draft.Timetable == timetable ? draft : throw new InvalidDataException("本机集控草稿损坏，尚未覆盖。");
                }
                catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return null; }
            }
            var settings = Read(false); var timetable = Read(true);
            Array.Clear(_acceptedRevisions);
            _edits[0] = settings; _edits[1] = timetable; _binding = binding;
        }
    }

    public void Save(bool timetable, long revision, string value)
    {
        lock (_gate)
        {
            if (_binding.Length == 0) return;
            var i = timetable ? 1 : 0;
            if (_edits[i]?.Value == value) return;
            var entry = new Entry(_binding, timetable, _edits[i]?.BaseRevision ?? Math.Max(revision, _acceptedRevisions[i]), value);
            Write(FileFor(_binding, timetable), JsonSerializer.Serialize(entry));
            _edits[i] = entry;
        }
    }

    public Entry[] Snapshot() { lock (_gate) return _edits.OfType<Entry>().ToArray(); }

    public bool CanReceive(bool timetable, long remote, long applied)
    {
        lock (_gate)
        {
            var i = timetable ? 1 : 0;
            return _edits[i] == null && remote > Math.Max(applied, _acceptedRevisions[i]);
        }
    }

    public void Received(bool timetable, long revision)
    {
        lock (_gate) { var i = timetable ? 1 : 0; _acceptedRevisions[i] = Math.Max(_acceptedRevisions[i], revision); }
    }

    public void Accepted(Entry sent, long revision)
    {
        lock (_gate)
        {
            var i = sent.Timetable ? 1 : 0;
            if (_binding != sent.Binding || _edits[i] is not { } latest) return;
            var path = FileFor(_binding, sent.Timetable);
            _acceptedRevisions[i] = revision;
            if (latest == sent) { File.Delete(path); _edits[i] = null; }
            else { var next = latest with { BaseRevision = revision }; Write(path, JsonSerializer.Serialize(next)); _edits[i] = next; }
        }
    }

    public string? Archive(Entry sent)
    {
        lock (_gate)
        {
            var i = sent.Timetable ? 1 : 0;
            if (_binding != sent.Binding || _edits[i] is not { } latest) return null;
            var name = $"{_binding[..32]}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}-" + (sent.Timetable ? "timetable.yaml" : "settings.json");
            Write(Path.Combine(Folder, name), latest.Value);
            File.Delete(FileFor(_binding, sent.Timetable)); _edits[i] = null;
            return name;
        }
    }

    public (bool Timetable, string Value) ReadConflict(string name)
    {
        lock (_gate)
        {
            if (_binding.Length == 0 || name != Path.GetFileName(name) || !name.StartsWith(_binding[..32] + "-", StringComparison.Ordinal) ||
                name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-')))
                throw new InvalidOperationException("该草稿不属于当前设备和班级。");
            var timetable = name.EndsWith("-timetable.yaml", StringComparison.Ordinal);
            if (!timetable && !name.EndsWith("-settings.json", StringComparison.Ordinal)) throw new InvalidDataException("不是集控冲突草稿。");
            var path = Path.Combine(Folder, name);
            var info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException("未找到此草稿。");
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || (new DirectoryInfo(Folder).Attributes & FileAttributes.ReparsePoint) != 0 || info.Length > (timetable ? 16L : 4L) * 1024 * 1024)
                throw new InvalidDataException("不能读取该草稿文件。");
            return (timetable, File.ReadAllText(path));
        }
    }

    private static void Write(string target, string contents)
    {
        Directory.CreateDirectory(Folder);
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, contents); File.Move(temp, target, true); }
        finally { File.Delete(temp); }
    }
}
