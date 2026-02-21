using ImGuiNET;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Openthesia.Core;
using Openthesia.Enums;
using Openthesia.Settings;
using Openthesia.Ui.Helpers;
using System.Numerics;

namespace Openthesia.Ui;

public class PianoRenderer
{
    static uint _black = ImGui.GetColorU32(ImGuiTheme.HtmlToVec4("#141414"));
    static uint _white = ImGui.GetColorU32(ImGuiTheme.HtmlToVec4("#FFFFFF"));
    static uint _whitePressed = ImGui.GetColorU32(ImGuiTheme.HtmlToVec4("#888888"));
    static uint _blackPressed = ImGui.GetColorU32(ImGuiTheme.HtmlToVec4("#555555"));

    public static float Width;
    public static float Height;
    public static Vector2 P;

    public static Dictionary<SevenBitNumber, int> WhiteNoteToKey = new();
    public static Dictionary<SevenBitNumber, int> BlackNoteToKey = new();

    // Returns (startWhiteMidi, startBlackMidi, numWhiteKeys) for each zoom preset.
    // White key counts:
    //   Full (88):   A0–C8  = 52 white keys, first black = A#0 (22)
    //   61 keys:     C2–C7  = 36 white keys, first black = C#2 (37)
    //   49 keys:     C3–C7  = 29 white keys, first black = C#3 (49)
    //   One Hand:    C4–C6  = 15 white keys, first black = C#4 (61)
    private static (int startWhite, int startBlack, int numWhite) GetZoomData() =>
        CoreSettings.KeyboardZoom switch
        {
            KeyboardZoom.Keys61  => (36, 37, 36),
            KeyboardZoom.Keys49  => (48, 49, 29),
            KeyboardZoom.OneHand => (60, 61, 15),
            _                    => (21, 22, 52),  // Full
        };

    public static bool IsNoteVisible(int midiNote) =>
        WhiteNoteToKey.ContainsKey((SevenBitNumber)midiNote) ||
        BlackNoteToKey.ContainsKey((SevenBitNumber)midiNote);

    public static void RenderKeyboard()
    {
        ImGui.PushFont(FontController.Font16_Icon12);
        ImDrawListPtr draw_list = ImGui.GetWindowDrawList();
        P = ImGui.GetCursorScreenPos();

        var (startWhiteKey, startBlackKey, numWhiteKeys) = GetZoomData();

        Width  = ImGui.GetIO().DisplaySize.X / numWhiteKeys;
        Height = ImGui.GetIO().DisplaySize.Y - ImGui.GetIO().DisplaySize.Y * 76f / 100;

        // Rebuild key maps every frame so zoom changes take effect immediately.
        WhiteNoteToKey.Clear();
        BlackNoteToKey.Clear();

        int cur_key = startBlackKey;

        /* Check if a black key is pressed (must be handled before white keys) */
        bool blackKeyClicked = false;
        for (int key = 0; key < numWhiteKeys; key++)
        {
            if (KeysUtils.HasBlack(key))
            {
                Vector2 min = new(P.X + key * Width + Width * 3 / 4, P.Y);
                Vector2 max = new(P.X + key * Width + Width * 5 / 4 + 1, P.Y + Height / 1.5f);

                if (ImGui.IsMouseHoveringRect(min, max) && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    blackKeyClicked = true;
                }

                cur_key += 2;
            }
            else
            {
                cur_key++;
            }
        }

        cur_key = startWhiteKey;
        for (int key = 0; key < numWhiteKeys; key++)
        {
            uint col = _white;

            if (ImGui.IsMouseHoveringRect(new(P.X + key * Width, P.Y), new(P.X + key * Width + Width, P.Y + Height)) && ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                && !CoreSettings.KeyboardInput && !blackKeyClicked)
            {
                IOHandle.OnEventReceived(null,
                    new Melanchall.DryWetMidi.Multimedia.MidiEventReceivedEventArgs(new NoteOnEvent((SevenBitNumber)cur_key, new SevenBitNumber(127))));
                DevicesManager.ODevice?.SendEvent(new NoteOnEvent((SevenBitNumber)cur_key, new SevenBitNumber(127)));
            }

            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && !CoreSettings.KeyboardInput)
            {
                if (IOHandle.PressedKeys.Contains(cur_key))
                {
                    IOHandle.OnEventReceived(null,
                        new Melanchall.DryWetMidi.Multimedia.MidiEventReceivedEventArgs(new NoteOffEvent((SevenBitNumber)cur_key, new SevenBitNumber(0))));
                    DevicesManager.ODevice?.SendEvent(new NoteOffEvent((SevenBitNumber)cur_key, new SevenBitNumber(0)));
                }
            }

            if (IOHandle.PressedKeys.Contains(cur_key))
            {
                var color = CoreSettings.KeyPressColorMatch ? ImGui.GetColorU32(ThemeManager.RightHandCol) : _whitePressed;
                col = color;
            }

            var offset = IOHandle.PressedKeys.Contains(cur_key) ? 2 : 0;

            draw_list.AddImageRounded(Drawings.C,
                new Vector2(P.X + key * Width, P.Y) + new Vector2(offset, 0),
                new Vector2(P.X + key * Width + Width, P.Y + Height) + new Vector2(offset, 0), Vector2.Zero, Vector2.One, col, 5, ImDrawFlags.RoundCornersBottom);

            WhiteNoteToKey.Add((SevenBitNumber)cur_key, key);

            // Draw octave label on every C note
            if (cur_key % 12 == 0)
            {
                int octave = cur_key / 12 - 1;
                var text = $"C{octave}";
                ImGui.GetForegroundDrawList().AddText(
                    new(P.X + key * Width + Width / 2 - ImGui.CalcTextSize(text).X / 2,
                        P.Y + Height - 25 * FontController.DSF),
                    _black, text);
            }

            cur_key++;
            if (KeysUtils.HasBlack(key))
            {
                cur_key++;
            }
        }

