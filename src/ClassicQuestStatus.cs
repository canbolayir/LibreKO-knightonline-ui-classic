using Godot;
using LibreKO.Network;
using System.Text.RegularExpressions;
namespace KnightOnlineUiClassic;

/// <summary>Consistent readable status ink for quest captions and native NPC menu tags.</summary>
public static class ClassicQuestStatus
{
    public static Color Ink(QuestViewState state) => new(state switch
    {
        QuestViewState.Available=>"e4c27e",
        QuestViewState.InProgress=>"94c9ed",
        QuestViewState.Claimable=>"a0db92",
        QuestViewState.Completed=>"aaa69e",
        _=>"dd9292",
    });
    public static void StyleCaption(Label label)
    {
        int state=label.GetMeta("quest_status").AsInt32();
        if(label.HasMeta("classic_status_ink") && label.GetMeta("classic_status_ink").AsInt32()==state) return;
        label.SetMeta("classic_status_ink",state);
        label.AddThemeFontOverride("font",Plugin.Kit.Bold);
        label.AddThemeColorOverride("font_color",Ink((QuestViewState)state));
        label.AddThemeColorOverride("font_shadow_color",Colors.Black);
        label.AddThemeConstantOverride("shadow_offset_x",1);label.AddThemeConstantOverride("shadow_offset_y",1);
    }
    public static void StyleOption(Button button)
    {
        if(button.HasMeta("classic_status_text") && button.GetMeta("classic_status_text").AsString()==button.Text) return;
        button.SetMeta("classic_status_text",button.Text);
        var tag=Regex.Match(button.Text,@"^(?:\d+\.\s*)?\[(In progress|Ready|Completed|Available)\]\s",RegexOptions.IgnoreCase);
        if(!tag.Success) return;
        var state=tag.Groups[1].Value.ToLowerInvariant() switch
        {
            "in progress"=>QuestViewState.InProgress,"ready"=>QuestViewState.Claimable,
            "completed"=>QuestViewState.Completed,_=>QuestViewState.Available,
        };
        button.SetMeta("quest_status",(int)state);
        var ink=Ink(state);
        button.AddThemeColorOverride("font_color",ink);
        foreach(var name in new[]{"font_hover_color","font_pressed_color","font_hover_pressed_color","font_focus_color"})
            button.AddThemeColorOverride(name,ink.Lerp(Colors.White,.45f));
    }
}
