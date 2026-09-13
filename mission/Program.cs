using AcierProtocol;
using ProtoBuf.Meta;

// usage: mission <out.bin>
// authors Missions/tutorial_0001.bin (free-roam tutorial v1) using the game's own
// protobuf contracts + serializer. No format RE needed.
string outFile = args.Length > 0 ? args[0] : "tutorial_0001.bin";

var root = new MissionRootContent
{
    MissionType = EMissionType.Mission_EMissionType,
    Scene = "Firenze_santacroce_sunny",
    Name = "Tutorial",
    BaseName = "tutorial_0001",
    GUID = Guid.NewGuid().ToString(),
    Region = ERegion.Firenze_SantaCroce,
    Age = EEra.Italy,
    Mood = EMood.Sunny,
    ObjectiveType = EObjectiveType.Find_EObjectiveType,
    MissionKind = EMissionKind.Kill_EMissionKind,
    DifficultyLevel = 1,
    Rank = 1,
    CrowdDensity = 0,
    MaxCrowdEntities = 0,
    BriefingText = string.Empty,
    DebriefingText = string.Empty,
    MissionUrl = "Missions/tutorial_0001.bin",
    GenerationTime = DateTime.UtcNow.ToString("o"),
    PlayerAssassinGUID = string.Empty,
    PlayerHirelingGUID = string.Empty,
    Hidden = false,
    MissionBaseContent = new MissionBaseContent { ID = 0, ActivateAtId = -2, RemoveAtId = -2 },
};

var spawn = new MissionPlayerSpawnContent
{
    MissionBaseContent = new MissionBaseContent { ID = 1, ActivateAtId = -2, RemoveAtId = -2 },
};

var file = new MissionFile
{
    RootNode = new SerializedNode
    {
        Name = "tutorial_0001",
        Components = new[]
        {
            new SerializedNodeComponent
            {
                Type = SerializedNodeComponent.NodeType.NodeType_MissionRootContent,
                MissionRootContent = root,
            },
        },
        Children = new[]
        {
            new SerializedNode
            {
                Name = "PlayerSpawn",
                PositionX = -50f,
                PositionY = 5f,
                PositionZ = -50f,
                Components = new[]
                {
                    new SerializedNodeComponent
                    {
                        Type = SerializedNodeComponent.NodeType.NodeType_MissionPlayerSpawnContent,
                        MissionPlayerSpawnContent = spawn,
                    },
                },
            },
            // civilian off for isolation test
            //Npc("GuardA", 2, "2d2ca21b-99e9-4a71-ad13-68dd5748574c", 10, 0),
            Npc("GuardA", 2, "520620a5-024d-4633-aaf8-21a5f0db7903", -40, -50, 5f),
            Npc("GuardB", 3, "2d2ca21b-99e9-4a71-ad13-68dd5748574c", -60, -50, 5f),
            Npc("GuardC", 4, "15c667c9-4ec9-4044-84c7-965cf31ba7d7", -50, -40, 5f),
        },
    },
};

static SerializedNode NpcDormant(string name, int id, string typeGuid, float x, float z = 0f)
{
    // ActivateAtId=9999: created+deserialized, never activated
    return NpcBase(name, id, typeGuid, x, z, 9999);
}
static SerializedNode Npc(string name, int id, string typeGuid, float x, float z = 0f, float y = 40f)
{
    return NpcBase(name, id, typeGuid, x, z, -2, y);
}
static SerializedNode NpcBase(string name, int id, string typeGuid, float x, float z, long activate, float y = 40f)
{
    return new SerializedNode
    {
        Name = name,
        PositionX = x,
        PositionY = y,
        PositionZ = z,
        Components = new[]
        {
            new SerializedNodeComponent
            {
                Type = SerializedNodeComponent.NodeType.NodeType_MissionNpcSpawnContent,
                MissionNpcSpawnContent = new MissionNpcSpawnContent
                {
                    MissionSpawnEntityContent = new MissionSpawnEntityContent
                    {
                        MissionBaseContent = new MissionBaseContent { ID = id, ActivateAtId = activate, RemoveAtId = -2 },
                        IsMimic = false,
                        FadeMode = EFadeMode.Simple_EFadeMode,
                        FadeDuration = 0f,
                    },
                    TypeGuid = typeGuid,
                    ScalingMultiplier = 1f,
                },
            },
        },
    };
}

var model = new AcierProtocolSerializer();
using (var fs = File.Create(outFile))
{
    ((TypeModel)model).Serialize(fs, file);
}
Console.WriteLine($"WROTE {outFile} ({new FileInfo(outFile).Length} bytes)");

// round-trip verify
using (var fs = File.OpenRead(outFile))
{
    fs.Position = 0;
    var back = (MissionFile)((TypeModel)new AcierProtocolSerializer()).Deserialize(fs, null, typeof(MissionFile));
    Console.WriteLine($"VERIFY scene={back.RootNode.Components[0].MissionRootContent.Scene} children={back.RootNode.Children.Length}");
}
