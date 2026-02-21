using ImGuiNET;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Openthesia.Core;
using Openthesia.Enums;
using Openthesia.Settings;
using Openthesia.Ui.Helpers;
using System.Numerics;

namespace Openthesia.Ui;

public class PianoRenderer
{
    static uint _black        = ImGui.GetColorU32(ImGuiTheme.HtmlToVec4("#141414"));
    static uint _white        = ImGui.GetColorU32(ImGuiTheme.HtmlToVec4("#FFFFFF"));
    static uint _whitePressed = ImGui.GetColorU32(ImGuiTheme.HtmlToVec4("#888888"));
    static uint _blackPressed = ImGui.GetColorU32(ImGuiTheme.HtmlToVec4("#555555"));

    public static float   Width;
    public static float   Height;
    public static Vector2 P;

    public static Dictionary<SevenBitNumber, int> WhiteNoteToKey = new();
    public static Dictionary<SevenBitNumber, int> BlackNoteToKey = new();

    // All 52 white-key MIDI notes of a full 88-key piano (A0=21 … C8=108).
    private static readonly int[] AllWhiteKeys =
        Enumerable.Range(21, 88)
                  .Where(n => IsWhiteKey(n))
                  .ToArray();

    // Cached MIDI note range of the currently loaded file.
    // Updated once when a file is loaded (see SetNoteRange).
    private static int _midiMin = 21;
    private static int _midiMax = 108;

    // ---------- public helpers ----------

    /// <summary>Call this once after loading a MIDI file so the auto-range works.</summary>
    public static void SetNoteRange(IEnumerable<Note> notes)
    {
        var list = notes?.ToList();
        if (list == null || list.Count == 0) { _midiMin = 21; _midiMax = 108; return; }
        _midiMin = list.Min(n => (int)n.NoteNumber);
        _midiMax = list.Max(n => (int)n.NoteNumber);
    }

    /// <summary>Returns true if the MIDI note is within the currently visible keyboard range.</summary>
    public static bool IsNoteVisible(int midiNote) =>
        WhiteNoteToKey.ContainsKey((SevenBitNumber)midiNote) ||
        BlackNoteToKey.ContainsKey((SevenBitNumber)midiNote);

    // ---------- private helpers ----------

    /// <summary>A MIDI note is a white key when its position in the octave is C D E F G A B.</summary>
    private static bool IsWhiteKey(int midiNote)
    {
        int p = midiNote % 12;
        return p == 0 || p == 2 || p == 4 || p == 5 || p == 7 || p == 9 || p == 11;
    }

    /// <summary>
    /// A white key has a black key immediately after it unless it is an E or a B
    /// (the two semitone-gap positions in the chromatic scale).
    /// This check is based on the actual MIDI note, so it is correct for any starting note.
    /// </summary>
    private static bool HasBlackAfter(int whiteMidiNote)
    {
        int p = whiteMidiNote % 12;
        return p != 4 && p != 11; // not E, not B
    }

    /// <summary>
    /// Returns the starting white-key MIDI note for the current zoom preset,
    /// auto-centred on the notes of the loaded file.
    /// For the Full preset the range is always A0–C8.
    /// </summary>
    private static (int startWhite, int numWhite) GetZoomData()
    {
        int numWhite = CoreSettings.KeyboardZoom switch
        {
            KeyboardZoom.Keys61  => 36,
            KeyboardZoom.Keys49  => 29,
            KeyboardZoom.OneHand => 15,
            _                    => 52,  // Full
        };

        if (CoreSettings.KeyboardZoom == KeyboardZoom.Full)
            return (AllWhiteKeys[0], 52); // always A0

        // Find the white-key index of the lowest note (or the first white key >= minNote).
        int minIdx = Array.FindIndex(AllWhiteKeys, k => k >= _midiMin);
        if (minIdx < 0) minIdx = 0;

        // Find the white-key index of the highest note (or the last white key <= maxNote).
        int maxIdx = Array.FindLastIndex(AllWhiteKeys, k => k <= _midiMax);
        if (maxIdx < 0) maxIdx = AllWhiteKeys.Length - 1;

        // Centre the preset window over the note range.
        int centerIdx = (minIdx + maxIdx) / 2;
        int startIdx  = Math.Clamp(centerIdx - numWhite / 2, 0, AllWhiteKeys.Length - numWhite);

        return (AllWhiteKeys[startIdx], numWhite);
    }

    // ---------- main render ----------

