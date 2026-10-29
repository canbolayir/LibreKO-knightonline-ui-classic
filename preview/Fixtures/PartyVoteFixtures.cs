using System.Reflection;
using Godot;
using LibreKO;
using LibreKO.Network;
using LibreKO.Plugins;

/// <summary>
/// Party, user vote and user card fixtures (3277df5). The client attaches its user card and user vote bridges
/// to its global plugin game API; the preview runs the plugin on its own <see cref="PluginGame"/>, so these
/// entry points link the two the way the client does when it hands the plugin its game API.
/// </summary>
public static partial class PreviewFixtures
{
    private static bool _linked;

    /// <summary>Points the plugin's user card and user vote API at the client's, and forwards their changes.</summary>
    public static void LinkClientFacades(PluginGame game)
    {
        if (_linked) return;
        _linked = true;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var host = PluginHost.Game;
        typeof(PluginGame).GetMethod("AttachUserInfo", flags)!.Invoke(game, new object[] { host.UserInfo });
        typeof(PluginGame).GetMethod("AttachUserVote", flags)!.Invoke(game, new object[] { host.UserVote });
        var raiseInfo = typeof(PluginGame).GetMethod("RaiseUserInfo", flags)!;
        var raiseVote = typeof(PluginGame).GetMethod("RaiseUserVote", flags)!;
        host.UserInfo.Changed += () => raiseInfo.Invoke(game, null);
        host.UserVote.Changed += () => raiseVote.Invoke(game, null);
    }

    /// <summary>
    /// Member pings, level progress and buffs (ff78eef), seeded like upstream's party HUD preview
    /// (<c>World.BuildPartyHudUiPreview</c>): buffs are timed buff skills with art, one per buff type and icon, from the
    /// installed skill table, which the installed content pack carries (the audit pack has no skills).
    /// </summary>
    public static IReadOnlyList<int> SeedPartyMembers(Net net, params (int Id, int PingMs, int LevelPermille, int FirstBuff, int Buffs)[] members)
    {
        if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false))
            throw new Exception("Missing installed content pack (skills)");
        LibreKO.Domain.SkillData.EnsureLoaded();
        // One skill per buff type and per icon, so the cells show different buffs.
        var buffs = new List<int>();
        var seen = new HashSet<string>();
        foreach (var skill in LibreKO.Domain.SkillData.All.Where(s => s.Duration > 0 && s.Type1 == LibreKO.Domain.MagicType.Buff)
            .OrderBy(s => s.Id).GroupBy(s => s.BuffType).Select(g => g.First()))
        {
            if (LibreKO.SkillIcons.Get(skill.Id)?.GetImage() is not { } art) continue;
            if (!seen.Add(Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(art.GetData())))) continue;
            buffs.Add(skill.Id);
            if (buffs.Count == 10) break;
        }
        foreach (var m in members)
            Game.Session.Party.SeedMemberPreview(m.Id, m.PingMs, m.LevelPermille, buffs.Skip(m.FirstBuff).Take(m.Buffs).ToArray());
        return buffs;
    }

    /// <summary>Moves a world canvas layer into the preview scene so the windows the client adds to it render.</summary>
    public static T Adopt<T>(WorldRoot world, string layer, Node scene) where T : CanvasLayer
    {
        var node = Field<T>(world, layer);
        node.Reparent(scene);
        return node;
    }
}
