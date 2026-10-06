using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic.Windows;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    private async Task CapturePresetStates(World model,CharacterEmbeddedPage page,string output,string prefix,int nation)
    {
        var plans=(PresetPlan[])DetailField(model,"_presetPlans")!;
        var labels=(Label[])DetailField(model,"_presetStatLbls")!;
        var minus=(Button[])DetailField(model,"_presetStatMinusBtns")!;
        var plus=(Button[])DetailField(model,"_presetStatPlusBtns")!;
        var records=new List<object>();
        async Task Capture(string suffix,int cls,int[] allocation,int pool=302)
        {
            typeof(World).GetField("_selfClass",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(model,cls);
            typeof(World).GetField("_presetSlot",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(model,0);
            plans[0].SetStats(allocation);
            var basis=StarterStats.BaseForClass(cls);
            Net.I.Sheet.SeedStats(basis.Str,basis.Sta,basis.Dex,basis.Int,basis.Mag,pool);
            DetailCall(model,"RefreshPresetUI");
            for(int frame=0;frame<4;frame++) await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            if(!plans[0].TryStatValues(cls,pool,out var totals,out int remaining)) throw new Exception("Invalid preset fixture");
            for(int i=0;i<5;i++)
            {
                if(labels[i].Text!=totals[i].ToString()) throw new Exception("Preset displays allocations rather than totals");
                if(minus[i].Disabled!=(allocation[i]==0) || plus[i].Disabled!=(allocation[i]>=plans[0].StatBudget(cls,i,pool))) throw new Exception("Preset stat limit state disagrees with the native plan");
                if(!page.GetGlobalRect().Encloses(labels[i].GetGlobalRect()) || !page.GetGlobalRect().Encloses(plus[i].GetGlobalRect())) throw new Exception("Preset controls escape their page");
                if(allocation[i]==0)
                {
                    minus[i].EmitSignal(BaseButton.SignalName.Pressed);
                    if(plans[0].Stats[i]!=0) throw new Exception("Preset decremented below its class base");
                }
            }
            using var image=GetViewport().GetTexture().GetImage();image.SavePng(System.IO.Path.Combine(output,prefix+"-presets-"+suffix+".png"));
            records.Add(new {suffix,cls,baseStats=Enumerable.Range(0,5).Select(basis.StatAtRow).ToArray(),allocation,totals,remaining,
                minusDisabled=minus.Select(b=>b.Disabled).ToArray(),plusDisabled=plus.Select(b=>b.Disabled).ToArray()});
        }
        int classPrefix=nation==1?100:200;
        foreach(var fixture in new[]{("base-warrior",6),("base-rogue",8),("base-mage",10),("base-priest",12),("base-kurian",15)})
            await Capture(fixture.Item1,classPrefix+fixture.Item2,[0,0,0,0,0]);
        await Capture("cap-priest",classPrefix+12,[205,0,0,0,0]);
        await Capture("pool-empty",classPrefix+6,[190,112,0,0,0]);
        await Capture("saved-plan",classPrefix+6,[185,55,12,0,0]);
        DetailCall(model,"SelectPresetSlot",1);
        for(int frame=0;frame<4;frame++) await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        var warrior=StarterStats.BaseForClass(classPrefix+6);
        if((int)DetailField(model,"_presetSlot")! !=1 || labels.Where((l,i)=>l.Text!=warrior.StatAtRow(i).ToString()).Any()) throw new Exception("Switching to an empty preset slot lost its class base");
        if(!plans[0].Stats.SequenceEqual(new[]{185,55,12,0,0})) throw new Exception("Switching slots changed the saved allocation");
        using(var image=GetViewport().GetTexture().GetImage()) image.SavePng(System.IO.Path.Combine(output,prefix+"-presets-slot-2.png"));
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,prefix+"-preset-verification.json"),JsonSerializer.Serialize(new {
            records,slotSwitchVerified=true,baseMinimumVerified=true,cap255Verified=true,allocatedBudgetVerified=true,savedOffsetsPreserved=true,
            pluginHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug/KnightOnlineUiClassic.dll")))),
            clientHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug/LibreKO.dll"))))
        },new JsonSerializerOptions {WriteIndented=true}));
        GD.Print("PRESET_BASE_TOTALS_OK: five class families, both nations, base minimum, 255 cap, exhausted pool, saved offsets and native slot switching");
    }
}
