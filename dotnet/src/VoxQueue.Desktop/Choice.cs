namespace VoxQueue.Desktop;

/// <summary>
/// A drop-down entry that shows one thing and stores another.
///
/// The settings combos used to hold raw strings like <c>"dark:Ciemny"</c> and
/// split them on the colon at read time. Dropdown paints what ToString returns,
/// so the code leaked into the label and the user saw "darkCiemny" — the colon is
/// almost invisible at this size. A pair of fields fixes that at the source:
/// display text and stored value cannot drift apart because they are the same
/// object.
/// </summary>
public sealed class Choice
{
    public string Code { get; }
    public string Text { get; }

    public Choice(string code, string text)
    {
        Code = code ?? "";
        Text = text ?? "";
    }

    /// <summary>What the drop-down draws.</summary>
    public override string ToString() => Text;
}
