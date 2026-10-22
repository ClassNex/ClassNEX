using ClassNex.Models;

namespace ClassNex.ViewModels;


public sealed class SubjectItem : ViewModelBase
{
    public SubjectItem(Subject model) => Model = model;

    public Subject Model { get; }

    public string Name
    {
        get => Model.Name;
        set
        {
            if (Model.Name == value)
                return;
            Model.Name = value;
            OnPropertyChanged();
        }
    }

    public string SimplifiedName
    {
        get => Model.SimplifiedName ?? "";
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
            if (Model.SimplifiedName == normalized)
                return;
            Model.SimplifiedName = normalized;
            OnPropertyChanged();
        }
    }

    public string Teacher
    {
        get => Model.Teacher ?? "";
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
            if (Model.Teacher == normalized)
                return;
            Model.Teacher = normalized;
            OnPropertyChanged();
        }
    }

    public string Room
    {
        get => Model.Room ?? "";
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
            if (Model.Room == normalized)
                return;
            Model.Room = normalized;
            OnPropertyChanged();
        }
    }
}
