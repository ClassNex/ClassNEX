namespace ClassNex.Core.Abstractions;

public interface INexPresentation
{
    bool IsAvailable { get; }

    void ShowOverlay(bool activate = false);

    void SetNexText(string? text);
}
