namespace CipherVault.Core.Events;

public sealed class EntryCopiedEvent
{
    public string EntryId { get; init; } = string.Empty;
    public string FieldType { get; init; } = string.Empty;
}