        cur_key = startBlackKey;
        for (int key = 0; key < numWhiteKeys; key++)
        {
            if (KeysUtils.HasBlack(key))
            {
                BlackNoteToKey.Add((SevenBitNumber)cur_key, key);

                uint col = ImGui.GetColorU32(Vector4.One);

                if (ImGui.IsMouseHoveringRect(new(P.X + key * Width + Width * 3 / 4, P.Y),
                    new(P.X + key * Width + Width * 5 / 4 + 1, P.Y + Height / 1.5f)) && ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                    && !CoreSettings.KeyboardInput)
                {
                    IOHandle.OnEventReceived(null,
                        new Melanchall.DryWetMidi.Multimedia.MidiEventReceivedEventArgs(new NoteOnEvent((SevenBitNumber)cur_key, new SevenBitNumber(127))));
                    DevicesManager.ODevice?.SendEvent(new NoteOnEvent((SevenBitNumber)cur_key, new SevenBitNumber(127)));
                }

                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && !CoreSettings.KeyboardInput)
                {
                    if (IOHandle.PressedKeys.Contains(cur_key))
                    {
                        IOHandle.OnEventReceived(null,
                            new Melanchall.DryWetMidi.Multimedia.MidiEventReceivedEventArgs(new NoteOffEvent((SevenBitNumber)cur_key, new SevenBitNumber(0))));
                        DevicesManager.ODevice?.SendEvent(new NoteOffEvent((SevenBitNumber)cur_key, new SevenBitNumber(0)));
                    }
                }

                if (IOHandle.PressedKeys.Contains(cur_key))
                {
                    var v3 = new Vector3(ThemeManager.RightHandCol.X, ThemeManager.RightHandCol.Y, ThemeManager.RightHandCol.Z);
                    var color = CoreSettings.KeyPressColorMatch ? ImGui.GetColorU32(new Vector4(v3, 1)) : _blackPressed;
                    col = color;
                }

                var offset = IOHandle.PressedKeys.Contains(cur_key) ? 1 : 0;
                var blackImage = IOHandle.PressedKeys.Contains(cur_key) ? Drawings.CSharpWhite : Drawings.CSharp;

                draw_list.AddImage(blackImage,
                    new Vector2(P.X + key * Width + Width * 3 / 4, P.Y),
                    new Vector2(P.X + key * Width + Width * 5 / 4 + 1, P.Y + Height / 1.5f) + new Vector2(offset), Vector2.Zero, Vector2.One, col);

                cur_key += 2;
            }
            else
            {
                cur_key++;
            }
        }

        ImGui.PopFont();
    }
}
