using Mono.Cecil;
using Mono.Cecil.Cil;

try
{
    PatchMain(args);
    Console.WriteLine("PATCHER DONE");
}
catch (Exception ex)
{
    Console.WriteLine("PATCHER FAIL: " + ex);
    Environment.Exit(2);
}
return;

void PatchMain(string[] args)
{
    // usage: patch <ManagedDir> <OutDll>
    string managedDir = args[0];
    string outDll = args[1];
    string mainDll = Path.Combine(managedDir, "Assembly-CSharp.dll");
    string acierDll = Path.Combine(managedDir, "AcierProtocol.dll");

    const string TracePath = "/storage/emulated/0/ACID_Revival/boot.log";
    const string LocalBase = "file:///storage/emulated/0/ACID_Revival/AcierData";
    const string BerHumanoid = "2183e8fb-3d3e-4f20-bd68-1ed2657dba9c";
    const string BerWeapon = "7d63cb2a-eecd-4e8c-bf8d-cdfe658d3faf";
    const string BerHead = "77d5f907-1c42-41bd-a94e-283d1a274fff";

    var resolver = new DefaultAssemblyResolver();
    resolver.AddSearchDirectory(managedDir);
    var asm = AssemblyDefinition.ReadAssembly(mainDll, new ReaderParameters { AssemblyResolver = resolver });
    var mod = asm.MainModule;
    var acier = AssemblyDefinition.ReadAssembly(acierDll, new ReaderParameters { AssemblyResolver = resolver });

    MethodDefinition FindMethod(TypeDefinition t, string name)
    {
        for (var c = t; c != null; c = c.BaseType?.Resolve())
        {
            var m = c.Methods.FirstOrDefault(x => x.Name == name && !x.HasParameters);
            if (m != null) return m;
        }
        return null;
    }
    TypeDefinition Find(ModuleDefinition m, string full)
    {
        foreach (var t in m.Types)
        {
            var r = FindIn(t, full);
            if (r != null) return r;
        }
        // fallback: substring match (must be unique)
        var all = new List<TypeDefinition>();
        foreach (var t in m.Types) CollectIn(t, full, all);
        if (all.Count == 1) return all[0];
        if (all.Count > 1) throw new Exception("ambiguous: " + full);
        return null;
        static TypeDefinition FindIn(TypeDefinition t, string full)
        {
            if (t.FullName == full) return t;
            foreach (var n in t.NestedTypes)
            {
                var r = FindIn(n, full);
                if (r != null) return r;
            }
            return null;
        }
        static void CollectIn(TypeDefinition t, string full, List<TypeDefinition> out_)
        {
            if (t.FullName.Contains(full)) out_.Add(t);
            foreach (var n in t.NestedTypes) CollectIn(n, full, out_);
        }
    }

    var TS = mod.TypeSystem;
    var strType = TS.String;
    var voidType = TS.Void;
    var excType = new TypeReference("System", "Exception", mod, TS.Corlib);
    var fileType = new TypeReference("System.IO", "File", mod, TS.Corlib);

    MethodReference MkMethod(TypeReference decl, string name, TypeReference ret, bool hasThis, params TypeReference[] ps)
    {
        var mr = new MethodReference(name, ret, decl) { HasThis = hasThis };
        foreach (var p in ps) mr.Parameters.Add(new ParameterDefinition(p));
        return mod.ImportReference(mr);
    }
    var appendAllText = MkMethod(fileType, "AppendAllText", voidType, false, strType, strType);

    TypeReference Acier(string full) => mod.ImportReference(Find(acier.MainModule, full));
    MethodReference AcierCtor(string typeFull)
    {
        var td = Find(acier.MainModule, typeFull);
        var c = td.Methods.First(m => m.IsConstructor && !m.HasParameters);
        return mod.ImportReference(c);
    }
    // property setter on AcierProtocol type (all members are properties)
    MethodReference AcierSet(string typeFull, string prop)
    {
        var td = Find(acier.MainModule, typeFull);
        var pd = td.Properties.First(p => p.Name == prop);
        return mod.ImportReference(pd.SetMethod);
    }

    // ---- types in main module ----
    var mainType = Find(mod, "Main");
    var buildDefType = Find(mod, "BuildDefinition");
    var profileMgrType = Find(mod, "Assets.Scripts.UI.State.ProfileManager.ProfileManager");
    var sceneLoaderType = Find(mod, "Assets.Scripts.Mission.SceneLoader");
    var startupType = Find(mod, "Startup");
    var uiLoadingType = Find(mod, "Assets.Scripts.Mission.UI.UILoadingScreen");
    Console.WriteLine($"types: main={mainType != null} bd={buildDefType != null} pm={profileMgrType != null} sl={sceneLoaderType != null} st={startupType != null} uil={uiLoadingType != null}");

    // ---- inject TraceOffline(string) into ProfileManager ----
    var traceMethod = new MethodDefinition("TraceOffline", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, voidType);
    traceMethod.Parameters.Add(new ParameterDefinition(strType));
    profileMgrType.Methods.Add(traceMethod);
    {
        var il = traceMethod.Body.GetILProcessor();
        traceMethod.Body.InitLocals = true;
        var end = il.Create(OpCodes.Ret);
        var t0 = il.Create(OpCodes.Ldstr, TracePath);
        il.Append(t0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, appendAllText);
        il.Emit(OpCodes.Leave, end);
        var h0 = il.Create(OpCodes.Pop);
        il.Append(h0);
        il.Emit(OpCodes.Leave, end);
        il.Append(end);
        traceMethod.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = t0, TryEnd = h0, HandlerStart = h0, HandlerEnd = end, CatchType = excType
        });
    }
    var traceRef = mod.ImportReference(traceMethod);
    void EmitTrace(ILProcessor il, string msg)
    {
        il.Emit(OpCodes.Ldstr, msg);
        il.Emit(OpCodes.Call, traceRef);
    }

    // generic setter emitters: object must be loaded before calling these
    void SetS(ILProcessor il, string typeFull, string prop, string v)
    {
        il.Emit(OpCodes.Ldstr, v);
        il.Emit(OpCodes.Callvirt, AcierSet(typeFull, prop));
    }
    void SetI4(ILProcessor il, string typeFull, string prop, int v)
    {
        il.Emit(OpCodes.Ldc_I4, v);
        il.Emit(OpCodes.Callvirt, AcierSet(typeFull, prop));
    }
    void SetI8(ILProcessor il, string typeFull, string prop, long v)
    {
        il.Emit(OpCodes.Ldc_I8, v);
        il.Emit(OpCodes.Callvirt, AcierSet(typeFull, prop));
    }

    // ---- inject BuildOfflineProfile() into ProfileManager ----
    var userProfileT = Acier("AcierProtocol.UserProfile");
    var buildProfile = new MethodDefinition("BuildOfflineProfile", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, userProfileT);
    profileMgrType.Methods.Add(buildProfile);
    {
        var il = buildProfile.Body.GetILProcessor();
        buildProfile.Body.InitLocals = true;
        var up = new VariableDefinition(userProfileT); buildProfile.Body.Variables.Add(up);
        var inv = new VariableDefinition(Acier("AcierProtocol.Inventory")); buildProfile.Body.Variables.Add(inv);
        var asv = new VariableDefinition(Acier("AcierProtocol.Assassin")); buildProfile.Body.Variables.Add(asv);
        var ans = new VariableDefinition(Acier("AcierProtocol.Assassins")); buildProfile.Body.Variables.Add(ans);
        var res = new VariableDefinition(Acier("AcierProtocol.Resources")); buildProfile.Body.Variables.Add(res);
        var cnt = new VariableDefinition(Acier("AcierProtocol.Counters")); buildProfile.Body.Variables.Add(cnt);
        var tut = new VariableDefinition(Acier("AcierProtocol.Tutorial")); buildProfile.Body.Variables.Add(tut);
        var mc = new VariableDefinition(Acier("AcierProtocol.MissionCollection")); buildProfile.Body.Variables.Add(mc);
        var us = new VariableDefinition(Acier("AcierProtocol.UserSettings")); buildProfile.Body.Variables.Add(us);

        il.Emit(OpCodes.Newobj, AcierCtor("AcierProtocol.UserProfile"));
        il.Emit(OpCodes.Stloc, up);
        il.Emit(OpCodes.Ldloc, up); SetS(il, "AcierProtocol.UserProfile", "id", "offline");
        il.Emit(OpCodes.Ldloc, up); SetS(il, "AcierProtocol.UserProfile", "userName", "Assassin");
        il.Emit(OpCodes.Ldloc, up); SetI4(il, "AcierProtocol.UserProfile", "level", 1);
        il.Emit(OpCodes.Ldloc, up); SetS(il, "AcierProtocol.UserProfile", "lastUserAssassionId", "offline_ber01");

        il.Emit(OpCodes.Newobj, AcierCtor("AcierProtocol.Inventory"));
        il.Emit(OpCodes.Stloc, inv);
        il.Emit(OpCodes.Ldloc, inv); SetI4(il, "AcierProtocol.Inventory", "maxSize", 100);
        il.Emit(OpCodes.Ldloc, inv);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, Acier("AcierProtocol.InventoryElement"));
        il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.Inventory", "elements"));
        il.Emit(OpCodes.Ldloc, up);
        il.Emit(OpCodes.Ldloc, inv);
        il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.UserProfile", "Inventory"));

        il.Emit(OpCodes.Newobj, AcierCtor("AcierProtocol.Assassin"));
        il.Emit(OpCodes.Stloc, asv);
        il.Emit(OpCodes.Ldloc, asv); SetS(il, "AcierProtocol.Assassin", "id", "offline_ber01");
        il.Emit(OpCodes.Ldloc, asv); SetI4(il, "AcierProtocol.Assassin", "classTypeId", 2);
        il.Emit(OpCodes.Ldloc, asv); SetI4(il, "AcierProtocol.Assassin", "extendedClassTypeId", 2);
        il.Emit(OpCodes.Ldloc, asv); SetI4(il, "AcierProtocol.Assassin", "level", 1);
        il.Emit(OpCodes.Ldloc, asv); SetI8(il, "AcierProtocol.Assassin", "hitPoints", 90);
        il.Emit(OpCodes.Ldloc, asv); SetI8(il, "AcierProtocol.Assassin", "strength", 25);
        il.Emit(OpCodes.Ldloc, asv); SetI8(il, "AcierProtocol.Assassin", "precision", 25);
        il.Emit(OpCodes.Ldloc, asv); SetI8(il, "AcierProtocol.Assassin", "baseWeapon", 23);
        il.Emit(OpCodes.Ldloc, asv); SetI8(il, "AcierProtocol.Assassin", "armoreRating", 13);
        il.Emit(OpCodes.Ldloc, asv); SetI8(il, "AcierProtocol.Assassin", "incognito", 20);
        il.Emit(OpCodes.Ldloc, asv); SetS(il, "AcierProtocol.Assassin", "humanoidGuid", BerHumanoid);
        il.Emit(OpCodes.Ldloc, asv); SetS(il, "AcierProtocol.Assassin", "weaponGuid", BerWeapon);
        il.Emit(OpCodes.Ldloc, asv); SetS(il, "AcierProtocol.Assassin", "headGuid", BerHead);
        il.Emit(OpCodes.Ldloc, asv);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, Acier("AcierProtocol.EquippedElement"));
        il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.Assassin", "equippedElements"));

        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, Acier("AcierProtocol.Assassin"));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, asv);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Nop); // first draft block; cleared and rebuilt below
    }
    // NOTE: array-into-property needs the array on stack under target; rebuild that part below via rewrite.
    // (We redo the tail of BuildOfflineProfile cleanly.)
    buildProfile.Body.Instructions.Clear();
    {
        var il = buildProfile.Body.GetILProcessor();
        var up = buildProfile.Body.Variables[0];
        var inv = buildProfile.Body.Variables[1];
        var asv = buildProfile.Body.Variables[2];
        var ans = buildProfile.Body.Variables[3];
        var res = buildProfile.Body.Variables[4];
        var cnt = buildProfile.Body.Variables[5];
        var tut = buildProfile.Body.Variables[6];
        var mc = buildProfile.Body.Variables[7];
        var us = buildProfile.Body.Variables[8];
        var arrA = new VariableDefinition(new ArrayType(Acier("AcierProtocol.Assassin"))); buildProfile.Body.Variables.Add(arrA);

        il.Emit(OpCodes.Newobj, AcierCtor("AcierProtocol.UserProfile"));
        il.Emit(OpCodes.Stloc, up);
        il.Emit(OpCodes.Ldloc, up); SetS(il, "AcierProtocol.UserProfile", "id", "offline");
        il.Emit(OpCodes.Ldloc, up); SetS(il, "AcierProtocol.UserProfile", "userName", "Assassin");
        il.Emit(OpCodes.Ldloc, up); SetI4(il, "AcierProtocol.UserProfile", "level", 1);
        il.Emit(OpCodes.Ldloc, up); SetS(il, "AcierProtocol.UserProfile", "lastUserAssassionId", "offline_ber01");

        il.Emit(OpCodes.Newobj, AcierCtor("AcierProtocol.Inventory"));
        il.Emit(OpCodes.Stloc, inv);
        il.Emit(OpCodes.Ldloc, inv); SetI4(il, "AcierProtocol.Inventory", "maxSize", 100);
        il.Emit(OpCodes.Ldloc, inv);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, Acier("AcierProtocol.InventoryElement"));
        il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.Inventory", "elements"));
        il.Emit(OpCodes.Ldloc, up);
        il.Emit(OpCodes.Ldloc, inv);
        il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.UserProfile", "Inventory"));

        il.Emit(OpCodes.Newobj, AcierCtor("AcierProtocol.Assassin"));
        il.Emit(OpCodes.Stloc, asv);
        il.Emit(OpCodes.Ldloc, asv); SetS(il, "AcierProtocol.Assassin", "id", "offline_ber01");
        il.Emit(OpCodes.Ldloc, asv); SetI4(il, "AcierProtocol.Assassin", "classTypeId", 2);
        il.Emit(OpCodes.Ldloc, asv); SetI4(il, "AcierProtocol.Assassin", "extendedClassTypeId", 2);
        il.Emit(OpCodes.Ldloc, asv); SetI4(il, "AcierProtocol.Assassin", "level", 1);
        il.Emit(OpCodes.Ldloc, asv); SetI8(il, "AcierProtocol.Assassin", "hitPoints", 90);
        il.Emit(OpCodes.Ldloc, asv); SetI8(il, "AcierProtocol.Assassin", "strength", 25);
        il.Emit(OpCodes.Ldloc, asv); SetI8(il, "AcierProtocol.Assassin", "precision", 25);
        il.Emit(OpCodes.Ldloc, asv); SetI8(il, "AcierProtocol.Assassin", "baseWeapon", 23);
        il.Emit(OpCodes.Ldloc, asv); SetI8(il, "AcierProtocol.Assassin", "armoreRating", 13);
        il.Emit(OpCodes.Ldloc, asv); SetI8(il, "AcierProtocol.Assassin", "incognito", 20);
        il.Emit(OpCodes.Ldloc, asv); SetS(il, "AcierProtocol.Assassin", "humanoidGuid", BerHumanoid);
        il.Emit(OpCodes.Ldloc, asv); SetS(il, "AcierProtocol.Assassin", "weaponGuid", BerWeapon);
        il.Emit(OpCodes.Ldloc, asv); SetS(il, "AcierProtocol.Assassin", "headGuid", BerHead);
        il.Emit(OpCodes.Ldloc, asv);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, Acier("AcierProtocol.EquippedElement"));
        il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.Assassin", "equippedElements"));
        il.Emit(OpCodes.Ldloc, asv);
        il.Emit(OpCodes.Newobj, AcierCtor("AcierProtocol.AssassinName"));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "Assassin");
        il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.AssassinName", "overrideName"));
        il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.Assassin", "assassinName"));

        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, Acier("AcierProtocol.Assassin"));
        il.Emit(OpCodes.Stloc, arrA);
        il.Emit(OpCodes.Ldloc, arrA);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, asv);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Newobj, AcierCtor("AcierProtocol.Assassins"));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldloc, arrA);
        il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.Assassins", "assassins"));
        il.Emit(OpCodes.Stloc, ans);
        il.Emit(OpCodes.Ldloc, up);
        il.Emit(OpCodes.Ldloc, ans);
        il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.UserProfile", "assassins"));

        il.Emit(OpCodes.Newobj, AcierCtor("AcierProtocol.Resources"));
        il.Emit(OpCodes.Stloc, res);
        il.Emit(OpCodes.Ldloc, res);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, Acier("AcierProtocol.Resource"));
        il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.Resources", "resources"));
        il.Emit(OpCodes.Ldloc, up);
        il.Emit(OpCodes.Ldloc, res);
        il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.UserProfile", "resources"));

        il.Emit(OpCodes.Newobj, AcierCtor("AcierProtocol.Counters"));
        il.Emit(OpCodes.Stloc, cnt);
        il.Emit(OpCodes.Ldloc, cnt);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, Acier("AcierProtocol.Counter"));
        il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.Counters", "counters"));
        il.Emit(OpCodes.Ldloc, up);
        il.Emit(OpCodes.Ldloc, cnt);
        il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.UserProfile", "counters"));

        il.Emit(OpCodes.Newobj, AcierCtor("AcierProtocol.Tutorial"));
        il.Emit(OpCodes.Stloc, tut);
        il.Emit(OpCodes.Ldloc, tut);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, Acier("AcierProtocol.Tutorial/Category"));
        il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.Tutorial", "categories"));
        il.Emit(OpCodes.Ldloc, up);
        il.Emit(OpCodes.Ldloc, tut);
        il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.UserProfile", "tutorial"));

        il.Emit(OpCodes.Newobj, AcierCtor("AcierProtocol.MissionCollection"));
        il.Emit(OpCodes.Stloc, mc);
        il.Emit(OpCodes.Ldloc, mc);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, Acier("AcierProtocol.MissionCollection/AssassinMissions"));
        il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.MissionCollection", "assassinMissions"));
        il.Emit(OpCodes.Ldloc, up);
        il.Emit(OpCodes.Ldloc, mc);
        il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.UserProfile", "missionCollection"));

        // p.achievements = new Achievements { entries = new Achievement[0] }
        {
            var ach = new VariableDefinition(Acier("AcierProtocol.Achievements")); buildProfile.Body.Variables.Add(ach);
            il.Emit(OpCodes.Newobj, AcierCtor("AcierProtocol.Achievements"));
            il.Emit(OpCodes.Stloc, ach);
            il.Emit(OpCodes.Ldloc, ach);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, Acier("AcierProtocol.Achievement"));
            il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.Achievements", "entries"));
            il.Emit(OpCodes.Ldloc, up);
            il.Emit(OpCodes.Ldloc, ach);
            il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.UserProfile", "achievements"));
        }

        il.Emit(OpCodes.Newobj, AcierCtor("AcierProtocol.UserSettings"));
        il.Emit(OpCodes.Stloc, us);
        il.Emit(OpCodes.Ldloc, us);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, Acier("AcierProtocol.PushNotificationSetting"));
        il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.UserSettings", "pushNotificationSettings"));
        il.Emit(OpCodes.Ldloc, up);
        il.Emit(OpCodes.Ldloc, us);
        il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.UserProfile", "userSettings"));

        il.Emit(OpCodes.Ldloc, up);
        il.Emit(OpCodes.Ret);
    }
    var buildProfileRef = mod.ImportReference(buildProfile);

    // ---- inject GetOfflineAvatarClasses() into ProfileManager ----
    var acdT = Acier("AcierProtocol.AvatarClassDefinitions");
    var buildClasses = new MethodDefinition("GetOfflineAvatarClasses", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, acdT);
    profileMgrType.Methods.Add(buildClasses);
    {
        var il = buildClasses.Body.GetILProcessor();
        buildClasses.Body.InitLocals = true;
        var dVar = new VariableDefinition(acdT); buildClasses.Body.Variables.Add(dVar);
        var cVar = new VariableDefinition(Acier("AcierProtocol.AvatarClassDefinitions/AvatarClass")); buildClasses.Body.Variables.Add(cVar);
        var hVar = new VariableDefinition(Acier("AcierProtocol.AvatarClassDefinitions/AvatarClass/AvatarHeadSettings")); buildClasses.Body.Variables.Add(hVar);
        var arrC = new VariableDefinition(new ArrayType(Acier("AcierProtocol.AvatarClassDefinitions/AvatarClass"))); buildClasses.Body.Variables.Add(arrC);
        il.Emit(OpCodes.Newobj, AcierCtor("AcierProtocol.AvatarClassDefinitions"));
        il.Emit(OpCodes.Stloc, dVar);
        il.Emit(OpCodes.Newobj, AcierCtor("AcierProtocol.AvatarClassDefinitions/AvatarClass"));
        il.Emit(OpCodes.Stloc, cVar);
        il.Emit(OpCodes.Ldloc, cVar); SetI4(il, "AcierProtocol.AvatarClassDefinitions/AvatarClass", "classTypeId", 2);
        il.Emit(OpCodes.Ldloc, cVar); SetS(il, "AcierProtocol.AvatarClassDefinitions/AvatarClass", "humanoidGuid", BerHumanoid);
        il.Emit(OpCodes.Ldloc, cVar); SetS(il, "AcierProtocol.AvatarClassDefinitions/AvatarClass", "weaponGuid", BerWeapon);
        il.Emit(OpCodes.Newobj, AcierCtor("AcierProtocol.AvatarClassDefinitions/AvatarClass/AvatarHeadSettings"));
        il.Emit(OpCodes.Stloc, hVar);
        il.Emit(OpCodes.Ldloc, hVar); SetS(il, "AcierProtocol.AvatarClassDefinitions/AvatarClass/AvatarHeadSettings", "headGuid", BerHead);
        il.Emit(OpCodes.Ldloc, cVar);
        il.Emit(OpCodes.Ldloc, hVar);
        il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.AvatarClassDefinitions/AvatarClass", "heads"));
        il.Emit(OpCodes.Ldloc, cVar);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, Acier("AcierProtocol.AvatarClassDefinitions/AvatarClass/AvatarTier"));
        il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.AvatarClassDefinitions/AvatarClass", "tiers"));
        il.Emit(OpCodes.Ldloc, cVar);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, strType);
        il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.AvatarClassDefinitions/AvatarClass", "hirelingAbilityGuids"));
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, Acier("AcierProtocol.AvatarClassDefinitions/AvatarClass"));
        il.Emit(OpCodes.Stloc, arrC);
        il.Emit(OpCodes.Ldloc, arrC);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, cVar);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, dVar);
        il.Emit(OpCodes.Ldloc, arrC);
        il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.AvatarClassDefinitions", "classes"));
        il.Emit(OpCodes.Ldloc, dVar);
        il.Emit(OpCodes.Ret);
    }
    var buildClassesRef = mod.ImportReference(buildClasses);

    // ---- P-A: ShouldStartOnline -> false (+ sets explicit OfflineMode flag) ----
    var offlineFlag = new FieldDefinition("OfflineMode", FieldAttributes.Public | FieldAttributes.Static, TS.Boolean);
    mainType.Fields.Add(offlineFlag);
    var offlineFlagRef = mod.ImportReference(offlineFlag);
    {
        var m = mainType.Methods.First(x => x.Name == "ShouldStartOnline");
        m.Body.Instructions.Clear(); m.Body.ExceptionHandlers.Clear();
        m.Body.Variables.Clear(); m.Body.InitLocals = false;
        var il = m.Body.GetILProcessor();
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stsfld, offlineFlagRef);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        Console.WriteLine("patched ShouldStartOnline");
    }

    // ---- P-B: BuildDefinition.get_Url -> local file base when Custom ----
    {
        var m = buildDefType.Methods.First(x => x.Name == "get_Url");
        var old = m.Body.Instructions.ToList();
        m.Body.Instructions.Clear(); m.Body.ExceptionHandlers.Clear();
        m.Body.InitLocals = true;
        var urlVar = new VariableDefinition(strType); m.Body.Variables.Add(urlVar);
        var il = m.Body.GetILProcessor();
        var getUriType = mod.ImportReference(buildDefType.Methods.First(x => x.Name == "get_UriType"));
        var notCustom = old[0];
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, getUriType);
        il.Emit(OpCodes.Ldc_I4, 10); // EUriType.Custom
        il.Emit(OpCodes.Bne_Un, notCustom);
        il.Emit(OpCodes.Ldstr, LocalBase);
        il.Emit(OpCodes.Stloc, urlVar);
        il.Emit(OpCodes.Ldloc, urlVar);
        il.Emit(OpCodes.Ret);
        foreach (var ins in old) il.Append(ins);
        Console.WriteLine("patched get_Url");
    }

    // ---- P-C: ProfileManager.get_Profile lazy offline profile ----
    {
        var m = profileMgrType.Methods.First(x => x.Name == "get_Profile");
        var fld = mod.ImportReference(profileMgrType.Fields.First(f => f.Name == "m_profile"));
        m.Body.Instructions.Clear(); m.Body.ExceptionHandlers.Clear();
        m.Body.Variables.Clear(); m.Body.InitLocals = false;
        var il = m.Body.GetILProcessor();
        var done = il.Create(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, fld);
        il.Emit(OpCodes.Brtrue_S, done);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, buildProfileRef);
        il.Emit(OpCodes.Stfld, fld);
        il.Append(done);
        il.Emit(OpCodes.Ldfld, fld);
        il.Emit(OpCodes.Ret);
        Console.WriteLine("patched get_Profile");
    }

    // ---- P-D: SceneLoader.LoadScene avatar statics swap ----
    // replaces the 3-instr chain (get_Instance + get_TemplateManager + get_AvatarDefinition)
    // with a single static call (keeps the stack balanced!)
    {
        var m = sceneLoaderType.Methods.First(x => x.Name == "LoadScene" && !x.HasParameters);
        int swapped = 0;
        var list = m.Body.Instructions.ToList();
        for (int ii = 0; ii < list.Count; ii++)
        {
            var ins = list[ii];
            if ((ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt) &&
                ins.Operand is MethodReference mr && mr.Name == "get_AvatarDefinition" &&
                mr.DeclaringType.FullName.Contains("TemplateManager"))
            {
                if (ii < 2) throw new Exception("P-D pattern too short");
                var a = list[ii - 2]; var b = list[ii - 1];
                bool aOk = (a.OpCode == OpCodes.Ldsfld) && a.Operand is FieldReference afr && afr.Name == "Instance";
                bool bOk = (b.OpCode == OpCodes.Callvirt) && b.Operand is MethodReference bmr && bmr.Name == "get_TemplateManager";
                if (!aOk || !bOk) throw new Exception("P-D pattern mismatch");
                a.OpCode = OpCodes.Nop; a.Operand = null;
                b.OpCode = OpCodes.Nop; b.Operand = null;
                ins.OpCode = OpCodes.Call;
                ins.Operand = buildClassesRef;
                swapped++;
            }
        }
        Console.WriteLine($"swapped get_AvatarDefinition chains: {swapped}");
    }

    // ---- P-G: skip server-dependent inits when offline (explicit flag, NOT IsNetworkEnabled) ----
    void GuardOffline(TypeDefinition t, string method)
    {
        var mm = t.Methods.First(x => x.Name == method && x.HasBody);
        var il = mm.Body.GetILProcessor();
        var first = mm.Body.Instructions[0];
        var cont = il.Create(OpCodes.Nop);
        il.InsertBefore(first, il.Create(OpCodes.Ldsfld, offlineFlagRef));
        il.InsertBefore(first, il.Create(OpCodes.Brfalse_S, cont));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
        il.InsertBefore(first, cont);
        Console.WriteLine($"guarded {t.Name}.{method}");
    }
    {
        var amType = Find(mod, "Assets.Scripts.Achievements.AchievementsManager");
        if (amType != null) GuardOffline(amType, "InitStatics");
        var lsType = Find(mod, "Assets.Scripts.Database.LocalStorage");
        if (lsType != null) GuardOffline(lsType, "ValidateStorageForOldUserOnStartUp");
        var ceType = Find(mod, "Assets.Scripts.UI.Notifications.UICampaignEvaluator");
        if (ceType != null) GuardOffline(ceType, "Init");
        var rdType = Find(mod, "Assets.Scripts.UI.Behaviours.UIAssassinRankDisplay");
        if (rdType != null) GuardOffline(rdType, "SetRank");
    }

    // ---- P-H: free-disk check must not call JNI (pool threads are not attached to JVM) ----
    {
        var m = mainType.Methods.First(x => x.Name == "GetFreeDiskspaceImpl");
        m.Body.Instructions.Clear(); m.Body.ExceptionHandlers.Clear();
        m.Body.Variables.Clear(); m.Body.InitLocals = false;
        var il = m.Body.GetILProcessor();
        il.Emit(OpCodes.Ldc_I8, -1L); // ulong.MaxValue
        il.Emit(OpCodes.Ret);
        Console.WriteLine("patched GetFreeDiskspaceImpl");
    }

    // ---- P-I: force ETC2/Low quality (staging only has Low+ETC2). UNCONDITIONAL:
    // IsNetworkEnabled now means "tutorial-file mode", not "has server".
    {
        var m = startupType.Methods.First(x => x.Name == "ApplyRenderQuality");
        var il = m.Body.GetILProcessor();
        var getQ = mod.ImportReference(startupType.Methods.First(x => x.Name == "get_QualitySettings"));
        var qsT = Find(acier.MainModule, "AcierProtocol.QualitySettings");
        var setTex = mod.ImportReference(qsT.Properties.First(p => p.Name == "textureType").SetMethod);
        var setQ = mod.ImportReference(qsT.Properties.First(p => p.Name == "assetBundleQuality").SetMethod);
        var setCrowd = mod.ImportReference(qsT.Properties.First(p => p.Name == "crowdQuality").SetMethod);
        var ret = m.Body.Instructions.Last(i => i.OpCode == OpCodes.Ret);
        il.InsertBefore(ret, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(ret, il.Create(OpCodes.Call, getQ));
        il.InsertBefore(ret, il.Create(OpCodes.Ldc_I4_2));
        il.InsertBefore(ret, il.Create(OpCodes.Callvirt, setTex));
        il.InsertBefore(ret, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(ret, il.Create(OpCodes.Call, getQ));
        il.InsertBefore(ret, il.Create(OpCodes.Ldc_I4_1));
        il.InsertBefore(ret, il.Create(OpCodes.Callvirt, setQ));
        il.InsertBefore(ret, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(ret, il.Create(OpCodes.Call, getQ));
        il.InsertBefore(ret, il.Create(OpCodes.Ldc_R4, 0.8f));
        il.InsertBefore(ret, il.Create(OpCodes.Callvirt, setCrowd));
        // ApplyRenderQuality copies QualitySettings.crowdQuality -> Crowd.CrowdQuality
        // BEFORE our set above, so set the live static too (else pool stays ~4).
        var crowdT0 = Find(mod, "Assets.Scripts.Crowd.Crowd");
        var setLive = mod.ImportReference(crowdT0.Methods.First(mt => mt.Name == "set_CrowdQuality"));
        il.InsertBefore(ret, il.Create(OpCodes.Ldc_R4, 0.8f));
        il.InsertBefore(ret, il.Create(OpCodes.Call, setLive));
        // Full-res + bumped shaders: fallback Low profiles render at HALF res
        // (RenderingUpscaler 0.5x) with Diffuse-only shaders (LOD 200), which
        // blunts High bundles. Force High-tier presentation; Unity-side calls
        // must be re-emitted because the original body already ran above.
        var setDown = mod.ImportReference(qsT.Properties.First(p => p.Name == "downScaling").SetMethod);
        var setQL = mod.ImportReference(qsT.Properties.First(p => p.Name == "qualityLevel").SetMethod);
        var setLOD = mod.ImportReference(qsT.Properties.First(p => p.Name == "renderingLOD").SetMethod);
        il.InsertBefore(ret, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(ret, il.Create(OpCodes.Call, getQ));
        il.InsertBefore(ret, il.Create(OpCodes.Ldc_I4_0));
        il.InsertBefore(ret, il.Create(OpCodes.Callvirt, setDown));
        il.InsertBefore(ret, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(ret, il.Create(OpCodes.Call, getQ));
        il.InsertBefore(ret, il.Create(OpCodes.Ldc_I4_3));
        il.InsertBefore(ret, il.Create(OpCodes.Callvirt, setQL));
        il.InsertBefore(ret, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(ret, il.Create(OpCodes.Call, getQ));
        il.InsertBefore(ret, il.Create(OpCodes.Ldc_I4, 400));
        il.InsertBefore(ret, il.Create(OpCodes.Callvirt, setLOD));
        var ueAsmQ = AssemblyDefinition.ReadAssembly(Path.Combine(managedDir, "UnityEngine.dll"), new ReaderParameters { AssemblyResolver = resolver });
        var setTier = mod.ImportReference(Find(ueAsmQ.MainModule, "UnityEngine.QualitySettings").Methods.First(mt => mt.Name == "SetQualityLevel" && mt.Parameters.Count == 2));
        // Tier 5 (max; fallback table + viewer both reference it): bigger
        // shadowmaps than tier 3. All presentation overrides above are
        // re-emitted after this, so only the preset's texture/shadow
        // resolution survives.
        il.InsertBefore(ret, il.Create(OpCodes.Ldc_I4_5));
        il.InsertBefore(ret, il.Create(OpCodes.Ldc_I4_1));
        il.InsertBefore(ret, il.Create(OpCodes.Call, setTier));
        var shLib = AssemblyDefinition.ReadAssembly(Path.Combine(managedDir, "SharedBaseLib.dll"), new ReaderParameters { AssemblyResolver = resolver });
        var rsT = Find(shLib.MainModule, "RenderingSettings");
        var getRS = mod.ImportReference(rsT.Methods.First(mt => mt.Name == "get_Instance"));
        var setRSLOD = mod.ImportReference(rsT.Methods.First(mt => mt.Name == "set_shaderLOD"));
        il.InsertBefore(ret, il.Create(OpCodes.Call, getRS));
        il.InsertBefore(ret, il.Create(OpCodes.Ldc_I4, 400));
        il.InsertBefore(ret, il.Create(OpCodes.Callvirt, setRSLOD));
        // Safe presentation tweaks (all after SetQualityLevel so they win):
        // full-res textures, 2x MSAA, aniso off, longer LODs, tight stable shadows.
        // Tier-3 defaults blunt all of these; High bundles need them.
        var ueQ = Find(ueAsmQ.MainModule, "UnityEngine.QualitySettings");
        var setTexLim = mod.ImportReference(ueQ.Methods.First(mt => mt.Name == "set_masterTextureLimit"));
        var setAniso = mod.ImportReference(ueQ.Methods.First(mt => mt.Name == "set_anisotropicFiltering"));
        var setAA = mod.ImportReference(ueQ.Methods.First(mt => mt.Name == "set_antiAliasing"));
        var setLodBias = mod.ImportReference(ueQ.Methods.First(mt => mt.Name == "set_lodBias"));
        var setShDist = mod.ImportReference(ueQ.Methods.First(mt => mt.Name == "set_shadowDistance"));
        var setShCasc = mod.ImportReference(ueQ.Methods.First(mt => mt.Name == "set_shadowCascades"));
        var setShProj = mod.ImportReference(ueQ.Methods.First(mt => mt.Name == "set_shadowProjection"));
        var setShSplit = mod.ImportReference(ueQ.Methods.First(mt => mt.Name == "set_shadowCascade2Split"));
        var setShNear = mod.ImportReference(ueQ.Methods.First(mt => mt.Name == "set_shadowNearPlaneOffset"));
        il.InsertBefore(ret, il.Create(OpCodes.Ldc_I4_0));
        il.InsertBefore(ret, il.Create(OpCodes.Call, setTexLim));
        il.InsertBefore(ret, il.Create(OpCodes.Ldc_I4_0));
        il.InsertBefore(ret, il.Create(OpCodes.Call, setAniso));
        il.InsertBefore(ret, il.Create(OpCodes.Ldc_I4_2));
        il.InsertBefore(ret, il.Create(OpCodes.Call, setAA));
        il.InsertBefore(ret, il.Create(OpCodes.Ldc_R4, 1.5f));
        il.InsertBefore(ret, il.Create(OpCodes.Call, setLodBias));
        il.InsertBefore(ret, il.Create(OpCodes.Ldc_R4, 15f));
        il.InsertBefore(ret, il.Create(OpCodes.Call, setShDist));
        il.InsertBefore(ret, il.Create(OpCodes.Ldc_I4_2));
        il.InsertBefore(ret, il.Create(OpCodes.Call, setShCasc));
        il.InsertBefore(ret, il.Create(OpCodes.Ldc_I4_1));
        il.InsertBefore(ret, il.Create(OpCodes.Call, setShProj));
        il.InsertBefore(ret, il.Create(OpCodes.Ldc_R4, 0.33f));
        il.InsertBefore(ret, il.Create(OpCodes.Call, setShSplit));
        il.InsertBefore(ret, il.Create(OpCodes.Ldc_R4, 4f));
        il.InsertBefore(ret, il.Create(OpCodes.Call, setShNear));
        Console.WriteLine("patched ApplyRenderQuality");
    }

    // ---- P-J: 60fps in missions (was 25) + boot-target switch (3rd arg) ----
    // freeroam: SetupAnimusFile scene AnimusGlobe->Firenze, MissionType 0->1.
    // hub (default): Co_LoadMission tutorial branch returns null category so
    // boot falls through to BuildDefinition.AnimusMission (AnimusGlobe hub).
    bool freeroam = args.Length > 2 && args[2] == "freeroam";
    bool hub = !freeroam;
    {
        var m = sceneLoaderType.Methods.First(x => x.Name == "SetLoadedMissionData");
        int n = 0;
        foreach (var ins in m.Body.Instructions)
        {
            if ((ins.OpCode == OpCodes.Ldc_I4_S || ins.OpCode == OpCodes.Ldc_I4) && ins.Operand is sbyte sb && sb == 25) { ins.Operand = (sbyte)60; n++; }
            else if (ins.OpCode == OpCodes.Ldc_I4 && ins.Operand is int iv && iv == 25) { ins.Operand = 60; n++; }
        }
        Console.WriteLine($"patched fps caps: {n}");
    }
    if (freeroam)
    {
        // BuildDefinition.SetupAnimusFile: Scene AnimusGlobe -> Firenze city, MissionType 0 -> 1
        var bd = buildDefType.Methods.First(x => x.Name == "SetupAnimusFile");
        int n = 0;
        var blist = bd.Body.Instructions.ToList();
        foreach (var ins in blist)
        {
            if (ins.OpCode == OpCodes.Ldstr && ins.Operand is string st && st == "AnimusGlobe") { ins.Operand = "Firenze_santacroce_sunny"; n++; }
        }
        for (int bi = 1; bi < blist.Count; bi++)
        {
            var ins = blist[bi];
            if ((ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt) && ins.Operand is MethodReference mmr && mmr.Name == "set_MissionType")
            {
                var prev = blist[bi - 1];
                if (prev.OpCode == OpCodes.Ldc_I4_0) { prev.OpCode = OpCodes.Ldc_I4_1; n++; }
            }
        }
        Console.WriteLine($"freeroam swaps: {n}");
    }
    if (hub)
    {
        // UITutorialManager.GetCategory -> null: Co_LoadMission skips the
        // tutorial-file branch (category != null fails) and loads AnimusMission.
        var tmT = Find(mod, "Assets.Scripts.UI.Ingame.Implementation.UITutorialManager");
        var gm = tmT.Methods.First(x => x.Name == "GetCategory" && x.Parameters.Count == 1);
        gm.Body.Instructions.Clear(); gm.Body.ExceptionHandlers.Clear();
        gm.Body.Variables.Clear(); gm.Body.InitLocals = false;
        var gil = gm.Body.GetILProcessor();
        gil.Emit(OpCodes.Ldnull);
        gil.Emit(OpCodes.Ret);
        Console.WriteLine("hub mode: GetCategory returns null");
    }

    // ---- P-K retired: was FPS meter + alive marker (log spam). Dropped for clean build. ----

    // ---- P-L: tutorial branch online-flag (file missions load, still no server) ----
    {
        var m = mainType.Methods.First(x => x.Name == "get_IsNetworkEnabled");
        m.Body.Instructions.Clear(); m.Body.ExceptionHandlers.Clear();
        m.Body.Variables.Clear(); m.Body.InitLocals = false;
        var il = m.Body.GetILProcessor();
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        Console.WriteLine("patched get_IsNetworkEnabled");
        foreach (var um in new[] { "Update", "SetTutorialStep" }) {
        var pm = um == "Update"
            ? profileMgrType.Methods.First(x => x.Name == um && !x.HasParameters)
            : profileMgrType.Methods.First(x => x.Name == um);
        var pil = pm.Body.GetILProcessor();
        var getDs = mod.ImportReference(profileMgrType.Methods.First(x => x.Name == "get_DataStore"));
        var first = pm.Body.Instructions[0];
        var cont = pil.Create(OpCodes.Nop);
        pil.InsertBefore(first, pil.Create(OpCodes.Ldarg_0));
        pil.InsertBefore(first, pil.Create(OpCodes.Callvirt, getDs));
        pil.InsertBefore(first, pil.Create(OpCodes.Brtrue_S, cont));
        pil.InsertBefore(first, pil.Create(OpCodes.Ret));
        pil.InsertBefore(first, cont);
        Console.WriteLine($"patched ProfileManager.{um}");
        }
    }

    // ---- P-Q: offline tutorial mapping (server statics stand-in) ----
    {
        var tcmT = Acier("AcierProtocol.TutorialCategoryMapping");
        var entT = Acier("AcierProtocol.TutorialCategoryMapping/Entry");
        var mkMap = new MethodDefinition("GetOfflineTutorialMapping", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, tcmT);
        profileMgrType.Methods.Add(mkMap);
        var il = mkMap.Body.GetILProcessor();
        mkMap.Body.InitLocals = true;
        var mapV = new VariableDefinition(tcmT); mkMap.Body.Variables.Add(mapV);
        var entV = new VariableDefinition(entT); mkMap.Body.Variables.Add(entV);
        var arrV = new VariableDefinition(new ArrayType(entT)); mkMap.Body.Variables.Add(arrV);
        il.Emit(OpCodes.Ldc_I4, 16);
        il.Emit(OpCodes.Newarr, entT);
        il.Emit(OpCodes.Stloc, arrV);
        int[] cats = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };
        for (int ci = 0; ci < cats.Length; ci++)
        {
            il.Emit(OpCodes.Newobj, AcierCtor("AcierProtocol.TutorialCategoryMapping/Entry"));
            il.Emit(OpCodes.Stloc, entV);
            il.Emit(OpCodes.Ldloc, entV);
            il.Emit(OpCodes.Ldc_I4, cats[ci]);
            il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.TutorialCategoryMapping/Entry", "tutorialCategory"));
            il.Emit(OpCodes.Ldloc, entV);
            if (ci == 0)
            {
                // intro tutorial must report NOT finished -> grant an entitlement nobody owns
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Newarr, strType);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldstr, "offline_tutorial_lock");
                il.Emit(OpCodes.Stelem_Ref);
            }
            else
            {
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Newarr, strType);
            }
            il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.TutorialCategoryMapping/Entry", "grantedEntitlements"));
            il.Emit(OpCodes.Ldloc, entV);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, strType);
            il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.TutorialCategoryMapping/Entry", "reqEntitlements"));
            il.Emit(OpCodes.Ldloc, arrV);
            il.Emit(OpCodes.Ldc_I4, ci);
            il.Emit(OpCodes.Ldloc, entV);
            il.Emit(OpCodes.Stelem_Ref);
        }
        il.Emit(OpCodes.Newobj, AcierCtor("AcierProtocol.TutorialCategoryMapping"));
        il.Emit(OpCodes.Stloc, mapV);
        il.Emit(OpCodes.Ldloc, mapV);
        il.Emit(OpCodes.Ldloc, arrV);
        il.Emit(OpCodes.Callvirt, AcierSet("AcierProtocol.TutorialCategoryMapping", "entries"));
        il.Emit(OpCodes.Ldloc, mapV);
        il.Emit(OpCodes.Ret);
        var mkMapRef = mod.ImportReference(mkMap);
        // swap Singleton.StateManager.Instance.TemplateManager.TutorialCategoryMapping chain
        var thType = Find(mod, "Assets.Scripts.UI.Tutorial.TutorialHelper");
        var gm2 = thType.Methods.First(x => x.Name == "GetCategoryMapping");
        var glist = gm2.Body.Instructions.ToList();
        int nsw = 0;
        for (int gi = 0; gi < glist.Count; gi++)
        {
            var gins = glist[gi];
            if ((gins.OpCode == OpCodes.Call || gins.OpCode == OpCodes.Callvirt) &&
                gins.Operand is MethodReference gmr && gmr.Name == "get_TutorialCategoryMapping")
            {
                if (gi < 2) throw new Exception("P-Q pattern too short");
                var ga = glist[gi - 2]; var gb = glist[gi - 1];
                bool aOk = (ga.OpCode == OpCodes.Ldsfld);
                bool bOk = (gb.OpCode == OpCodes.Callvirt) && gb.Operand is MethodReference gbmr && gbmr.Name == "get_TemplateManager";
                if (!aOk || !bOk) throw new Exception("P-Q pattern mismatch");
                ga.OpCode = OpCodes.Nop; ga.Operand = null;
                gb.OpCode = OpCodes.Nop; gb.Operand = null;
                gins.OpCode = OpCodes.Call;
                gins.Operand = mkMapRef;
                nsw++;
            }
        }
        Console.WriteLine($"swapped TutorialCategoryMapping chains: {nsw}");
    }

    // ---- P-R: GetCategoryMapping never returns null (default Entry fallback) ----
    {
        var thType = Find(mod, "Assets.Scripts.UI.Tutorial.TutorialHelper");
        var gm = thType.Methods.First(x => x.Name == "GetCategoryMapping");
        var list = gm.Body.Instructions.ToList();
        int n = 0;
        for (int gi = 0; gi < list.Count - 1; gi++)
        {
            if (list[gi].OpCode == OpCodes.Ldnull && list[gi + 1].OpCode == OpCodes.Ret)
            {
                var il = gm.Body.GetILProcessor();
                var entT = Acier("AcierProtocol.TutorialCategoryMapping/Entry");
                var ev = new VariableDefinition(entT); gm.Body.Variables.Add(ev);
                gm.Body.InitLocals = true;
                var repl = new List<Instruction>();
                repl.Add(il.Create(OpCodes.Newobj, AcierCtor("AcierProtocol.TutorialCategoryMapping/Entry")));
                repl.Add(il.Create(OpCodes.Stloc, ev));
                repl.Add(il.Create(OpCodes.Ldloc, ev));
                repl.Add(il.Create(OpCodes.Ldarg_0));
                repl.Add(il.Create(OpCodes.Callvirt, AcierSet("AcierProtocol.TutorialCategoryMapping/Entry", "tutorialCategory")));
                repl.Add(il.Create(OpCodes.Ldloc, ev));
                repl.Add(il.Create(OpCodes.Ldc_I4_0));
                repl.Add(il.Create(OpCodes.Newarr, strType));
                repl.Add(il.Create(OpCodes.Callvirt, AcierSet("AcierProtocol.TutorialCategoryMapping/Entry", "grantedEntitlements")));
                repl.Add(il.Create(OpCodes.Ldloc, ev));
                repl.Add(il.Create(OpCodes.Ldc_I4_0));
                repl.Add(il.Create(OpCodes.Newarr, strType));
                repl.Add(il.Create(OpCodes.Callvirt, AcierSet("AcierProtocol.TutorialCategoryMapping/Entry", "reqEntitlements")));
                repl.Add(il.Create(OpCodes.Ldloc, ev));
                repl.Add(il.Create(OpCodes.Ret));
                // replace ldnull with first, ret stays as final ret; insert middle before ret
                list[gi].OpCode = repl[0].OpCode; list[gi].Operand = repl[0].Operand;
                for (int k = 1; k < repl.Count - 1; k++) il.InsertBefore(list[gi + 1], repl[k]);
                n++;
            }
        }
        Console.WriteLine($"patched GetCategoryMapping null-returns: {n}");
    }

    // ---- P-S: snap player spawn to navmesh (any approximate spawn lands walkable) ----
    {
        var sharedLib = AssemblyDefinition.ReadAssembly(Path.Combine(managedDir, "SharedBaseLib.dll"), new ReaderParameters { AssemblyResolver = resolver });
        var ueLib = AssemblyDefinition.ReadAssembly(Path.Combine(managedDir, "UnityEngine.dll"), new ReaderParameters { AssemblyResolver = resolver });
        var snapT = Find(sharedLib.MainModule, "UbiCore.Helpers.SceneTools");
        var snapM = mod.ImportReference(snapT.Methods.First(mt => mt.Name == "SnapToNavMesh"));
        var compT = Find(ueLib.MainModule, "UnityEngine.Component");
        var getTr = mod.ImportReference(compT.Methods.First(mt => mt.Name == "get_transform"));
        var spT = Find(mod, "MissionPlayerSpawn");
        var pre = spT.Methods.First(x => x.Name == "OnPreActivate");
        var pil = pre.Body.GetILProcessor();
        var f0 = pre.Body.Instructions[0];
        pil.InsertBefore(f0, pil.Create(OpCodes.Ldarg_0));
        pil.InsertBefore(f0, pil.Create(OpCodes.Callvirt, getTr));
        pil.InsertBefore(f0, pil.Create(OpCodes.Ldc_I4_M1));
        pil.InsertBefore(f0, pil.Create(OpCodes.Ldc_R4, 100f));
        pil.InsertBefore(f0, pil.Create(OpCodes.Call, snapM));
        Console.WriteLine("patched spawn snap");
        var mspT = Find(mod, "MissionSpawnEntity`1");
        var mpre = mspT.Methods.First(x => x.Name == "OnPreActivate");
        var mpil = mpre.Body.GetILProcessor();
        var mf0 = mpre.Body.Instructions[0];
        mpil.InsertBefore(mf0, mpil.Create(OpCodes.Ldarg_0));
        mpil.InsertBefore(mf0, mpil.Create(OpCodes.Callvirt, getTr));
        mpil.InsertBefore(mf0, mpil.Create(OpCodes.Ldc_I4_M1));
        mpil.InsertBefore(mf0, mpil.Create(OpCodes.Ldc_R4, 100f));
        mpil.InsertBefore(mf0, mpil.Create(OpCodes.Call, snapM));
        Console.WriteLine("patched npc spawn snap");
    }

    // ---- P-T: disable Play Games auto-sign-in (external app launch + crash vector) ----
    {
        var gpgT = Find(mod, "Assets.Scripts.Achievements.ClientAchievementsAndroid");
        var gm = gpgT.Methods.First(x => x.Name == "Init");
        gm.Body.Instructions.Clear(); gm.Body.ExceptionHandlers.Clear();
        gm.Body.Variables.Clear(); gm.Body.InitLocals = false;
        var gil = gm.Body.GetILProcessor();
        gil.Emit(OpCodes.Ret);
        Console.WriteLine("patched GPG Init");
    }

    // ---- P-E retired: entry traces + mission-state trace. Dropped for clean build. ----
    // NpcData.AddDefinition tracer retired (127 lines/run). Dropped for clean build.
    // Alias: mission Papal guid -> live enemy_papal_medici (-1861353487).
    // Papal guid EXISTS in GameDB as a non-NPC item, so it hits the dict
    // with the wrong table. Intercept at entry, before the lookup.
    {
        var gameDbT = Find(mod, "Database.GameDB");
        if (gameDbT == null) throw new Exception("no GameDB");
        var fm = gameDbT.Methods.First(mt => mt.Name == "FindGameIdFromGuid" && mt.Parameters.Count == 1 && mt.ReturnType.FullName.Contains("GameID"));
        var gameIdDef = Find(mod, "Database.GameID").Resolve();
        var dummyInt = mod.ImportReference(gameIdDef.Methods.First(mt => mt.Name == "CreateDummy" && mt.Parameters.Count == 1 && mt.Parameters[0].ParameterType.FullName == "System.Int32"));
        var strEq = mod.ImportReference(MkMethod(strType, "op_Equality", TS.Boolean, false, strType, strType));
        var fil = fm.Body.GetILProcessor();
        var ffirst = fm.Body.Instructions[0];
        var nextCheck = ffirst;
        var aliases = new (string guid, int hash)[]
        {
            ("520620a5-024d-4633-aaf8-21a5f0db7903", -1861353487), // GuardA -> enemy_papal_medici
            ("2d2ca21b-99e9-4a71-ad13-68dd5748574c", -1600799789), // GuardB -> enemy_guardcaptain_medici
            ("d91147f6-5386-4ba7-b5b2-0334051fac5b", -1847913466), // GuardC -> enemy_crossbowman_medici (unused guid, frees crowd[0])
        };
        foreach (var (g, h) in ((System.Collections.Generic.IEnumerable<(string guid, int hash)>)aliases).Reverse())
        {
            var la = fil.Create(OpCodes.Ldarg_1);
            fil.InsertBefore(nextCheck, la);
            fil.InsertBefore(nextCheck, fil.Create(OpCodes.Ldstr, g));
            fil.InsertBefore(nextCheck, fil.Create(OpCodes.Call, strEq));
            fil.InsertBefore(nextCheck, fil.Create(OpCodes.Brfalse, nextCheck));
            fil.InsertBefore(nextCheck, fil.Create(OpCodes.Ldc_I4, h));
            fil.InsertBefore(nextCheck, fil.Create(OpCodes.Call, dummyInt));
            fil.InsertBefore(nextCheck, fil.Create(OpCodes.Ret));
            nextCheck = la;
        }
        Console.WriteLine("aliased 3 mission guids to live enemies");
    }
    // OnDeserialized checkpoints retired. Dropped for clean build.
    // Lone NPCs have no MissionNpcFormation: GetComponent returns null and
    // get_EntityID throws, aborting the whole stats init (frozen guard).
    // Skip just the patrol-id set when formation is missing.
    {
        var nhT = Find(mod, "Assets.Scripts.Mission.Logic.Stats.NpcStatsHelper");
        var nm = nhT.Methods.First(mt => mt.Name == "InitializeMutable");
        var nil = nm.Body.GetILProcessor();
        var getComp = nm.Body.Instructions.First(i => i.OpCode == OpCodes.Callvirt && i.Operand.ToString().Contains("GetComponent"));
        var contT = getComp.Next;
        var setPatrol = nm.Body.Instructions.First(i => i.OpCode == OpCodes.Callvirt && i.Operand.ToString().Contains("Set_EntityId"));
        var skipT = (Mono.Cecil.Cil.Instruction)setPatrol.Next;
        nil.InsertBefore(contT, nil.Create(OpCodes.Dup));
        nil.InsertBefore(contT, nil.Create(OpCodes.Brtrue, contT));
        nil.InsertBefore(contT, nil.Create(OpCodes.Pop));
        nil.InsertBefore(contT, nil.Create(OpCodes.Br, skipT));
        Console.WriteLine("null-checked formation in NpcStatsHelper");
    }
    // Crowd: random guid picks that aren't live HumanoidDefs nulled the whole
    // SpawnCrowd loop (NRE at item.CharType aborted mission load -> 100% hang).
    // Skip bad picks instead: return null, caller ignores the list anyway.
    {
        var crowdT = Find(mod, "Assets.Scripts.Crowd.Crowd");
        var sm = crowdT.Methods.First(mt => mt.Name == "SpawnCrowdCharacter" && mt.Parameters.Count == 0);
        var sil = sm.Body.GetILProcessor();
        var gameIdDef = Find(mod, "Database.GameID").Resolve();
        var isValid = mod.ImportReference(gameIdDef.Methods.First(mt => mt.Name == "IsValid" && mt.IsStatic && mt.Parameters.Count == 1));
        Mono.Cecil.Cil.VariableDefinition LocalOf(Mono.Cecil.Cil.Instruction st)
        {
            if (st.Operand is Mono.Cecil.Cil.VariableDefinition vd) return vd;
            var code = st.OpCode.Code;
            int idx = (int)code - (int)Mono.Cecil.Cil.Code.Stloc_0;
            if (idx < 0 || idx > 3) throw new Exception("not an stloc");
            return sm.Body.Variables[idx];
        }
        void SkipIfBad(Mono.Cecil.Cil.Instruction call, bool useIsValid)
        {
            var st = call.Next;
            while (st != null && !(st.OpCode.Code >= Mono.Cecil.Cil.Code.Stloc_0 && st.OpCode.Code <= Mono.Cecil.Cil.Code.Stloc_S)) st = st.Next;
            if (st == null) throw new Exception("no stloc after call");
            var v = LocalOf(st);
            var cont = st.Next;
            if (useIsValid)
            {
                sil.InsertBefore(cont, sil.Create(OpCodes.Ldloc, v));
                sil.InsertBefore(cont, sil.Create(OpCodes.Call, isValid));
                sil.InsertBefore(cont, sil.Create(OpCodes.Brtrue, cont));
            }
            else
            {
                sil.InsertBefore(cont, sil.Create(OpCodes.Ldloc, v));
                sil.InsertBefore(cont, sil.Create(OpCodes.Brtrue, cont));
            }
            sil.InsertBefore(cont, sil.Create(OpCodes.Ldnull));
            sil.InsertBefore(cont, sil.Create(OpCodes.Ret));
        }
        var findCall = sm.Body.Instructions.First(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && i.Operand.ToString().Contains("FindGameIdFromGuid"));
        SkipIfBad(findCall, true);
        var getItem = sm.Body.Instructions.First(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && i.Operand.ToString().Contains("GetItem"));
        SkipIfBad(getItem, false);
        Console.WriteLine("null-skipped bad crowd picks");
    }
    // Crowd alive: GuidList had 10 entries but only 3 are live HumanoidDefs,
    // so most random picks died even with the skip (empty streets). Replace
    // with live guids only -> every spawn survives. Mercenary test: male
    // special_npc_mercenary_01 first so quality 0.7 pool (indices 0-2)
    // includes him. Keep InProgress crowd enabled (missions blank streets
    // by design, we want alive).
    // NOTE (male crowd): frozen GameDBs (DXT/ETC2/Generic, all identical)
    // define exactly 8 HumanoidDefs - 3 female crowd, mercenary, 2 courtesans,
    // 2 player classes. crowd_civilian_male_01/02/03 + rich_male_01/02 have
    // NO HumanoidDef entries (FBX/atlas source paths exist in GameDB strings,
    // meshes almost surely inside Italy_Male bundle proven present by the
    // walking mercenary, but SpawnCrowdCharacter's GetItem<HumanoidDef>
    // lookup has nothing to find). True male crowd needs GameDB surgery
    // (new entries) or the mission-NPC path (NpcData defs exist - open
    // experiment). Courtesans below are the recoverable variety win: live
    // defs, bodies almost surely in Italy_Female (present - females walk).
    {
        var csT = Find(mod, "Assets.Scripts.Crowd.CrowdSettings");
        var cctor = csT.Methods.First(mt => mt.Name == ".cctor");
        var cil = cctor.Body.GetILProcessor();
        var cRet = cctor.Body.Instructions.Last(i => i.OpCode == OpCodes.Ret);
        var guidField = csT.Fields.First(f => f.Name == "GuidList");
        var live = new[]
        {
            "58c1d7ef-4e26-4dca-8667-6aae65cee5e3", // special_npc_mercenary_01 (male)
            "15c667c9-4ec9-4044-84c7-965cf31ba7d7", // crowd_civilian_female_03
            "86d505ad-c3ba-43ac-a5b9-101fff12be84", // crowd_civilian_female_01
            "ddac503d-625f-486e-a56d-7c552f579cbf", // crowd_civilian_female_02
            "37aac035-5b61-4269-8214-15bffc0deef3", // special_npc_courtesan_01 (TEST - needs device verify)
            "a8936d0b-ead1-4953-99c1-9cb21cf525e3", // special_npc_courtesan_02 (TEST - needs device verify)
        };
        cil.InsertBefore(cRet, cil.Create(OpCodes.Ldc_I4, live.Length));
        cil.InsertBefore(cRet, cil.Create(OpCodes.Newarr, strType));
        for (int gi = 0; gi < live.Length; gi++)
        {
            cil.InsertBefore(cRet, cil.Create(OpCodes.Dup));
            cil.InsertBefore(cRet, gi switch { 0 => cil.Create(OpCodes.Ldc_I4_0), 1 => cil.Create(OpCodes.Ldc_I4_1), 2 => cil.Create(OpCodes.Ldc_I4_2), 3 => cil.Create(OpCodes.Ldc_I4_3), _ => cil.Create(OpCodes.Ldc_I4, gi) });
            cil.InsertBefore(cRet, cil.Create(OpCodes.Ldstr, live[gi]));
            cil.InsertBefore(cRet, cil.Create(OpCodes.Stelem_Ref));
        }
        cil.InsertBefore(cRet, cil.Create(OpCodes.Stsfld, guidField));
        Console.WriteLine("crowd GuidList -> mercenary + 3 females + 2 courtesans (TEST)");
        var crowdT2 = Find(mod, "Assets.Scripts.Crowd.Crowd");
        var omc = crowdT2.Methods.First(mt => mt.Name == "OnMissionChanged" && mt.Parameters.Count == 1);
        omc.Body.Instructions.Clear(); omc.Body.ExceptionHandlers.Clear();
        omc.Body.Variables.Clear(); omc.Body.InitLocals = false;
        var oil = omc.Body.GetILProcessor();
        oil.Emit(OpCodes.Ret);
        Console.WriteLine("crowd stays enabled InProgress");
    }
    // Spawn/lifecycle/startup entry traces retired. Dropped for clean build.
    // ---- P-V: ParcourHelper.CheckCivilianCover null guards (NRE-spam fix) ----
    // Crowd / diverted NPCs often have null Patrol, PatrolDefinition,
    // Definition, PatrolComponent or Formation. Each nulled the per-tick
    // cover prediction with an NRE (log spam, cover never found).
    // Return false (no cover) instead, matching the existing early-outs.
    // STACK-DEPTH TRAP (bit us once -> InvalidProgramException on device):
    // an early Ret is only valid with exactly [return-value] on the stack.
    // Two guarded calls sit INSIDE the CanApproachPatrol argument list with
    // [interaction, id] already pending, so their false-path must pop 3
    // (null result + 2 pending), not 1. Guards are depth-aware accordingly;
    // asserts fail loud at patch time if the compiler output ever changes.
    {
        var phT = Find(mod, "Assets.Scripts.Mission.Motion.ParcourHelper");
        var cov = phT.Methods.First(mt => mt.Name == "CheckCivilianCover" && mt.Parameters.Count == 5);
        var cil = cov.Body.GetILProcessor();
        var deref = new HashSet<string> { "get_Patrol", "get_PatrolDefinition", "get_Definition", "get_PatrolComponent", "get_Formation" };
        var insns = cov.Body.Instructions.ToList();
        int winStart = insns.FindIndex(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && i.Operand is MethodReference m1 && m1.Name == "get_AssassinInteraction");
        int winEnd = insns.FindIndex(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && i.Operand is MethodReference m2 && m2.Name == "CanApproachPatrol");
        if (winStart < 0 || winEnd < 0 || winEnd < winStart) throw new Exception("P-V: approach window not found");
        var inside = new HashSet<Mono.Cecil.Cil.Instruction>(insns.Skip(winStart + 1).Take(winEnd - winStart - 1));
        // no outside branch may land inside the window (would break depth 0 at entry)
        foreach (var i in insns)
        {
            if (i.Operand is Mono.Cecil.Cil.Instruction t && inside.Contains(t) && !inside.Contains(i))
                throw new Exception("P-V: outside branch into approach window");
            if (i.Operand is Mono.Cecil.Cil.Instruction[] ts && ts.Any(t => inside.Contains(t)))
                throw new Exception("P-V: outside switch into approach window");
        }
        int n = 0, nDeep = 0;
        foreach (var ins in insns)
        {
            if ((ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt) && ins.Operand is MethodReference mr && deref.Contains(mr.Name))
            {
                if (mr.Name == "get_Patrol" && !mr.DeclaringType.FullName.Contains("NpcCharacter")) continue;
                bool deep = inside.Contains(ins);
                if (deep && !(mr.Name == "get_PatrolDefinition" || mr.Name == "get_Definition"))
                    throw new Exception("P-V: unexpected nested deref " + mr.Name);
                var cont = ins.Next;
                if (cont == null) throw new Exception("P-V: deref is last instr");
                cil.InsertBefore(cont, cil.Create(OpCodes.Dup));
                cil.InsertBefore(cont, cil.Create(OpCodes.Brtrue_S, cont));
                cil.InsertBefore(cont, cil.Create(OpCodes.Pop)); // null result
                if (deep)
                {
                    cil.InsertBefore(cont, cil.Create(OpCodes.Pop)); // pending id
                    cil.InsertBefore(cont, cil.Create(OpCodes.Pop)); // pending interaction
                    nDeep++;
                }
                cil.InsertBefore(cont, cil.Create(OpCodes.Ldc_I4_0));
                cil.InsertBefore(cont, cil.Create(OpCodes.Ret));
                n++;
            }
        }
        if (n != 7 || nDeep != 2) throw new Exception($"P-V: expected 7 guards / 2 nested, got {n} / {nDeep}");
        Console.WriteLine($"parcour cover null-guards: {n} ({nDeep} nested)");
        // public CheckCivilianCovers: null NavMeshAgent -> Invalid (0), skip prediction
        var covs = phT.Methods.First(mt => mt.Name == "CheckCivilianCovers");
        var cil2 = covs.Body.GetILProcessor();
        var f0 = covs.Body.Instructions[0];
        var cont2 = cil2.Create(OpCodes.Nop);
        cil2.InsertBefore(f0, cil2.Create(OpCodes.Ldarg_1));
        cil2.InsertBefore(f0, cil2.Create(OpCodes.Brtrue_S, cont2));
        cil2.InsertBefore(f0, cil2.Create(OpCodes.Ldc_I4_0));
        cil2.InsertBefore(f0, cil2.Create(OpCodes.Ret));
        cil2.InsertBefore(f0, cont2);
        Console.WriteLine("parcour covers null-agent guard");
    }
    // ---- P-W: OptionsMenuData.UpdateMissionTracker empty-mission guard ----
    // Custom missions ship no objectives: CollectedObjectiveData is an empty
    // BBList, so Last()/First() NRE on every options refresh (live in
    // boot.log). Wrap the UI-only method in a swallow-guard: any failure
    // just skips the tracker refresh. Same handler shape as TraceOffline.
    {
        var omdT = Find(mod, "Assets.Scripts.UI.OptionsMenuData");
        var um = omdT.Methods.First(mt => mt.Name == "UpdateMissionTracker" && !mt.HasParameters);
        var uil = um.Body.GetILProcessor();
        var first = um.Body.Instructions[0];
        var end = uil.Create(OpCodes.Ret);
        uil.Append(end);
        var hPop = uil.Create(OpCodes.Pop);
        uil.Append(hPop);
        uil.Append(uil.Create(OpCodes.Leave, end));
        foreach (var ins in um.Body.Instructions.ToList())
        {
            if (ins.OpCode == OpCodes.Ret && ins != end) { ins.OpCode = OpCodes.Leave; ins.Operand = end; }
        }
        um.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = first, TryEnd = hPop, HandlerStart = hPop, HandlerEnd = end, CatchType = excType
        });
        Console.WriteLine("tracker empty-mission guard");
    }

    asm.Write(outDll);
    Console.WriteLine("WROTE " + outDll);

    // ---- P-F: crash logger (managed exceptions + Unity logs) in SharedBaseLib ----
    {
        string sharedDll = Path.Combine(managedDir, "SharedBaseLib.dll");
        string sharedOut = Path.Combine(Path.GetDirectoryName(outDll), "SharedBaseLib.dll");
        var sasm = AssemblyDefinition.ReadAssembly(sharedDll, new ReaderParameters { AssemblyResolver = resolver });
        var mod2 = sasm.MainModule;
        var ue = AssemblyDefinition.ReadAssembly(Path.Combine(managedDir, "UnityEngine.dll"), new ReaderParameters { AssemblyResolver = resolver });
        var TS2 = mod2.TypeSystem;
        var str2 = TS2.String; var void2 = TS2.Void; var obj2 = TS2.Object;
        var exc2 = new TypeReference("System", "Exception", mod2, TS2.Corlib);
        var file2 = new TypeReference("System.IO", "File", mod2, TS2.Corlib);
        var intptrT = new TypeReference("System", "IntPtr", mod2, TS2.Corlib);
        MethodReference Mk2(TypeReference decl, string name, TypeReference ret, bool hasThis, params TypeReference[] ps)
        {
            var mr = new MethodReference(name, ret, decl) { HasThis = hasThis };
            foreach (var p in ps) mr.Parameters.Add(new ParameterDefinition(p));
            return mod2.ImportReference(mr);
        }
        var append2 = Mk2(file2, "AppendAllText", void2, false, str2, str2);
        var string2 = new TypeReference("System", "String", mod2, TS2.Corlib);
        var concat2 = Mk2(string2, "Concat", str2, false, str2, str2);
        var appDomT = new TypeReference("System", "AppDomain", mod2, TS2.Corlib);
        var getCurDom = Mk2(appDomT, "get_CurrentDomain", appDomT, false);
        var uhehT = new TypeReference("System", "UnhandledExceptionEventHandler", mod2, TS2.Corlib);
        var uhehCtor = Mk2(uhehT, ".ctor", void2, true, obj2, intptrT);
        var addUEH = Mk2(appDomT, "add_UnhandledException", void2, true, uhehT);
        var ueaT = new TypeReference("System", "UnhandledExceptionEventArgs", mod2, TS2.Corlib);
        var getExObj = Mk2(ueaT, "get_ExceptionObject", obj2, true);
        var objToStr = Mk2(obj2, "ToString", str2, true);
        var appT = mod2.ImportReference(Find(ue.MainModule, "UnityEngine.Application"));
        var logCbT = mod2.ImportReference(Find(ue.MainModule, "UnityEngine.Application/LogCallback"));
        var logTypeT = mod2.ImportReference(Find(ue.MainModule, "UnityEngine.LogType"));
        var appDef = Find(ue.MainModule, "UnityEngine.Application");
        var addLog = mod2.ImportReference(appDef.Methods.First(m => m.Name == "add_logMessageReceivedThreaded"));
        var logCbCtor = mod2.ImportReference(Find(ue.MainModule, "UnityEngine.Application/LogCallback").Methods.First(m => m.IsConstructor));
        var sharedMain = Find(mod2, "SharedMain");

        var traceCrash = new MethodDefinition("TraceCrash", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, void2);
        traceCrash.Parameters.Add(new ParameterDefinition(str2));
        sharedMain.Methods.Add(traceCrash);
        {
            var il = traceCrash.Body.GetILProcessor();
            traceCrash.Body.InitLocals = true;
            var end = il.Create(OpCodes.Ret);
            var t0 = il.Create(OpCodes.Ldstr, TracePath);
            il.Append(t0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, append2);
            il.Emit(OpCodes.Leave, end);
            var h0 = il.Create(OpCodes.Pop);
            il.Append(h0);
            il.Emit(OpCodes.Leave, end);
            il.Append(end);
            traceCrash.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
            { TryStart = t0, TryEnd = h0, HandlerStart = h0, HandlerEnd = end, CatchType = exc2 });
        }
        var traceCrashRef = mod2.ImportReference(traceCrash);

        var onUnhandled = new MethodDefinition("OnUnhandled", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, void2);
        onUnhandled.Parameters.Add(new ParameterDefinition(obj2));
        onUnhandled.Parameters.Add(new ParameterDefinition(ueaT));
        sharedMain.Methods.Add(onUnhandled);
        {
            var il = onUnhandled.Body.GetILProcessor();
            il.Emit(OpCodes.Ldstr, "[FATAL] ");
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, getExObj);
            il.Emit(OpCodes.Callvirt, objToStr);
            il.Emit(OpCodes.Call, concat2);
            il.Emit(OpCodes.Ldstr, "\n");
            il.Emit(OpCodes.Call, concat2);
            il.Emit(OpCodes.Call, traceCrashRef);
            il.Emit(OpCodes.Ret);
        }

        var onLog = new MethodDefinition("OnLog", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, void2);
        onLog.Parameters.Add(new ParameterDefinition(str2));
        onLog.Parameters.Add(new ParameterDefinition(str2));
        onLog.Parameters.Add(new ParameterDefinition(logTypeT));
        sharedMain.Methods.Add(onLog);
        {
            var il = onLog.Body.GetILProcessor();
            il.Emit(OpCodes.Ldstr, "[LOG] ");
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, concat2);
            il.Emit(OpCodes.Ldstr, "\n");
            il.Emit(OpCodes.Call, concat2);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, concat2);
            il.Emit(OpCodes.Ldstr, "\n");
            il.Emit(OpCodes.Call, concat2);
            il.Emit(OpCodes.Call, traceCrashRef);
            il.Emit(OpCodes.Ret);
        }

        var initLog = new MethodDefinition("InitCrashLog", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, void2);
        sharedMain.Methods.Add(initLog);
        {
            var il = initLog.Body.GetILProcessor();
            initLog.Body.InitLocals = true;
            // marker first (proves hook ran + file writable)
            il.Emit(OpCodes.Ldstr, TracePath);
            il.Emit(OpCodes.Ldstr, "[boot] InitCrashLog\n");
            il.Emit(OpCodes.Call, append2);
            // sub-try 1: AppDomain handler. Normal flow MUST leave (never fall into h1).
            var end = il.Create(OpCodes.Ret);
            var t1 = il.Create(OpCodes.Call, getCurDom);
            il.Append(t1);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldftn, mod2.ImportReference(onUnhandled));
            il.Emit(OpCodes.Newobj, uhehCtor);
            il.Emit(OpCodes.Callvirt, addUEH);
            var after1 = il.Create(OpCodes.Ldnull); // == t2, appended below
            il.Emit(OpCodes.Leave, after1);
            var h1 = il.Create(OpCodes.Pop);
            il.Append(h1);
            il.Emit(OpCodes.Leave, after1);
            initLog.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
            { TryStart = t1, TryEnd = after1, HandlerStart = h1, HandlerEnd = after1, CatchType = exc2 });
            // sub-try 2: Unity log handler (threaded = fires on any thread)
            il.Append(after1);
            il.Emit(OpCodes.Ldftn, mod2.ImportReference(onLog));
            il.Emit(OpCodes.Newobj, logCbCtor);
            il.Emit(OpCodes.Call, addLog);
            il.Emit(OpCodes.Leave, end);
            var h2 = il.Create(OpCodes.Pop);
            il.Append(h2);
            il.Emit(OpCodes.Leave, end);
            il.Append(end);
            initLog.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
            { TryStart = after1, TryEnd = h2, HandlerStart = h2, HandlerEnd = end, CatchType = exc2 });
        }
        var initLogRef = mod2.ImportReference(initLog);
        var awake = sharedMain.Methods.First(m => m.Name == "Awake" && !m.HasParameters);
        {
            var il = awake.Body.GetILProcessor();
            il.InsertBefore(awake.Body.Instructions[0], il.Create(OpCodes.Call, initLogRef));
        }
        // P-G2: SharedBaseLib-internal traces via same-module TraceCrash
        {
            var crashRef2 = mod2.ImportReference(sharedMain.Methods.First(m => m.Name == "TraceCrash"));
            void TraceEntry2(TypeDefinition t, string method)
            {
                var ms = t.Methods.Where(x => x.Name == method && x.HasBody).ToList();
                foreach (var mm in ms)
                {
                    var il = mm.Body.GetILProcessor();
                    var first = mm.Body.Instructions[0];
                    var a = il.Create(OpCodes.Ldstr, $"[boot] enter {t.Name}.{mm.Name}\n");
                    var b = il.Create(OpCodes.Call, crashRef2);
                    il.InsertBefore(first, a);
                    il.InsertBefore(first, b);
                    Console.WriteLine($"traced2 {t.Name}.{mm.Name}");
                }
            }
            // P-M retired: pre-seed restores exact 2018 entry types; keep original routing.
            var mgrT = Find(mod2, "SharedBaseLib.System.Cache.Manager");
            // P-O: rev-agnostic lookup (seed catalogue uses rev 0)
            {
                var m = mgrT.Methods.First(x => x.Name == "GetEntryUnsafe");
                var list = m.Body.Instructions.ToList();
                int n = 0;
                for (int ii = 0; ii < list.Count; ii++)
                {
                    var ins = list[ii];
                    // force captured _revision to 0 (ldarg.2 -> ldc.i4.0 before stfld _revision)
                    if (ins.OpCode == OpCodes.Stfld && ins.Operand is FieldReference sfr && sfr.Name == "_revision")
                    {
                        if (ii == 0) throw new Exception("P-O no prev");
                        var prev = list[ii - 1];
                        if (prev.OpCode == OpCodes.Ldarg_2) { prev.OpCode = OpCodes.Ldc_I4_0; prev.Operand = null; n++; }
                    }
                }
                Console.WriteLine($"patched GetEntryUnsafe callsites: {n}");
            }
            // Per-file/per-entry traces retired. Dropped for clean build.
            // Crash hook (InitCrashLog/OnLog) stays: silent unless a real error fires.
        }
        sasm.Write(sharedOut);
        Console.WriteLine("WROTE " + sharedOut);
    }
}
