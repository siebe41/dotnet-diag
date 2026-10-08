namespace DotnetDiag.Gui.ViewModels;

/// <summary>An entry of a drop-down: what the user reads and the value it stands for.</summary>
public sealed record Choice<T>(string Label, T Value)
{
    public override string ToString() => Label;
}
