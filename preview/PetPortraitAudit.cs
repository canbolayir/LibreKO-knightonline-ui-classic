using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Windows;
using System.Collections;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    private sealed record FamiliarForm(int Id, int Material, int Result, int ModelId, int Size, int Weight);

    private async Task CapturePetPortraitAudit(int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); Directory.CreateDirectory(output);
        foreach (string pack in new[] { "knightonline.pck", "content/npcs.pck" })
            if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/" + pack), false))
                throw new Exception("Missing existing portrait content: " + pack);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(800, 680);
        AddChild(new ColorRect { Size = new Vector2(800, 680), Color = new Color("252822"), MouseFilter = MouseFilterEnum.Ignore });
        var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        PluginHost.Ui.ExtendWindow("pet", ClassicPetSkin.Extend);
        var world = new PetAuditWorld { ProcessMode = ProcessModeEnum.Disabled };
        var layer = (CanvasLayer)DetailCall(world, "BuildPetClassicUiPreview", nation)!; AddChild(world); AddChild(layer);
        for (int i = 0; i < 6; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var window = layer.GetChildren().OfType<HudWindow>().Single(); window.Position = new Vector2(240, 70);
        var panel = window.GetChildren().OfType<ClassicPetPanel>().Single();
        var portrait = Descendants(panel).OfType<ClassicFamiliarPortrait>().Single();
        for (int i = 0; i < 600 && portrait.Texture == null; i++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var checks = new List<string>();
        await AuditPetPortraitForms(world, net, window, panel, nation, output, (condition, message) =>
        {
            if (!condition) throw new Exception("PET_PORTRAIT_AUDIT: " + message); checks.Add(message);
        });
        File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-pet-portrait-checks.json",
            JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        DetailCall(world, "PetSkillObserveDispose"); world.Free(); net.Free();
        GD.Print("PET_PORTRAIT_AUDIT: " + checks.Count + " checks");
    }

    private async Task AuditPetPortraitForms(World world, Net net, HudWindow window, ClassicPetPanel panel,
        int nation, string output, Action<bool, string> require)
    {
        string source = ProjectSettings.GlobalizePath("res://../../LibreKO/Server/LibreKO.Game/Seed/Data/PetTransforms.json");
        var forms = JsonSerializer.Deserialize<FamiliarForm[]>(File.ReadAllText(source))!.OrderBy(f => f.ModelId).ToArray();
        require(forms.Length == 30 && forms.Select(f => f.ModelId).Distinct().Count() == forms.Length,
            "Familiar portrait audit covers every distinct authoritative transform model");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var actors = (IDictionary)typeof(World).GetField("_ents", flags)!.GetValue(world)!;
        var actor = actors[1]!; var modelField = actor.GetType().GetField("ModelId")!;
        int originalModel = (int)modelField.GetValue(actor)!;
        var deadField = typeof(World).GetField("_selfDead", flags)!;
        bool originalDead = (bool)deadField.GetValue(world)!;
        deadField.SetValue(world, false);
        var sheet = net.Pet!;
        var portrait = Descendants(panel).OfType<ClassicFamiliarPortrait>().Single();
        var view = Descendants(panel).OfType<FamiliarPortraitView>().Single();
        var snapshots = new List<object>(); var textures = new Dictionary<int, ulong>();
        async Task Frames(int count = 4)
        {
            for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        void Select(int model)
        {
            modelField.SetValue(actor, model); DetailCall(world, "RefreshPetPortrait");
            require(view.Appearance?.AppearanceKey == "familiar:" + model, "Native familiar appearance selects transform model " + model);
        }
        async Task Ready(int model)
        {
            for (int i = 0; i < 600 && (portrait.Texture == null || NpcPortraitCache.RetainedViewportCount != 0); i++) await Frames(1);
            require(portrait.Texture != null, "Actual transformed familiar model produces a portrait " + model);
            require(NpcPortraitCache.RetainedViewportCount == 0 && NpcPortraitCache.ActiveRenderers == 0 && NpcPortraitCache.SceneNodeCount == 0,
                "Transformed familiar retains only a 2D texture " + model);
            require(portrait.Texture!.GetWidth() == NpcPortraitCache.Resolution && portrait.Texture.GetHeight() == NpcPortraitCache.Resolution,
                "Transformed familiar uses bounded capture resolution " + model);
        }
        async Task Capture(string state, FamiliarForm? form)
        {
            await Frames();
            var status = Descendants(panel).OfType<Label>().Single(c => c.Name == "pet_status");
            require(window.Size == new Vector2(286, status.Visible && status.Text.Length > 0 ? 525 : 457),
                state + " preserves original familiar composition");
            require(GetViewportRect().Encloses(window.GetGlobalRect()), state + " familiar window contained");
            require(view.Size == new Vector2(89, 89) && view.Position == new Vector2(13, 51),
                state + " original circular portrait bounds");
            foreach (var control in Descendants(panel).OfType<Control>().Where(c => c.HasMeta("pet_expected_rect")))
            {
                var expected = control.GetMeta("pet_expected_rect").AsRect2();
                require((control.Position - expected.Position).Length() < .1 && (control.Size - expected.Size).Length() < .1,
                    state + " actual control bounds " + control.Name);
                if (control is Label label && label.IsVisibleInTree() && label.Text.Length > 0)
                    require(label.GetVisibleLineCount() == label.GetLineCount(), state + " complete label " + control.Name);
            }
            string file = (nation == 1 ? "karus" : "human") + "-pet-portrait-" + state + ".png";
            using var image = GetViewport().GetTexture().GetImage(); image.SavePng(output + "/" + file);
            snapshots.Add(new { state, file, form, window = window.GetGlobalRect().ToString(), portrait = view.GetGlobalRect().ToString(),
                renderRequests = NpcPortraitCache.RenderRequests,
                cacheEntries = NpcPortraitCache.EntryCount, textureBytes = NpcPortraitCache.CachedTextureBytes });
        }
        window.Visible = true;
        DetailCall(world, "SetPetStatus", "", false); DetailCall(world, "ShowPetSheet", sheet);
        var ability = Descendants(panel).OfType<Button>().Single(b => b.Text == "Ability");
        ability.EmitSignal(BaseButton.SignalName.Pressed); await Frames();
        int renders = NpcPortraitCache.RenderRequests, entries = NpcPortraitCache.EntryCount;
        foreach (var form in forms)
            require(DetailCall(world, "ResolveMobScene", form.ModelId) is PackedScene,
                "Existing content supplies authoritative familiar transform model " + form.ModelId);
        // Queue two uncached appearances before either renderer completes; only the newest may reach the control.
        Select(forms[0].ModelId); require(portrait.Texture == null, "Transform clears the previous familiar portrait immediately");
        Select(forms[1].ModelId); await Ready(forms[1].ModelId);
        require(view.Appearance?.AppearanceKey == "familiar:" + forms[1].ModelId,
            "Rapid familiar transform keeps the newest appearance identity");
        var newestTexture = portrait.Texture;
        await Frames(8);
        require(portrait.Texture == newestTexture, "Late familiar capture cannot overwrite the newest transform");
        foreach (var form in forms)
        {
            Select(form.ModelId); await Ready(form.ModelId);
            using var texture = portrait.Texture!.GetImage();
            int painted = 0;
            for (int y = 0; y < texture.GetHeight(); y += 4)
                for (int x = 0; x < texture.GetWidth(); x += 4)
                    if (texture.GetPixel(x, y).A > .1f) painted++;
            texture.SavePng(output + "/" + (nation == 1 ? "karus" : "human") + "-pet-model-texture-" + form.ModelId + ".png");
            GD.Print("PET_PORTRAIT: model=" + form.ModelId + " painted=" + painted + " framing=" + NpcPortraitCache.LastFraming
                + " target=" + NpcPortraitCache.LastTarget + " pose=" + NpcPortraitCache.LastPose);
            require(painted > 20, "Transformed familiar portrait contains actual visible model geometry " + form.ModelId);
            textures[form.ModelId] = portrait.Texture.GetInstanceId();
            await Capture("model-" + form.ModelId, form);
        }
        require(NpcPortraitCache.RenderRequests == renders + forms.Length && NpcPortraitCache.EntryCount == entries + forms.Length,
            "Every supported familiar transform is captured exactly once");
        require(NpcPortraitCache.EntryCount <= NpcPortraitCache.Capacity && NpcPortraitCache.CachedTextureBytes <= (long)NpcPortraitCache.Capacity * 256 * 256 * 4,
            "All familiar transforms fit the bounded shared portrait cache");
        foreach (var form in Enumerable.Reverse(forms))
        {
            Select(form.ModelId); await Ready(form.ModelId);
            require(portrait.Texture!.GetInstanceId() == textures[form.ModelId],
                "Revisiting transformed familiar reuses its original 2D texture " + form.ModelId);
        }
        require(NpcPortraitCache.RenderRequests == renders + forms.Length,
            "Revisiting all familiar transforms starts no additional renderer");
        DetailCall(world, "ShowPetSheet", (object?)null);
        require(portrait.Texture == null && view.Appearance == null, "Dismissed transformed familiar clears its portrait immediately");
        await Capture("dismissed", null);
        modelField.SetValue(actor, originalModel);
        DetailCall(world, "SetPetStatus", "", false); DetailCall(world, "ShowPetSheet", sheet); await Ready(originalModel);
        require(NpcPortraitCache.RenderRequests == renders + forms.Length,
            "Returning to default Kaul reuses its existing portrait after all transformations");
        await Capture("default-restored", null);
        deadField.SetValue(world, originalDead); DetailCall(world, "RefreshPetUI");
        window.Visible = false;
        File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-pet-portraits.json",
            JsonSerializer.Serialize(new { source, screens = snapshots }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
