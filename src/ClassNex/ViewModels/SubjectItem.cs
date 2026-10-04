using ClassNex.Models;

namespace ClassNex.ViewModels;

/// <summary>科目列表项：包装 CsesSubject，让编辑时列表能实时刷新。</summary>
public sealed class SubjectItem : ViewModelBase
{
    private readonly CsesSubject _model;

    public SubjectItem(CsesSubject model) => _model = model;

    public CsesSubject Model => _model;

    public string Name
    {
        get => _model.Name;
        set
        {
            if (_model.Name == value)
                return;
            _model.Name = value;
            OnPropertyChanged();
        }
    }

    public string SimplifiedName
    {
        get => _model.SimplifiedName ?? "";
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
            if (_model.SimplifiedName == normalized)
                return;
            _model.SimplifiedName = normalized;
            OnPropertyChanged();
        }
    }

    public string Teacher
    {
        get => _model.Teacher ?? "";
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
            if (_model.Teacher == normalized)
                return;
            _model.Teacher = normalized;
            OnPropertyChanged();
        }
    }

    public string Room
    {
        get => _model.Room ?? "";
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
            if (_model.Room == normalized)
                return;
            _model.Room = normalized;
            OnPropertyChanged();
        }
    }
}
