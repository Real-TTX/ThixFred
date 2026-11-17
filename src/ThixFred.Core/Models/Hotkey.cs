namespace ThixFred.Core.Models;

/// <summary>
/// A global keyboard shortcut: modifiers plus one key. <see cref="Meta"/> is the Windows key on Windows and the Command
/// key on macOS. <see cref="Key"/> is a letter (A-Z), a digit (0-9), F1-F12, "Space" or "Insert".
/// Stored as text, e.g. "Ctrl+Win+V" (see <see cref="Business.HotkeyService.Parse"/>).
/// </summary>
public sealed record Hotkey(bool Control, bool Alt, bool Shift, bool Meta, string Key);
