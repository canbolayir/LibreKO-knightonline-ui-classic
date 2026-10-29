using NativeApi = KnightOnlineUiClassic.Native.Native;
using KnightOnlineUiClassic.Layout;
using Godot;
using LibreKO;
using Spec = KnightOnlineUiClassic.Layout.ClassicEventsLayout.Tournament;

namespace KnightOnlineUiClassic.Windows;

/// <summary>The arena tournament: round, participants and registration in a quest inset above the client's buttons.</summary>
public partial class ClassicTournamentPanel : ClassicEventPanel
{
    private readonly Label[] _native, _values;

    public ClassicTournamentPanel(HudWindow window, Control body, WorldRoot world) : base(window, body, world, Spec.Size)
    {
        Inset(Spec.Info);
        string[] captions = { "Round", "Participants", "Status" };
        _native = new[] { Find<Label>("tournament_round"), Find<Label>("tournament_count"), Find<Label>("tournament_state") };
        _values = new Label[3];
        for (int row = 0; row < 3; row++)
        {
            Text(captions[row], Spec.Caption(row), Caption);
            _native[row].Visible = false;
            _values[row] = Text("", Spec.Value(row), Ink, HorizontalAlignment.Right);
            _values[row].Name = "tournament_value_" + row;
        }
        Action(Find<Button>("tournament_register"), Spec.Register);
        Action(Find<Button>("tournament_unregister"), Spec.Unregister);
        Refresh();
    }

    protected override void Refresh()
    {
        _values[0].Text = After(_native[0].Text);
        _values[1].Text = After(_native[1].Text);
        _values[2].Text = NativeEventWindows.Tournament(World).PendingSince != 0 ? "Please wait…" : _native[2].Text;
        bool registered = NativeApi.Get<bool>(World, "Tournament.registered");
        ThemeNames.SetColor(_values[2], HotNames.FontColor, NativeEventWindows.Tournament(World).PendingSince != 0 ? Amount : registered ? Accepted : Muted);
    }
}

/// <summary>Godot names used every frame in this file, created once (a literal creates a finalizable wrapper per call).</summary>
file static class HotNames
{
    internal static readonly StringName FontColor = "font_color";
}
