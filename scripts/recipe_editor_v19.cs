// Dedicated qualification executable; deliberately separate from Host/Worker capabilities.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HmiUnified.UI.Shapes;
using Siemens.Engineering.HmiUnified.UI.Widgets;
using Siemens.Engineering.HmiUnified.UI.Features;
using Siemens.Engineering.HmiUnified.UI.Enum;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Script;
using Siemens.Engineering.Compiler;
using System.IO.Compression;

internal static class RecipeEditorV19
{
    const string Vendor = @"C:\Program Files\Siemens\Automation\Portal V19";
    const string Target = @"C:\RMA\TIA_TEST\T05_RecipeSim_V19\T05_RecipeSim_V19.ap19";
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 64000000 };
    static string Output;
    static bool MutationStarted;
    static int TreeNodes;
    static string Gate;
    static void Write(string name, object value) { File.WriteAllText(Path.Combine(Output,name), Json.Serialize(value)); }
    static void Phase(string s) { Console.WriteLine(DateTime.UtcNow.ToString("o")+" "+s); Console.Out.Flush(); }
    static Assembly Resolve(object sender, ResolveEventArgs e) {
        string n = new AssemblyName(e.Name).Name+".dll";
        foreach (string dir in new[]{@"PublicAPI\V19",@"Bin\PublicAPI","Bin"}) {
            string p=Path.Combine(Vendor,dir,n); if(File.Exists(p)) return Assembly.LoadFrom(p);
        } return null;
    }
    [STAThread] static int Main(string[] args) {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        try { return Run(args); } catch(Exception e) {
            if(Output!=null) Write("failure.json",new{status=MutationStarted?"RECONCILIATION_REQUIRED":"BLOCKED",error=e.ToString(),retry=false});
            Console.Error.WriteLine(e); return 2;
        }
    }
    static bool Same(string a,string b) { return String.Equals(Path.GetFullPath(a),Path.GetFullPath(b),StringComparison.OrdinalIgnoreCase); }
    static int Run(string[] a) {
        if(a.Length<2||!new[]{"processes","archive","inspect","configure","metadata","plan","retrieve","open","verify","compile-policy-tests"}.Contains(a[0])) throw new ArgumentException("See scripts/recipe_editor_v19.md for the bounded qualification commands");
        string requested=Path.GetFullPath(a[1]); if(Directory.Exists(requested)||File.Exists(requested)) throw new Exception("OUTPUT_EXISTS");
        Directory.CreateDirectory(requested);Output=requested;
        if(a[0]=="compile-policy-tests"){PolicyTests();Write("policy-tests.json",new{status="PASS",cases=6});return 0;}
        if(a[0]=="metadata") {
            Write("metadata.json",new[]{typeof(HmiIOField),typeof(HmiScreen),typeof(HmiButton),typeof(HmiText),typeof(IHmiBoxFeature)}.Select(t=>new{type=t.FullName,properties=t.GetProperties().Select(p=>new{name=p.Name,type=p.PropertyType.FullName,write=p.CanWrite}).ToArray()})); return 0;
        }
        if(a[0]=="plan") {ValidatePlan();Write("plan.json",new{controls=Plan(),events=EventPlan(),serialized_text=Plan().Where(c=>c.Kind!="input").ToDictionary(c=>c.Name,c=>Rich(c.Text))});return 0;}
        if(a[0]=="processes") { Write("processes.json",TiaPortal.GetProcesses().Select(p=>new{pid=p.Id,path=p.ProjectPath==null?null:p.ProjectPath.FullName}).ToArray());return 0; }
        if(a[0]=="retrieve") {
            string archive=Path.GetFullPath(a[2]), root=Path.GetFullPath(a[3]);
            if(!root.StartsWith(@"X:\T05_EditorLab\",StringComparison.OrdinalIgnoreCase)||Directory.Exists(root))throw new Exception("NEW_LAB_ROOT_REQUIRED");
            VerifyArchive(archive);Directory.CreateDirectory(root);
            using(var own=new TiaPortal(TiaPortalMode.WithoutUserInterface)) {
                Phase("Retrieve isolated lab copy");var copy=own.Projects.Retrieve(new FileInfo(archive),new DirectoryInfo(root));
                Write("retrieved.json",new{path=copy.Path.FullName,pid=own.GetCurrentProcess().Id,isModified=copy.IsModified});
                copy.Close();
            } return 0;
        }
        if(a[0]=="open") {
            string lab=Path.GetFullPath(a[2]); if(!lab.StartsWith(@"X:\T05_EditorLab\",StringComparison.OrdinalIgnoreCase))throw new Exception("LAB_ONLY");
            using(var own=new TiaPortal(TiaPortalMode.WithoutUserInterface)) {
                var p=own.Projects.Open(new FileInfo(lab));Write("opened.json",new{path=p.Path.FullName,pid=own.GetCurrentProcess().Id,isModified=p.IsModified});
                Phase("Lab open; waiting for explicit stdin close, no automatic Save"); Console.ReadLine();p.Close();
            } return 0;
        }
        string path=Path.GetFullPath(a[2]);
        if(a[0]=="archive" && !Same(path,Target)) throw new Exception("SOURCE_NOT_EXACT_TARGET");
        if(a[0]=="configure" && (Same(path,Target) || !path.StartsWith(@"X:\T05_EditorLab\",StringComparison.OrdinalIgnoreCase))) throw new Exception("LAB_ONLY");
        var matches=TiaPortal.GetProcesses().Where(p=>p.ProjectPath!=null&&Same(p.ProjectPath.FullName,path)).ToArray();
        if(matches.Length!=1) throw new Exception("EXACT_OPEN_PROJECT_NOT_UNIQUE:"+matches.Length);
        Phase("Attach "+matches[0].Id);
        using(var tia=matches[0].Attach()) {
            if(tia.Projects.Count!=1) throw new Exception("PROJECT_COUNT");
            var project=tia.Projects[0]; if(!Same(project.Path.FullName,path)) throw new Exception("PROJECT_IDENTITY");
            var hmi=Hmi(project); var screen=hmi.Screens.Find("T05_Recipes_Editor");
            if(screen==null||screen.ScreenNumber!=9601) throw new Exception("SCREEN_IDENTITY");
            Write("before.json",Snapshot(project,hmi,screen,tia));
            if(a[0]=="inspect"||a[0]=="verify") {
                foreach(var m in hmi.Scripts){var dir=Path.Combine(Output,"modules",m.Name);Directory.CreateDirectory(dir);m.Export(new DirectoryInfo(dir));}
                if(a[0]=="verify") {
                    if(a.Length!=4||project.IsModified||!Matches(screen))throw new Exception("INDEPENDENT_VERIFY_FAILED");
                    var previous=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(a[3],"result.json")));
                    if((string)previous["status"]!="CONFIGURED"||File.ReadAllText(Path.Combine(a[3],"after.json"))!=Json.Serialize(Snapshot(project,hmi,screen,tia)))throw new Exception("INDEPENDENT_STATE_MISMATCH");
                    string gate=GatePath(path);var pending=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(gate));
                    if((string)pending["outcome"]!="SAVED_NEEDS_READBACK"||!Same((string)pending["evidence"],a[3]))throw new Exception("GATE_BINDING_MISMATCH");
                    File.Move(gate,Path.Combine(Output,"closed-intent.json"));Write("verification.json",new{status="PASS",isModified=false,configurationMatches=true});
                }
                return 0;
            }
            if(project.IsModified) throw new Exception("DIRTY_REVIEW_REQUIRED");
            using(var access=tia.ExclusiveAccess("Recipe Editor V19 isolated qualification")) {
                if(project.IsModified || !Same(project.Path.FullName,path)) throw new Exception("PRECONDITION_CHANGED");
                if(a[0]=="archive") {
                    Phase("Archive source (read-only)");
                    project.Archive(new DirectoryInfo(Output),project.Name+".zap19",ProjectArchivationMode.Compressed);
                    VerifyArchive(Path.Combine(Output,project.Name+".zap19"));
                    Write("after.json",Snapshot(project,hmi,screen,tia));return 0;
                }
                if(a[0]!="configure"||a.Length!=5)throw new Exception("CONFIGURE_EXPECTED_SNAPSHOT_AND_NATIVE_DEPENDENCIES_REQUIRED");
                Gate=GatePath(path);if(File.Exists(Gate))throw new Exception("RECONCILIATION_REQUIRED_EXISTING_INTENT");
                string expected=File.ReadAllText(a[3]);
                if(Json.Serialize(Snapshot(project,hmi,screen,tia))!=expected)throw new Exception("STALE_EXPECTED_STATE");
                foreach(string name in new[]{"T05RecipeCatalog","T05RecipeEditorReadonly"}) {
                    var m=hmi.Scripts.SingleOrDefault(x=>x.Name==name);if(m==null)throw new Exception("JS_DEPENDENCY_MISSING:"+name);
                    var dir=Path.Combine(Output,"dependency-preflight",name);Directory.CreateDirectory(dir);m.Export(new DirectoryInfo(dir));
                    foreach(string ext in new[]{".hmi.js",".hmi.yml"})if(Hash(Path.Combine(dir,name+ext))!=Hash(Path.Combine(a[4],name+ext)))throw new Exception("JS_DEPENDENCY_NOT_SYNCHRONIZED:"+name+ext);
                }
                Configure(project,hmi,screen,tia,()=>access.IsCancellationRequested);
            }
        }
        return 0;
    }
    static string Hash(string p) {using(var s=File.OpenRead(p))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
    static string GatePath(string path) {
        string key;using(var h=SHA256.Create())key=BitConverter.ToString(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()))).Replace("-","");
        string dir=@"X:\T05_EditorLab\state";Directory.CreateDirectory(dir);return Path.Combine(dir,key+".json");
    }
    static void VerifyArchive(string path) {
        using(var z=ZipFile.OpenRead(path)) {
            var ap=z.Entries.Where(e=>e.Name.EndsWith(".ap19",StringComparison.OrdinalIgnoreCase)).ToArray();
            if(ap.Length!=1||ap[0].Name!="T05_RecipeSim_V19.ap19")throw new Exception("ARCHIVE_PROJECT_MISMATCH");
            foreach(var e in z.Entries)using(var s=e.Open()){byte[] b=new byte[65536];while(s.Read(b,0,b.Length)>0){}}
            // CRC is additionally checked by the qualification driver using Python ZipFile.testzip.
            Write("archive.json",new{path=path,sha256=Hash(path),entries=z.Entries.Count,ap19=ap[0].FullName,streamsRead=true,crc="DRIVER_REQUIRED"});
        }
        var psi=new System.Diagnostics.ProcessStartInfo("py.exe","-3 -c \"import sys,zipfile; z=zipfile.ZipFile(sys.argv[1]); assert z.testzip() is None\" \""+path+"\""){UseShellExecute=false,CreateNoWindow=true};
        using(var check=System.Diagnostics.Process.Start(psi)){check.WaitForExit();if(check.ExitCode!=0)throw new Exception("ARCHIVE_CRC_FAILED");}
        Write("archive-verified.json",new{path=path,sha256=Hash(path),crc="PASS",project="T05_RecipeSim_V19"});
    }
    sealed class Control {
        public string Name,Kind,Text;public int X,Y;public uint W,H;public bool Visible=true;
    }
    static readonly string[] Inputs={"camera_job","motion_start_angle","motion_end_angle","start_angle","fall_angle","rise_distance","fall_distance","power","velocity_mm_s","gas_type","gas_flow","keyence_program","trajectory_offset_z","trajectory_offset_r","trajectory_offset_focus","post_flow_time_s","tank_fast_fill_time_s","tank_working_flow_l_min","ProfileChoice"};
    static readonly string[] Buttons={"ChooseProfile","Validate","SaveAsNew","Discard","ApplyChanges","DiscardChanges"};
    static readonly string[] Actions={"chooseTechnology","validate","saveAsNew","discardDraft","applyChanges","discardChanges"};
    static readonly string[] Labels={"Wybierz profil","Waliduj draft","Zapisz jako nowy - przygotuj","Odrzuc caly draft wraz z wpisami","Zastosuj zmiany","Odrzuc wpisane zmiany"};
    static List<Control> Plan() {
        var r=new List<Control>();
        for(int i=0;i<Inputs.Length;i++)r.Add(new Control{Name="EDIT_"+Inputs[i],Kind="input",Text=Inputs[i],X=1285+(i%2)*290,Y=195+(i/2)*53,W=280,H=43});
        r.Add(new Control{Name="EDIT_Context",Kind="text",Text="",X=0,Y=0,W=1,H=1,Visible=false});
        r.Add(new Control{Name="EDIT_Profile",Kind="text",Text="Profil",X=645,Y=540,W=570,H=40});
        r.Add(new Control{Name="EDIT_Candidates",Kind="text",Text="Kandydaci",X=645,Y=585,W=570,H=45});
        r.Add(new Control{Name="EDIT_Missing",Kind="text",Text="Brakujace parametry",X=45,Y=790,W=530,H=150});
        r.Add(new Control{Name="EDIT_Dirty",Kind="text",Text="Stan draftu i wpisow",X=645,Y=860,W=570,H=35});
        for(int i=0;i<Buttons.Length;i++)r.Add(new Control{Name="EDIT_"+Buttons[i],Kind="button",Text=Labels[i],X=645+(i%2)*290,Y=650+(i/2)*65,W=280,H=55});
        return r;
    }
    static void ValidatePlan() {
        var p=Plan();if(p.Count!=30||p.Select(x=>x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=30||EventPlan().Count!=11)throw new Exception("INVALID_PLAN_COUNT");
        foreach(var x in p)if(x.X<0||x.Y<0||x.X+x.W>1920||x.Y+x.H>980)throw new Exception("OUT_OF_BOUNDS:"+x.Name);
        foreach(var x in p.Where(x=>x.Visible))foreach(var y in p.Where(y=>y.Visible&&String.CompareOrdinal(x.Name,y.Name)<0))
            if(x.X<y.X+y.W&&y.X<x.X+x.W&&x.Y<y.Y+y.H&&y.Y<x.Y+x.H)throw new Exception("OVERLAPPING_CONTROLS:"+x.Name+":"+y.Name);
    }
    static Dictionary<string,string> EventPlan() {
        var d=new Dictionary<string,string>{{"T05_Recipes_Editor/Loaded","load"},{"T05_Recipes_Editor/Unloaded","unload"},{"SELECT_Group/Tapped","prevNozzle"},{"SELECT_Nozzle/Tapped","nextNozzle"},{"SELECT_Seam/Tapped","seam"}};
        for(int i=0;i<Buttons.Length;i++)d.Add("EDIT_"+Buttons[i]+"/Tapped",Actions[i]);return d;
    }
    const string Global="import * as RecipeEditor from \"T05RecipeEditorReadonly\";";
    static string Code(string action) {return "await RecipeEditor.HandleRecipeEditor({type:\"formEvent\", screen:Screen, event:\""+action+"\"});";}
    static string Rich(string s) {return s.Length==0?"<body><p/></body>":"<body><p>"+System.Security.SecurityElement.Escape(s)+"</p></body>";}
    static int writes,created,eventsChanged;
    static void Set(IEngineeringObject o,string name,object v) {
        var old=o.GetAttribute(name);if(Object.Equals(old,v))return;
        MutationStarted=true;o.SetAttribute(name,v);writes++;
    }
    static void Text(MultilingualText t,string value) {
        // Preserve all configured languages; the English project language is the allowlisted label target.
        var rows=t.Items.Where(x=>x.Language.Culture.Name=="en-US").ToArray();if(rows.Length!=1)throw new Exception("EN_US_TEXT_REQUIRED");
        string rich=Rich(value);if(rows[0].Text!=rich){MutationStarted=true;rows[0].Text=rich;writes++;}
    }
    static void SetScript(IHmiScript script,string action) {
        string c=Code(action);if(script.Async&&script.ScriptCode==c&&script.GlobalDefinitionAreaScriptCode==Global)return;
        MutationStarted=true;
        if(!script.Async){script.Async=true;writes++;}
        if(script.ScriptCode!=c){script.ScriptCode=c;writes++;}
        if(script.GlobalDefinitionAreaScriptCode!=Global){script.GlobalDefinitionAreaScriptCode=Global;writes++;}
        eventsChanged++;
    }
    static void Configure(Project p,HmiSoftware h,HmiScreen s,TiaPortal tia,Func<bool> cancelled) {
        ValidatePlan();
        var plan=Plan();var names=s.ScreenItems.Select(i=>i.Name).ToArray();
        if(names.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=names.Length)throw new Exception("DUPLICATE_NAMES");
        if(s.Width!=1920||s.Height!=1080)throw new Exception("UNQUALIFIED_SCREEN_SIZE");
        foreach(var spec in plan) {
            if(names.Any(n=>String.Equals(n,spec.Name,StringComparison.OrdinalIgnoreCase)&&n!=spec.Name))throw new Exception("CONTROL_NAME_CASE_CONFLICT:"+spec.Name);
            var item=s.ScreenItems.Find(spec.Name);if(item==null)continue;
            if(spec.Kind=="input"&&!(item is HmiIOField)||spec.Kind=="text"&&!(item is HmiText)||spec.Kind=="button"&&!(item is HmiButton))throw new Exception("TYPE_CONFLICT:"+spec.Name);
            if(item.Dynamizations.Count!=0||item.PropertyEventHandlers.Count!=0)throw new Exception("UNKNOWN_BINDINGS:"+spec.Name);
        }
        foreach(string name in new[]{"SELECT_Group","SELECT_Nozzle","SELECT_Seam","SAVE_Draft"})if(!(s.ScreenItems.Find(name) is HmiButton))throw new Exception("EXISTING_BUTTON_REQUIRED:"+name);
        foreach(string name in new[]{"TankTechnologyFields","WeldField_0","WeldField_1","WeldField_2","WeldField_3","WeldField_4","WeldField_5","WeldField_6","WeldField_7","Offsets_Body","Offsets_Header","Offsets_Title","OffsetFields","DraftHint"})if(s.ScreenItems.Find(name)==null)throw new Exception("EXISTING_ITEM_REQUIRED:"+name);
        var compiler=Compiler(h); // Resolve before the first mutation.
        Write("plan.json",new{controls=plan,events=EventPlan(),preserve="All objects retained; navigation handlers untouched; explicit preview/layout patches only"});
        // A clean repeat performs no archive, compile or save. Read only property comparison first.
        bool identical=Matches(s);
        if(identical){Write("result.json",new{status="NO_OP",created=0,eventsChanged=0,writes=0,compile="NOT_RUN",save="NOT_RUN",isModified=p.IsModified});return;}
        Phase("Archive lab before first mutation");
        p.Archive(new DirectoryInfo(Output),p.Name+".zap19",ProjectArchivationMode.Compressed);VerifyArchive(Path.Combine(Output,p.Name+".zap19"));
        if(p.IsModified||cancelled())throw new Exception("CHANGED_OR_CANCELLED_DURING_BACKUP");
        if(File.ReadAllText(Path.Combine(Output,"before.json"))!=Json.Serialize(Snapshot(p,h,s,tia)))throw new Exception("STATE_CHANGED_DURING_BACKUP");
        using(var gate=new FileStream(Gate,FileMode.CreateNew,FileAccess.Write,FileShare.None)){var b=System.Text.Encoding.UTF8.GetBytes(Json.Serialize(new{outcome="INTENT",project=p.Path.FullName,evidence=Output}));gate.Write(b,0,b.Length);gate.Flush(true);}
        foreach(var spec in plan) {
            if(cancelled())throw new Exception("CANCELLED_NO_RETRY");
            var item=s.ScreenItems.Find(spec.Name);
            if(item==null){MutationStarted=true;item=spec.Kind=="input"?(HmiScreenItemBase)s.ScreenItems.Create<HmiIOField>(spec.Name):spec.Kind=="text"?(HmiScreenItemBase)s.ScreenItems.Create<HmiText>(spec.Name):s.ScreenItems.Create<HmiButton>(spec.Name);created++;}
            Set(item,"Left",spec.X);Set(item,"Top",spec.Y);Set(item,"Width",spec.W);Set(item,"Height",spec.H);Set(item,"Visible",spec.Visible);Set(item,"Enabled",false);
            var input=item as HmiIOField;
            if(input!=null){Set(input,"IOFieldType",HmiIOFieldType.InputOutput);Set(input,"OutputFormat","{S}");Set(input.InputBehavior,"AcceptOnDeactivated",true);Text(input.ToolTipText,spec.Text);}
            var text=item as HmiText;if(text!=null)Text(text.Text,spec.Text);
            var button=item as HmiButton;if(button!=null)Text(button.Text,spec.Text);
        }
        foreach(var entry in EventPlan()) {
            if(cancelled())throw new Exception("CANCELLED_NO_RETRY");
            var parts=entry.Key.Split('/');
            if(parts[0]==s.Name){var type=(HmiScreenEventType)Enum.Parse(typeof(HmiScreenEventType),parts[1]);var e=s.EventHandlers.Find(type);if(e==null){MutationStarted=true;e=s.EventHandlers.Create(type);}SetScript(e.Script,entry.Value);}
            else {var b=(HmiButton)s.ScreenItems.Find(parts[0]);var e=b.EventHandlers.Find(HmiButtonEventType.Tapped);if(e==null){MutationStarted=true;e=b.EventHandlers.Create(HmiButtonEventType.Tapped);}SetScript(e.Script,entry.Value);}
        }
        ApplyPreview(s);
        if(!Matches(s)){Write("after-mismatch.json",Snapshot(p,h,s,tia));throw new Exception("CONFIGURATION_READBACK_MISMATCH");}
        Write("after-mutation.json",Snapshot(p,h,s,tia));Phase("HMI Compile");
        var result=compiler.Compile();var diagnostics=new List<Diagnostic>();foreach(var m in result.Messages)Flatten(m,diagnostics,0);
        bool accepted=AcceptCompile(result.State.ToString(),result.ErrorCount,result.WarningCount,diagnostics);
        Write("compile.json",new{state=result.State.ToString(),errors=result.ErrorCount,warnings=result.WarningCount,messages=diagnostics,accepted=accepted,policy="ALLOW_ONLY_KNOWN_ANONYMOUS_RUNTIME_USER_WARNING"});
        if(!accepted)throw new Exception("COMPILE_NOT_ACCEPTED");
        if(cancelled())throw new Exception("CANCELLED_BEFORE_SAVE_NO_RETRY");
        Phase("Save lab");p.Save();if(p.IsModified)throw new Exception("SAVE_DIRTY");
        Write("after.json",Snapshot(p,h,s,tia));Write("result.json",new{status="CONFIGURED",created=created,eventsChanged=eventsChanged,writes=writes,compile="PASS",save="PASS",isModified=p.IsModified,independentReadback="REQUIRED"});
        File.WriteAllText(Gate,Json.Serialize(new{outcome="SAVED_NEEDS_READBACK",project=p.Path.FullName,evidence=Output}));
    }
    sealed class Diagnostic {public string state,description,path;public int errors,warnings,children;}
    const string Anonymous="The user \"Anonymous\" is not supported by WinCC Unified Runtime devices and is not available in runtime.";
    static void Flatten(CompilerResultMessage m,List<Diagnostic> rows,int depth){
        if(depth>32||rows.Count>=10000)throw new Exception("COMPILE_DIAGNOSTIC_LIMIT");
        rows.Add(new Diagnostic{state=m.State.ToString(),description=m.Description,path=m.Path,errors=m.ErrorCount,warnings=m.WarningCount,children=m.Messages.Count});
        foreach(var child in m.Messages)Flatten(child,rows,depth+1);
    }
    static bool AcceptCompile(string state,int errors,int warnings,List<Diagnostic> rows){
        if(errors!=0||warnings<0||warnings>1||!new[]{"Success","Information","Warning"}.Contains(state))return false;
        if(rows.Any(d=>d.errors!=0||d.warnings<0||d.warnings>1||!new[]{"Success","Information","Warning"}.Contains(d.state)))return false;
        var leaves=rows.Where(d=>d.children==0&&(d.state=="Warning"||d.warnings!=0)).ToArray();
        if(leaves.Length==0)return state!="Warning"&&warnings==0&&!rows.Any(d=>d.state=="Warning"||d.warnings!=0);
        return leaves.Length==1&&leaves[0].state=="Warning"&&leaves[0].description==Anonymous;
    }
    static void PolicyTests(){
        var empty=new List<Diagnostic>();var known=new List<Diagnostic>{new Diagnostic{state="Warning",description=Anonymous,warnings=1}};
        if(!AcceptCompile("Success",0,0,empty)||!AcceptCompile("Success",0,0,known)||!AcceptCompile("Warning",0,1,known)||AcceptCompile("Success",0,0,new List<Diagnostic>{new Diagnostic{state="Warning",description="unrecognized",warnings=1}})||AcceptCompile("Success",0,0,new List<Diagnostic>{new Diagnostic{state="Error",errors=1}})||AcceptCompile("Warning",0,2,known))throw new Exception("COMPILE_POLICY_TEST_FAILED");
    }
    static ICompilable Compiler(HmiSoftware h) {
        IEngineeringObject current=h;
        while(current!=null){
            if(current is Project)break;
            var device=current as Device;if(device!=null&&(device.Name!="HMI_JSON_TEST"||SoftwareCount(device.DeviceItems)!=1))throw new Exception("HMI_COMPILER_SCOPE_AMBIGUOUS");
            var service=(current as IEngineeringServiceProvider);var c=service==null?null:service.GetService<ICompilable>();if(c!=null)return c;
            if(device!=null)break;current=current.Parent;
        }
        throw new Exception("EXACT_HMI_COMPILER_UNAVAILABLE");
    }
    static int SoftwareCount(IEnumerable<DeviceItem> items){int count=0;foreach(var i in items){var sc=i.GetService<SoftwareContainer>();if(sc!=null&&sc.Software!=null)count++;count+=SoftwareCount(i.DeviceItems);}return count;}
    static readonly string[] Hidden={"TankTechnologyFields","WeldField_0","WeldField_1","WeldField_2","WeldField_3","WeldField_4","WeldField_5","WeldField_6","WeldField_7","Offsets_Body","Offsets_Header","Offsets_Title","OffsetFields"};
    static void ApplyPreview(HmiScreen s) {
        foreach(var n in Hidden)Set(s.ScreenItems.Find(n),"Visible",false);
        Set(s.ScreenItems.Find("SAVE_Draft"),"Top",900);Set(s.ScreenItems.Find("SAVE_Draft"),"Height",(uint)40);
        Set(s.ScreenItems.Find("DraftHint"),"Top",945);Set(s.ScreenItems.Find("DraftHint"),"Height",(uint)25);
    }
    static bool Eq(IEngineeringObject i,string n,object v){return Object.Equals(i.GetAttribute(n),v);}
    static bool TextMatches(MultilingualText t,string value){var r=t.Items.Where(x=>x.Language.Culture.Name=="en-US").ToArray();return r.Length==1&&r[0].Text==Rich(value);}
    static bool ScriptMatches(IHmiScript s,string action){return s.Async&&s.ScriptCode==Code(action)&&s.GlobalDefinitionAreaScriptCode==Global;}
    static bool Matches(HmiScreen s) {
        foreach(var spec in Plan()) {
            var i=s.ScreenItems.Find(spec.Name);if(i==null)return false;
            if(!Eq(i,"Left",spec.X)||!Eq(i,"Top",spec.Y)||!Eq(i,"Width",spec.W)||!Eq(i,"Height",spec.H)||!Eq(i,"Visible",spec.Visible)||!Eq(i,"Enabled",false))return false;
            var input=i as HmiIOField;if(input!=null&&(!Eq(input,"IOFieldType",HmiIOFieldType.InputOutput)||input.OutputFormat!="{S}"||!input.InputBehavior.AcceptOnDeactivated||!TextMatches(input.ToolTipText,spec.Text)))return false;
            var text=i as HmiText;if(text!=null&&!TextMatches(text.Text,spec.Text))return false;
            var button=i as HmiButton;if(button!=null&&!TextMatches(button.Text,spec.Text))return false;
        }
        foreach(var e in EventPlan()) {
            var pair=e.Key.Split('/');IHmiScript script;
            if(pair[0]==s.Name){var ev=s.EventHandlers.Find((HmiScreenEventType)Enum.Parse(typeof(HmiScreenEventType),pair[1]));if(ev==null)return false;script=ev.Script;}
            else {var ev=((HmiButton)s.ScreenItems.Find(pair[0])).EventHandlers.Find(HmiButtonEventType.Tapped);if(ev==null)return false;script=ev.Script;}
            if(!ScriptMatches(script,e.Value))return false;
        }
        return Hidden.All(n=>!s.ScreenItems.Find(n).Visible)&&Eq(s.ScreenItems.Find("SAVE_Draft"),"Top",900)&&Eq(s.ScreenItems.Find("SAVE_Draft"),"Height",(uint)40)&&Eq(s.ScreenItems.Find("DraftHint"),"Top",945)&&Eq(s.ScreenItems.Find("DraftHint"),"Height",(uint)25);
    }
    static HmiSoftware Hmi(Project p) {
        if(!p.Devices.Any(d=>d.DeviceItems.Any(i=>i.Name=="T05_PLC_SIM"))) throw new Exception("PLC_IDENTITY");
        var devices=p.Devices.Where(d=>d.Name=="HMI_JSON_TEST").ToArray(); if(devices.Length!=1)throw new Exception("DEVICE_IDENTITY");
        var items=devices[0].DeviceItems.Where(i=>i.Name=="HMI_RT_1").ToArray();if(items.Length!=1)throw new Exception("HMI_IDENTITY");
        var c=items[0].GetService<SoftwareContainer>(); var h=c==null?null:c.Software as HmiSoftware;if(h==null)throw new Exception("UNIFIED_REQUIRED");return h;
    }
    static object Script(string ev,IHmiScript s) { return new{eventType=ev,async=s.Async,code=s.ScriptCode,global=s.GlobalDefinitionAreaScriptCode}; }
    static object[] Events(HmiScreenItemBase i) {
        var b=i as HmiButton;if(b!=null)return b.EventHandlers.Select(e=>Script(e.EventType.ToString(),e.Script)).ToArray();
        var f=i as HmiIOField;if(f!=null)return f.EventHandlers.Select(e=>Script(e.EventType.ToString(),e.Script)).ToArray();
        var t=i as HmiText;if(t!=null)return t.EventHandlers.Select(e=>Script(e.EventType.ToString(),e.Script)).ToArray();
        return new object[0];
    }
    static SortedDictionary<string,object> Scalars(IEngineeringObject o) {
        var d=new SortedDictionary<string,object>(StringComparer.Ordinal);
        foreach(var a in o.GetAttributeInfos()) {
            var v=o.GetAttribute(a.Name);
            if(v==null||v.GetType().IsPrimitive||v.GetType().IsEnum||v is string||v is decimal) {
                d[a.Name]=v==null?null:Convert.ToString(v,System.Globalization.CultureInfo.InvariantCulture);
            }
            else if(v is MultilingualText) d[a.Name]=((MultilingualText)v).Items.Select(x=>new{language=x.Language.Culture.Name,text=x.Text}).ToArray();
            else if(v is System.Drawing.Color)d[a.Name]=v.ToString();
        } return d;
    }
    static object Snapshot(Project p,HmiSoftware h,HmiScreen s,TiaPortal tia) {
        TreeNodes=0;var tree=Tree(s,0);
        return new{project=p.Name,path=p.Path.FullName,isModified=p.IsModified,pid=tia.GetCurrentProcess().Id,
            api=typeof(TiaPortal).Assembly.FullName,devices=p.Devices.Select(d=>new{name=d.Name,items=d.DeviceItems.Select(i=>i.Name).ToArray()}).ToArray(),
            screen=Scalars(s),events=s.EventHandlers.Select(e=>Script(e.EventType.ToString(),e.Script)).ToArray(),
            items=s.ScreenItems.OrderBy(i=>i.Name,StringComparer.Ordinal).Select(i=>new{name=i.Name,type=i.GetType().FullName,properties=Scalars(i),inputBehavior=i is HmiIOField?Scalars(((HmiIOField)i).InputBehavior):null,events=Events(i)}).ToArray(),
            modules=h.Scripts.Select(m=>m.Name).OrderBy(n=>n,StringComparer.Ordinal).ToArray(),publicScreenTree=tree,configurationMatches=Matches(s),coverage="Public advertised attributes/compositions and typed screen/button/text/IO events; unexposed vendor state NOT_PROVEN"};
    }
    static object Tree(IEngineeringObject o,int depth) {
        if(depth>16||++TreeNodes>20000)throw new Exception("PUBLIC_TREE_LIMIT");
        var children=new SortedDictionary<string,object>(StringComparer.Ordinal);
        foreach(var c in o.GetCompositionInfos().OrderBy(x=>x.Name,StringComparer.Ordinal)) {
            var value=o.GetComposition(c.Name);var child=value as IEngineeringObject;
            if(child!=null){children[c.Name]=Tree(child,depth+1);continue;}
            var list=value as System.Collections.IEnumerable;
            if(list==null)throw new Exception("UNREAD_COMPOSITION:"+c.Name);
            var entries=new List<object>();foreach(var entry in list){var obj=entry as IEngineeringObject;if(obj==null)throw new Exception("UNREAD_CHILD:"+c.Name);entries.Add(Tree(obj,depth+1));}
            children[c.Name]=entries.ToArray();
        }
        return new{type=o.GetType().FullName,attributes=Scalars(o),children=children};
    }
}
