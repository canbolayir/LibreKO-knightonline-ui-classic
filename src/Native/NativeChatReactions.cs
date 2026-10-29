using Godot;
using LibreKO;
using KnightOnlineUiClassic.Windows;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// The reaction popups (fa3cfc3: the quick bar, the details card and the reaction emoticon picker) are shared by the
/// chat room and the whispers and live on the World's own reaction layer, above the room and whisper layers, so the
/// room's popup hooks never see them. The layer is watched through a tracked hook (released with the World or at
/// shutdown) and every popup is dressed Classic as the client creates it; the client keeps building, positioning
/// and wiring them.
/// </summary>
public static class NativeChatReactions
{
    private const string LayerMeta = "classic_reaction_layer";

    /// <summary>Watches the World's reaction layer once; safe to call from every chat preparer.</summary>
    public static void Attach(WorldRoot world)
    {
        if (!Native.TryGet<CanvasLayer>(world, "ChatReactions.ReactionLayer", out var layer) || !GodotObject.IsInstanceValid(layer) || layer.HasMeta(LayerMeta)) return;
        layer.SetMeta(LayerMeta, true);
        foreach (var popup in layer.GetChildren().OfType<PopupPanel>()) Style(popup);
        Node.ChildEnteredTreeEventHandler added = node => { if (node is PopupPanel popup) Style(popup); };
        NativeHooks.Listen("World._reactionLayer (reaction popups)", () => layer.ChildEnteredTree += added, () =>
        {
            if (!GodotObject.IsInstanceValid(layer)) return;
            layer.ChildEnteredTree -= added;
            layer.RemoveMeta(LayerMeta);
        }, world);
    }

    private static void Style(PopupPanel popup)
    {
        if (Plugin.Active && GodotObject.IsInstanceValid(popup) && ClassicReactionStyle.Owns(popup)) ClassicReactionStyle.Apply(popup);
    }
}
