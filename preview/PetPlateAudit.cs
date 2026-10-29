using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    /// <summary>The familiar plate: Classic party-member composition over the client's plate and its click.</summary>
    private async Task AuditPetPlate(WorldRoot world, Net net, HudWindow petWindow, int nation, string output, Action<bool, string> require)
    {
        DetailCall(world, "Pet.Close");
        var layer = (CanvasLayer)DetailCall(world, "BuildPetPlateClassicUiPreview")!; AddChild(layer);
        async Task Frames(int count = 4) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        await Frames();
        var plate = layer.GetChildren().OfType<PanelContainer>().Single();
        var view = plate.GetChildren().OfType<ClassicPetPlate>().Single();
        var screens = new List<object>();
        plate.Position = new Vector2(24, 176);
        async Task Capture(string state)
        {
            await Frames();
            require(plate.Visible && plate.Size == ClassicPetPlateLayout.Size && GetViewportRect().Encloses(plate.GetGlobalRect()), "Familiar plate keeps the party slot size / " + state + " / " + plate.GetGlobalRect());
            require(plate.GetChildren().OfType<Control>().Where(c => c != view).All(c => !c.Visible), "Familiar plate hides the client's own drawing / " + state);
            foreach (var label in view.GetChildren().OfType<Label>())
            {
                var rect = label.GetMeta("pet_plate_expected_rect").AsRect2();
                require(label.Position == rect.Position && label.Size == rect.Size && plate.GetGlobalRect().Encloses(label.GetGlobalRect()), "Familiar plate label geometry / " + state + " / " + label.Name + " " + label.GetRect() + " " + label.GetGlobalRect() + " in " + plate.GetGlobalRect());
                require(label.GetThemeFont("font") == KnightOnlineUiClassic.Plugin.Kit.Bold && label.GetThemeFontSize("font_size") == 10, "Familiar plate uses the party caption type / " + label.Name);
            }
            var sheet = Game.Session.Pet.Pet!;
            require(view.GetNode<Label>("pet_plate_name").Text == sheet.Name && view.GetNode<Label>("pet_plate_level").Text == "Lv " + sheet.Level, "Familiar plate shows the summoned familiar's name and level / " + state);
            var image = GetViewport().GetTexture().GetImage();
            Color Sample(Rect2 rect, float fraction) => image.GetPixelv((Vector2I)(plate.GlobalPosition + rect.Position + new Vector2(Mathf.Max(1, rect.Size.X * fraction - 3), rect.Size.Y / 2)).Floor());
            require(Sample(ClassicPetPlateLayout.Hp, sheet.Hp / (float)sheet.MaxHp).R > .45f, "Familiar health fill reaches its value / " + state);
            string file = (nation == 1 ? "karus" : "human") + "-plate-" + state + ".png";
            image.SavePng(output + "/" + file); screens.Add(new { state, file });
        }
        var pet = Game.Session.Pet.Pet!;
        int hp = pet.Hp, mp = pet.Mp, satisfaction = pet.Satisfaction, exp = pet.ExpPercent; string name = pet.Name;
        try
        {
            pet.Hp = pet.MaxHp; pet.Mp = pet.MaxMp; pet.Satisfaction = PetSheet.MaxSatisfaction; pet.ExpPercent = 7500;
            DetailCall(world, "Pet.RefreshUI"); await Capture("full");
            pet.Hp = Math.Max(1, pet.MaxHp / 4); pet.Mp = pet.MaxMp / 2; pet.Satisfaction = 1800; pet.ExpPercent = 300;
            DetailCall(world, "Pet.RefreshUI"); await Capture("low");
            require(plate.TooltipText.Contains($"{1800 / 100f:0.00}% satisfaction") && plate.TooltipText.Contains($"{300 / 100f:0.00}% EXP"), "The client's satisfaction and experience tooltip stays on the plate");
            pet.Name = "FamiliarName123"; DetailCall(world, "Pet.RefreshUI"); await Capture("long-name");
            var label = view.GetNode<Label>("pet_plate_name");
            require(label.TextOverrunBehavior == TextServer.OverrunBehavior.TrimEllipsis && label.TooltipText == pet.Name, "A long familiar name ends with an ellipsis inside the slot");
            var at = plate.GetGlobalRect().GetCenter();
            Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at }); await Frames(2);
            foreach (bool pressed in new[] { true, false }) Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = pressed });
            await Frames();
            require((bool)Native.Get<object>(world, "Pet.shown")! && !plate.Visible, "Clicking the plate opens the Familiar window, which replaces the plate");
            DetailCall(world, "Pet.Close"); await Frames();
            require(plate.Visible, "Closing the Familiar window brings the plate back");
            Native.Set(world, "Avatar.Dead", true); DetailCall(world, "Pet.RefreshUI"); await Frames();
            require(!plate.Visible, "A dead owner shows no familiar plate");
            Native.Set(world, "Avatar.Dead", false);
        }
        finally
        {
            pet.Hp = hp; pet.Mp = mp; pet.Satisfaction = satisfaction; pet.ExpPercent = exp; pet.Name = name;
            petWindow.Visible = false; DetailCall(world, "Pet.RefreshUI");
            layer.Free();
        }
        File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-pet-plate.json", JsonSerializer.Serialize(new { screens }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
