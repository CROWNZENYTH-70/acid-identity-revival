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
// test_patrol reuses the locked spawn so the player can watch the guard beats.
bool isMain = baseName == "tutorial_0001" && sceneName == "Firenze_santacroce_sunny";
bool isPatrol = baseName == "test_patrol" && sceneName == "Firenze_santacroce_sunny";
float px = (isMain || isPatrol) ? -50f : 0f;
float py = (isMain || isPatrol) ? 5f : 40f;
float pz = (isMain || isPatrol) ? -50f : 0f;

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
    else if (isPatrol)
    {
        // Patrol test: each guard node carries formation FIRST (patrol entity
        // must exist before the spawn pre-creates the NPC: MissionPhase runs
        // OnPreActivate in component order) + spawn, with waypoint children
        // forming the beat. NpcStatsHelper reads the formation off the
        // spawn's own GameObject -> Npc_PatrolId -> patrol member; the patrol
        // warps members onto the route at OnActivate and walks it.
        // v2: varied route shapes - A walks a square loop (Circeling), B a
        // 3-point beat with one fast leg, C a short brisk beat with one long
        // standing-watch halt.
        children.Add(PatrolNpc("GuardA", 2, 12, "520620a5-024d-4633-aaf8-21a5f0db7903",
            -47, -49, 5f, EFormationIterationType.Circeling, new (float, float, int, float, EMovementSpeed)[] {
                (-52f, -54f, 22, 0.8f, EMovementSpeed.Walk),
                (-42f, -54f, 23, 0.8f, EMovementSpeed.Walk),
                (-42f, -44f, 24, 0.8f, EMovementSpeed.Walk),
                (-52f, -44f, 25, 0.8f, EMovementSpeed.Walk),
            }));
        children.Add(PatrolNpc("GuardB", 3, 13, "2d2ca21b-99e9-4a71-ad13-68dd5748574c",
            -60, -50, 5f, EFormationIterationType.BackAndForth, new (float, float, int, float, EMovementSpeed)[] {
                (-68f, -50f, 26, 0.5f, EMovementSpeed.WalkFast),
                (-60f, -50f, 27, 1.0f, EMovementSpeed.Walk),
                (-52f, -50f, 28, 0.5f, EMovementSpeed.Walk),
            }));
        children.Add(PatrolNpc("GuardC", 4, 14, "d91147f6-5386-4ba7-b5b2-0334051fac5b",
            -50, -40, 5f, EFormationIterationType.BackAndForth, new (float, float, int, float, EMovementSpeed)[] {
                (-50f, -46f, 29, 0.3f, EMovementSpeed.Walk),
                (-50f, -34f, 30, 3.0f, EMovementSpeed.Walk),
            }));
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
// Patrol guard: formation component FIRST (see call-site note), then spawn,
// then one waypoint child per beat point. All IDs unique mission-wide.
static SerializedNode PatrolNpc(string name, int spawnId, int formationId, string typeGuid,
    float x, float z, float y, EFormationIterationType iter,
    (float wx, float wz, int wid, float idle, EMovementSpeed speed)[] beat)
{
    var node = new SerializedNode
    {
        Name = name,
        PositionX = x,
        PositionY = y,
        PositionZ = z,
        Components = new[]
        {
            new SerializedNodeComponent
            {
                Type = SerializedNodeComponent.NodeType.NodeType_MissionNpcFormationContent,
                MissionNpcFormationContent = new MissionNpcFormationContent
                {
                    MissionFormationBaseContent = new MissionFormationBaseContent
                    {
                        MissionBaseContent = new MissionBaseContent { ID = formationId, ActivateAtId = -2, RemoveAtId = -2 },
                        Type = EPatrolType.Military,
                        IterationType = iter,
                        IterationDirection = EFormationIterationDirection.Up,
                        FixedLeaderSlot = false,
                        Radius = 0f,
                    },
                },
            },
            new SerializedNodeComponent
            {
                Type = SerializedNodeComponent.NodeType.NodeType_MissionNpcSpawnContent,
                MissionNpcSpawnContent = new MissionNpcSpawnContent
                {
                    MissionSpawnEntityContent = new MissionSpawnEntityContent
                    {
                        MissionBaseContent = new MissionBaseContent { ID = spawnId, ActivateAtId = -2, RemoveAtId = -2 },
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
    var wps = new List<SerializedNode>();
    foreach (var (wx, wz, wid, idle, speed) in beat)
    {
        wps.Add(new SerializedNode
        {
            Name = $"{name}_WP{wid}",
            PositionX = wx,
            PositionY = y,
            PositionZ = wz,
            Components = new[]
            {
                new SerializedNodeComponent
                {
                    Type = SerializedNodeComponent.NodeType.NodeType_MissionWaypointContent,
                    MissionWaypointContent = new MissionWaypointContent
                    {
                        MissionBaseContent = new MissionBaseContent { ID = wid, ActivateAtId = -2, RemoveAtId = -2 },
                        IdleCategory = ENpcIdleCategory.Neutral,
                        IdleTime = idle,
                        RotationType = ERotationAtWaypoint.None,
                        MinSpeed = speed,
                        MaxSpeed = speed,
                        WarpTarget = false,
                        ActivationIterationType = iter,
                        ActivationIterationStartType = ERouteIterationStart.Up_ERouteIterationStart,
                    },
                },
            },
        });
    }
    node.Children = wps.ToArray();
    return node;
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
