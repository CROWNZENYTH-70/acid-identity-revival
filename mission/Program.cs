using AcierProtocol;
using ProtoBuf.Meta;

// usage: mission <out.bin>
// authors Missions/tutorial_0001.bin (free-roam tutorial v1) using the game's own
// protobuf contracts + serializer. No format RE needed.
string outFile = args.Length > 0 ? args[0] : "tutorial_0001.bin";
string sceneName = args.Length > 1 ? args[1] : "Firenze_santacroce_sunny";
string regionName = args.Length > 2 ? args[2] : "Firenze_SantaCroce";
string ageName = args.Length > 3 ? args[3] : "Italy";
string moodName = args.Length > 4 ? args[4] : "Sunny";
bool noGuards = args.Contains("--no-guards");
string baseName = Path.GetFileNameWithoutExtension(outFile);
string missionUrl = "Missions/" + Path.GetFileName(outFile);
// Main mission keeps locked spawn; test missions probe at origin with navmesh snap.
bool isMain = baseName == "tutorial_0001" && sceneName == "Firenze_santacroce_sunny";
float px = isMain ? -50f : 0f;
float py = isMain ? 5f : 40f;
float pz = isMain ? -50f : 0f;

var root = new MissionRootContent
{
    MissionType = EMissionType.Mission_EMissionType,
    Scene = sceneName,
    Name = "Tutorial",
    BaseName = baseName,
    GUID = Guid.NewGuid().ToString(),
    Region = Enum.Parse<ERegion>(regionName),
    Age = Enum.Parse<EEra>(ageName),
    Mood = Enum.Parse<EMood>(moodName),
    ObjectiveType = EObjectiveType.Find_EObjectiveType,
    MissionKind = EMissionKind.Kill_EMissionKind,
    DifficultyLevel = 1,
    Rank = 1,
    CrowdDensity = 40,
    MaxCrowdEntities = 20,
    BriefingText = string.Empty,
    DebriefingText = string.Empty,
    MissionUrl = missionUrl,
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

var children = new List<SerializedNode>
{
    new SerializedNode
    {
        Name = "PlayerSpawn",
        PositionX = px,
        PositionY = py,
        PositionZ = pz,
        Components = new[]
        {
            new SerializedNodeComponent
            {
                Type = SerializedNodeComponent.NodeType.NodeType_MissionPlayerSpawnContent,
                MissionPlayerSpawnContent = spawn,
            },
        },
    },
};
if (!noGuards)
{
    if (isMain)
    {
        children.Add(Npc("GuardA", 2, "520620a5-024d-4633-aaf8-21a5f0db7903", -40, -50, 5f));
        children.Add(Npc("GuardB", 3, "2d2ca21b-99e9-4a71-ad13-68dd5748574c", -60, -50, 5f));
        children.Add(Npc("GuardC", 4, "d91147f6-5386-4ba7-b5b2-0334051fac5b", -50, -40, 5f));
    }
    else
    {
        children.Add(Npc("GuardA", 2, "520620a5-024d-4633-aaf8-21a5f0db7903", 10, 0, 5f));
        children.Add(Npc("GuardB", 3, "2d2ca21b-99e9-4a71-ad13-68dd5748574c", -10, 0, 5f));
        children.Add(Npc("GuardC", 4, "d91147f6-5386-4ba7-b5b2-0334051fac5b", 0, 10, 5f));
    }
}

var file = new MissionFile
{
    RootNode = new SerializedNode
    {
        Name = baseName,
        Components = new[]
        {
            new SerializedNodeComponent
            {
                Type = SerializedNodeComponent.NodeType.NodeType_MissionRootContent,
                MissionRootContent = root,
            },
        },
        Children = children.ToArray(),
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
