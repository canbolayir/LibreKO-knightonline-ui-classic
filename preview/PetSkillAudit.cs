using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

public partial class Preview
{
    private async Task AuditPetSkills(WorldRoot world, Net net, Control panel, Action<bool, string> require, Func<string, Task> capture)
    {
        async Task Frames(int count = 6) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        T Field<T>(string name) => (T)Native.Get<object>(world, name)!;
        void SetField(string name, object value) => Native.Set(world, name, value);
        T Find<T>(string name) where T : Control => Descendants(panel).OfType<T>().Single(c => c.Name == name);
        async Task Click(Control control)
        {
            var at = control.GetGlobalRect().GetCenter(); Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at }); await Frames(2);
            foreach (bool pressed in new[] { true, false }) Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = pressed });
            await Frames();
        }
        double Now() => Time.GetTicksMsec() / 1000d;
        var sheet = Game.Session.Pet.Pet!; int oldLevel = sheet.Level, oldMp = sheet.Mp, oldMaxMp = sheet.MaxMp;
        var cooldowns = Field<Dictionary<int, double>>("PetSkillBar.PetSkillReadyAt");
        var connection = (GameConn)typeof(Net).GetField("conn", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(net)!;
        var skillIds = PetSkills.BarSkills(SkillData.All.Select(s => (s.Id, s.Tree)), sheet.Class);
        int unlockedLevel = Math.Max(oldLevel, skillIds.Max(id => SkillData.Get(id)!.Level));
        int availableMana = Math.Max(oldMaxMp, skillIds.Max(id => SkillData.Get(id)!.Msp));
        require(skillIds.Count > 8, "Real familiar data supplies skills on the second page");
        sheet.Level = unlockedLevel; sheet.Mp = sheet.MaxMp = availableMana; DetailCall(world, "Pet.RefreshUI");
        foreach (var button in Descendants(panel).OfType<FamiliarSkillButton>())
            require(button.SkillId == skillIds[int.Parse(button.Name.ToString().Replace("pet_skill_", ""))], "First familiar skill page matches actual table order " + button.Name);
        await Click(Find<Button>("pet_skill_next"));
        require(Find<Label>("pet_skill_page").Text == "2/2" && Find<Button>("pet_skill_next").Disabled && !Find<Button>("pet_skill_previous").Disabled,
            "Actual next click reaches bounded second skill page");
        for (int i = 0; i < 8; i++)
        {
            var button = Find<FamiliarSkillButton>("pet_skill_" + i);
            int expected = i + 8 < skillIds.Count ? skillIds[i + 8] : 0;
            require(button.SkillId == expected && (expected != 0 || button.Disabled), "Second familiar page retains skill or empty-slot identity " + i);
        }
        await capture("skills-second-page");
        sheet.Level = 1; DetailCall(world, "Pet.RefreshUI");
        require(Descendants(panel).OfType<FamiliarSkillButton>().All(button => button.SkillId == 0 || button.Disabled == !PetSkills.Unlocked(SkillData.Get(button.SkillId)!.Level, sheet.Level)),
            "Second-page level locks use the actual native requirements");
        await capture("skills-second-page-locked");
        sheet.Level = unlockedLevel; DetailCall(world, "Pet.RefreshUI"); await Click(Find<Button>("pet_skill_previous"));
        require(Find<Label>("pet_skill_page").Text == "1/2" && Find<Button>("pet_skill_previous").Disabled, "Actual previous click restores familiar page one");

        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        connection.Connect("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port);
        using var timeout = new CancellationTokenSource(30000);
        using var receiver = await listener.AcceptTcpClientAsync(timeout.Token);
        for (int i = 0; i < 120 && !connection.Connected; i++) await Frames(1);
        require(net.Connected, "Native familiar send path connects to isolated loopback capture");
        using var stream = receiver.GetStream();
        async Task ReadSkillPacket(int id, int stage)
        {
            var header = new byte[4]; await stream.ReadExactlyAsync(header, timeout.Token);
            require(header[0] == 0xaa && header[1] == 0x55, "Native familiar request retains original framing");
            int length = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2)); var body = new byte[length + 2];
            await stream.ReadExactlyAsync(body, timeout.Token);
            require(length == 39 && body[length] == 0x55 && body[length + 1] == 0xaa, "Native familiar request retains complete original payload length");
            require(body[0] == (byte)GameOpcodes.GS_PET && body[1] == 2 && body[2] == stage
                && BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(3)) == id && BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(7)) == 1,
                "Actual familiar callback sends expected skill, stage and caster");
            var skill = SkillData.Get(id)!;
            require(BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(11)) == (skill.Moral == PetSkills.OwnerMoral ? 42 : 2), "Familiar request preserves native owner or hostile target selection");
        }
        async Task SelectPageFor(int id)
        {
            int page = skillIds.IndexOf(id) / 8;
            if (Find<Label>("pet_skill_page").Text != (page + 1) + "/2") await Click(Find<Button>(page == 0 ? "pet_skill_previous" : "pet_skill_next"));
        }
        FamiliarSkillButton SkillButton(int id) => Descendants(panel).OfType<FamiliarSkillButton>().Single(b => b.SkillId == id);
        var instant = skillIds.Select(SkillData.Get).OfType<SkillData.Skill>().First(s => s.CastSeconds == 0 && s.RecastSeconds > 0);
        await SelectPageFor(instant.Id); await Click(SkillButton(instant.Id)); await ReadSkillPacket(instant.Id, PetSkills.StageEffecting);
        var active = SkillButton(instant.Id);
        require(cooldowns.ContainsKey(instant.Id) && active.Disabled && active.CooldownFraction > 0, "Actual familiar skill click starts native shared cooldown and disables its button");
        var shade = active.GetNode<ColorRect>("pet_skill_cooldown");
        require(shade.Visible && shade.GetGlobalRect().Position == active.GetGlobalRect().Position + new Vector2(2, 2)
            && shade.Size == active.Size - new Vector2(4, 4), "Actual familiar cooldown shading stays inside the skill frame with shared 2 px insets");
        await capture("skill-cooldown");
        double firstReady = cooldowns[instant.Id]; await Click(active);
        require(!stream.DataAvailable && cooldowns[instant.Id] == firstReady, "Cooldown click cannot submit a duplicate familiar skill");
        await Click(Find<Button>("pet_skill_next")); await Click(Find<Button>("pet_skill_previous"));
        await SelectPageFor(instant.Id);
        require(SkillButton(instant.Id).CooldownFraction > 0, "Changing familiar skill pages preserves the shared cooldown");
        cooldowns[instant.Id] = Now() - 1; DetailCall(world, "PetBarTick", Now());
        require(!SkillButton(instant.Id).Disabled && SkillButton(instant.Id).CooldownFraction == 0, "Native tick clears the expired cooldown without requiring a vitals reply");
        await capture("skill-cooldown-expired");
        SetField("Avatar.Dead", true); DetailCall(world, "Pet.RefreshUI");
        DetailCall(world, "PetSkillBar.UsePetSkill", instant.Id); await Frames();
        require(!stream.DataAvailable, "Dead owner cannot submit a familiar skill through either entry point");
        SetField("Avatar.Dead", false); sheet.Mp = 0; DetailCall(world, "Pet.RefreshUI");
        var manaSkill = skillIds.Select(SkillData.Get).OfType<SkillData.Skill>().First(s => s.Msp > 0);
        DetailCall(world, "PetSkillBar.UsePetSkill", manaSkill.Id); await Frames();
        require(!stream.DataAvailable && (!cooldowns.ContainsKey(manaSkill.Id) || cooldowns[manaSkill.Id] <= Now()), "Insufficient familiar MP does not start a new skill cooldown");
        sheet.Mp = availableMana; SetField("Selection.SelectedId", -1);
        var hostile = skillIds.Select(SkillData.Get).OfType<SkillData.Skill>().First(s => s.Moral != PetSkills.OwnerMoral);
        cooldowns.Clear(); DetailCall(world, "PetSkillBar.UsePetSkill", hostile.Id); await Frames();
        require(!stream.DataAvailable && cooldowns.Count == 0, "Missing hostile target cannot start a familiar cast");
        SetField("Selection.SelectedId", 2); DetailCall(world, "Pet.RefreshUI");

        var cast = skillIds.Select(SkillData.Get).OfType<SkillData.Skill>().First(s => s.CastSeconds > 0);
        await SelectPageFor(cast.Id); await Click(SkillButton(cast.Id)); await ReadSkillPacket(cast.Id, PetSkills.StageCasting);
        int generation = Field<int>("PetSkillBar.petSkillGeneration");
        require((bool)DetailCall(world, "PetSkillBar.CanFinishPetSkill", sheet.Index, 1, generation)!, "Started cast retains its original familiar identity");
        sheet.Index++;
        require(!(bool)DetailCall(world, "PetSkillBar.CanFinishPetSkill", sheet.Index - 1, 1, generation)!, "Replacement familiar cannot finish the previous cast");
        sheet.Index--; SetField("Avatar.Dead", true);
        require(!(bool)DetailCall(world, "PetSkillBar.CanFinishPetSkill", sheet.Index, 1, generation)!, "Owner death invalidates a pending familiar cast");
        SetField("Avatar.Dead", false);
        require(!(bool)DetailCall(world, "PetSkillBar.CanFinishPetSkill", sheet.Index, 999, generation)!, "Replacement NPC identity invalidates a pending familiar cast");
        DetailCall(world, "PetSkillBar.HidePetBar");
        require(!(bool)DetailCall(world, "PetSkillBar.CanFinishPetSkill", sheet.Index, 1, generation)!, "Dismissal invalidates the original familiar cast generation");
        double until = Now() + cast.CastSeconds + .2;
        while (Now() < until) await Frames(1);
        require(!stream.DataAvailable, "Actual cast timer does not send a delayed effect after dismissal");
        cooldowns.Clear(); DetailCall(world, "Pet.RefreshUI"); await SelectPageFor(cast.Id);
        await Click(SkillButton(cast.Id)); await ReadSkillPacket(cast.Id, PetSkills.StageCasting);
        await ReadSkillPacket(cast.Id, PetSkills.StageEffecting);
        require(cooldowns.ContainsKey(cast.Id), "Unchanged familiar completes its original cast through the native timer");
        cooldowns.Clear(); DetailCall(world, "Pet.RefreshUI");
        await Click(SkillButton(cast.Id)); await ReadSkillPacket(cast.Id, PetSkills.StageCasting);
        var refusal = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        refusal.WriteByte(MagicSub.Fail); refusal.WriteInt(cast.Id); refusal.WriteInt(999); refusal.WriteInt(0);
        for (int i = 0; i < 7; i++) refusal.WriteInt(0);
        PreviewPackets.Receive(refusal);
        require(cooldowns.ContainsKey(cast.Id), "Unrelated NPC refusal cannot clear the familiar cooldown");
        refusal = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        refusal.WriteByte(MagicSub.Fail); refusal.WriteInt(cast.Id); refusal.WriteInt(1); refusal.WriteInt(0);
        for (int i = 0; i < 7; i++) refusal.WriteInt(0);
        PreviewPackets.Receive(refusal); await Frames();
        require(!cooldowns.ContainsKey(cast.Id) && !SkillButton(cast.Id).Disabled && SkillButton(cast.Id).CooldownFraction == 0,
            "Decoded native server refusal cancels the familiar cast and releases its cooldown");
        double retrySince = Now();
        await Click(SkillButton(cast.Id)); await ReadSkillPacket(cast.Id, PetSkills.StageCasting);
        await ReadSkillPacket(cast.Id, PetSkills.StageEffecting); await Frames();
        require(Now() >= retrySince + cast.CastSeconds && !stream.DataAvailable,
            "Retry completes once and the refused cast timer cannot complete or consume the newer cast");
        connection.Close(); cooldowns.Clear(); DetailCall(world, "Pet.RefreshUI");
        DetailCall(world, "PetSkillBar.UsePetSkill", instant.Id);
        require(cooldowns.Count == 0 && !Game.Requests.Pet.Skill(PetSkills.StageEffecting, instant.Id, 1, 2, 0, 0, 0), "Disconnected familiar requests cannot leave an artificial cooldown");
        sheet.Level = oldLevel; sheet.Mp = oldMp; sheet.MaxMp = oldMaxMp; SetField("Avatar.Dead", false);
        if (Find<Label>("pet_skill_page").Text == "2/2") await Click(Find<Button>("pet_skill_previous"));
        DetailCall(world, "Pet.RefreshUI");
    }
}
