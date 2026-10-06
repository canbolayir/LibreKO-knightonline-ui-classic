using Godot;
namespace KnightOnlineUiClassic.Layout;
/// <summary>Editable nation message-box composition used by the original trade permission flow.</summary>
public static class ClassicExchangeNoticeLayout
{
    public static Vector2 Size(bool request)=>new(327,156);
    public static Rect2 Message(bool request)=>new(21,28,283,58);
    public static readonly Rect2 Title=new(12,10,297,16);
    public static Rect2 Accept=>ClassicDesign.Karus?new(11,117,97,29):new(9,116,97,29);
    public static Rect2 Decline=>ClassicDesign.Karus?new(219,116,97,29):new(220,116,97,29);
    public static readonly Rect2 FinalPrompt=new(24,35,279,64);
    public static readonly Rect2 FinalAccept=new(58,116,97,29);
    public static readonly Rect2 FinalDecline=new(172,116,97,29);
    public static Rect2 WaitCancel=>new(115,ClassicDesign.Karus?117:116,97,29);
}
