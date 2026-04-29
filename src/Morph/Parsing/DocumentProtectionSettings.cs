/// <summary>
/// Document protection / editing-restriction settings (w:documentProtection in settings.xml).
/// </summary>
sealed record DocumentProtectionSettings
{
    /// <summary>True when any kind of editing restriction is set.</summary>
    public bool IsProtected => EditingMode != DocumentEditingMode.None;

    /// <summary>The kind of editing the document is restricted to.</summary>
    public DocumentEditingMode EditingMode { get; init; } = DocumentEditingMode.None;
}