    public static void RenderKeyboard()
    {
        ImGui.PushFont(FontController.Font16_Icon12);
        ImDrawListPtr draw_list = ImGui.GetWindowDrawList();
        P = ImGui.GetCursorScreenPos();

        var (startWhiteKey, numWhiteKeys) = GetZoomData();

        Width  = ImGui.GetIO().DisplaySize.X / numWhiteKeys;
        Height = ImGui.GetIO().DisplaySize.Y - ImGui.GetIO().DisplaySize.Y * 76f / 100;

        // Rebuild every frame so zoom/file changes take effect immediately.
        WhiteNoteToKey.Clear();
        BlackNoteToKey.Clear();

        // ── Pass 1: detect black-key clicks (must precede white-key hit-test) ──────
        bool blackKeyClicked = false;
        {
            int note = startWhiteKey;
            for (int key = 0; key < numWhiteKeys; key++)
            {
                if (HasBlackAfter(note))
                {
                    int blackNote = note + 1;
                    Vector2 min = new(P.X + key * Width + Width * 3 / 4, P.Y);
                    Vector2 max = new(P.X + key * Width + Width * 5 / 4 + 1, P.Y + Height / 1.5f);
                    if (ImGui.IsMouseHoveringRect(min, max) && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                        blackKeyClicked = true;

                    note += 2; // skip the black key
                }
                else
                {
                    note++;
                }
            }
        }

        // ── Pass 2: render white keys ────────────────────────────────────────────
        {
            int note = startWhiteKey;
            for (int key = 0; key < numWhiteKeys; key++)
            {
                bool hasBlack = HasBlackAfter(note);
                uint col = _white;

                if (ImGui.IsMouseHoveringRect(new(P.X + key * Width, P.Y), new(P.X + key * Width + Width, P.Y + Height))
                    && ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                    && !CoreSettings.KeyboardInput && !blackKeyClicked)
                {
                    IOHandle.OnEventReceived(null,
                        new Melanchall.DryWetMidi.Multimedia.MidiEventReceivedEventArgs(
                            new NoteOnEvent((SevenBitNumber)note, new SevenBitNumber(127))));
                    DevicesManager.ODevice?.SendEvent(new NoteOnEvent((SevenBitNumber)note, new SevenBitNumber(127)));
                }

                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && !CoreSettings.KeyboardInput)
                {
                    if (IOHandle.PressedKeys.Contains(note))
                    {
                        IOHandle.OnEventReceived(null,
                            new Melanchall.DryWetMidi.Multimedia.MidiEventReceivedEventArgs(
                                new NoteOffEvent((SevenBitNumber)note, new SevenBitNumber(0))));
                        DevicesManager.ODevice?.SendEvent(new NoteOffEvent((SevenBitNumber)note, new SevenBitNumber(0)));
                    }
                }

                if (IOHandle.PressedKeys.Contains(note))
                    col = CoreSettings.KeyPressColorMatch ? ImGui.GetColorU32(ThemeManager.RightHandCol) : _whitePressed;

                var offset = IOHandle.PressedKeys.Contains(note) ? 2 : 0;

                draw_list.AddImageRounded(Drawings.C,
                    new Vector2(P.X + key * Width, P.Y) + new Vector2(offset, 0),
                    new Vector2(P.X + key * Width + Width, P.Y + Height) + new Vector2(offset, 0),
                    Vector2.Zero, Vector2.One, col, 5, ImDrawFlags.RoundCornersBottom);

                WhiteNoteToKey[(SevenBitNumber)note] = key;

                // Octave label on every C key
                if (note % 12 == 0)
                {
                    int octave = note / 12 - 1;
                    var text = $"C{octave}";
                    ImGui.GetForegroundDrawList().AddText(
                        new(P.X + key * Width + Width / 2 - ImGui.CalcTextSize(text).X / 2,
                            P.Y + Height - 25 * FontController.DSF),
                        _black, text);
                }

                note++;
                if (hasBlack) note++; // skip the black key
            }
        }

        // ── Pass 3: render black keys ────────────────────────────────────────────
        {
            int note = startWhiteKey;
            for (int key = 0; key < numWhiteKeys; key++)
            {
                bool hasBlack = HasBlackAfter(note);
                if (hasBlack)
                {
                    int blackNote = note + 1;
                    BlackNoteToKey[(SevenBitNumber)blackNote] = key;

                    uint col = ImGui.GetColorU32(Vector4.One);

                    if (ImGui.IsMouseHoveringRect(
                            new(P.X + key * Width + Width * 3 / 4, P.Y),
                            new(P.X + key * Width + Width * 5 / 4 + 1, P.Y + Height / 1.5f))
                        && ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                        && !CoreSettings.KeyboardInput)
                    {
                        IOHandle.OnEventReceived(null,
                            new Melanchall.DryWetMidi.Multimedia.MidiEventReceivedEventArgs(
                                new NoteOnEvent((SevenBitNumber)blackNote, new SevenBitNumber(127))));
                        DevicesManager.ODevice?.SendEvent(new NoteOnEvent((SevenBitNumber)blackNote, new SevenBitNumber(127)));
                    }

                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && !CoreSettings.KeyboardInput)
                    {
                        if (IOHandle.PressedKeys.Contains(blackNote))
                        {
                            IOHandle.OnEventReceived(null,
                                new Melanchall.DryWetMidi.Multimedia.MidiEventReceivedEventArgs(
                                    new NoteOffEvent((SevenBitNumber)blackNote, new SevenBitNumber(0))));
                            DevicesManager.ODevice?.SendEvent(new NoteOffEvent((SevenBitNumber)blackNote, new SevenBitNumber(0)));
                        }
                    }

                    if (IOHandle.PressedKeys.Contains(blackNote))
                    {
                        var v3    = new Vector3(ThemeManager.RightHandCol.X, ThemeManager.RightHandCol.Y, ThemeManager.RightHandCol.Z);
                        col = CoreSettings.KeyPressColorMatch ? ImGui.GetColorU32(new Vector4(v3, 1)) : _blackPressed;
                    }

                    var offset    = IOHandle.PressedKeys.Contains(blackNote) ? 1 : 0;
                    var blackImage = IOHandle.PressedKeys.Contains(blackNote) ? Drawings.CSharpWhite : Drawings.CSharp;

                    draw_list.AddImage(blackImage,
                        new Vector2(P.X + key * Width + Width * 3 / 4, P.Y),
                        new Vector2(P.X + key * Width + Width * 5 / 4 + 1, P.Y + Height / 1.5f) + new Vector2(offset),
                        Vector2.Zero, Vector2.One, col);

                    note += 2;
                }
                else
                {
                    note++;
                }
            }
        }

        ImGui.PopFont();
    }
}
