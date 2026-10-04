using ClassNex.Models;
using ClassNex.Services;

namespace ClassNex.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private CsesDocument? _document;
    private string _sourceDescription = "";

    public CsesDocument? Document
    {
        get => _document;
        private set => SetProperty(ref _document, value);
    }

    public string SourceDescription
    {
        get => _sourceDescription;
        set => SetProperty(ref _sourceDescription, value);
    }

    public MainWindowViewModel() => ReloadDefault();

    /// <summary>加载内置示例课表。</summary>
    public void ReloadDefault()
    {
        var path = CsesService.DefaultSamplePath;
        if (File.Exists(path))
        {
            try
            {
                Document = CsesService.Load(path);
                SourceDescription = $"示例课表 · {Path.GetFileName(path)}";
            }
            catch (Exception)
            {
                Document = new CsesDocument();
                SourceDescription = "示例课表加载失败";
            }
        }
        else
        {
            Document = new CsesDocument();
            SourceDescription = "未找到课表文件";
        }
    }

    /// <summary>从指定文件重新加载课表。</summary>
    public void ReloadFrom(string path)
    {
        Document = CsesService.Load(path);
        SourceDescription = Path.GetFileName(path) ?? path;
    }
}
