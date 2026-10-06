using Godot;
using LibreKO;
namespace KnightOnlineUiClassic.Windows;

/// <summary>Native auxiliary windows keep their lifecycle while their content belongs to Various.</summary>
public static class CharacterPageRouter
{
    public static bool Supports(string id) => id is "titles" or "presets" or "quests" or "character_clan_details" or "clanpoint";
    private static readonly Dictionary<string,CharacterEmbeddedPage> Pages=new();
    public static CharacterWindow? Owner { get; set; }
    public static CharacterEmbeddedPage Register(HudWindow window,Control body,Button? close)
    {
        var page=new CharacterEmbeddedPage(window,body,close);Pages[window.Id]=page;
        window.AddChild(page);page.Visible=false;
        window.MouseFilter=Control.MouseFilterEnum.Ignore;
        window.VisibilityChanged+=()=>Callable.From(()=>Sync(window.Id)).CallDeferred();
        if(window.Visible) Callable.From(()=>Present(window.Id)).CallDeferred();
        return page;
    }
    public static void Present(string id)
    {
        if(Owner==null || !GodotObject.IsInstanceValid(Owner) || !Pages.TryGetValue(id,out var page) || !GodotObject.IsInstanceValid(page)) return;
        if(!page.NativeWindow.Visible) return;
        Owner.ShowEmbedded(page);
    }
    private static void Sync(string id)
    {
        if(!Pages.TryGetValue(id,out var page) || !GodotObject.IsInstanceValid(page)) return;
        if(page.NativeWindow.Visible) Present(id);
        else if(Owner!=null && GodotObject.IsInstanceValid(Owner)) Owner.DismissEmbedded(page);
    }
    public static void ResumeVisible()
    {
        foreach(var id in Pages.Keys.ToArray())
            if(Pages.TryGetValue(id,out var page) && GodotObject.IsInstanceValid(page) && GodotObject.IsInstanceValid(page.NativeWindow)) Present(id);
    }
    public static void Release(CharacterWindow owner)
    {
        foreach(var page in Pages.Values.Where(GodotObject.IsInstanceValid))
        {
            if(page.GetParent()!=owner || !GodotObject.IsInstanceValid(page.NativeWindow)) continue;
            page.Visible=false;if(page.NativeWindow.Visible) page.CloseNative();page.Reparent(page.NativeWindow);
        }
    }
}
