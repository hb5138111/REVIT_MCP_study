using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using RevitMCP.Models;

namespace RevitMCP.Core
{
    /// <summary>Runs only on documents created here; never acquires ActiveUIDocument.</summary>
    internal static class CoordinationSelfTest
    {
        internal const string FixtureVersion = "coordination-1";
        internal sealed class Assertion
        {
            public string TestName { get; set; } = string.Empty;
            public string Expected { get; set; } = string.Empty;
            public string Actual { get; set; } = string.Empty;
            public bool Passed { get; set; }
            public string AffectedDomain { get; set; } = "mep-opening-candidate-scan";
            public string AffectedTool { get; set; } = "scan_opening_candidates / detect_clashes shared geometry";
            public string AffectedBackend { get; set; } = "MCP/Core/CoordinationService.cs";
            public string Evidence { get; set; } = string.Empty;
        }
        public static void Run(Autodesk.Revit.ApplicationServices.Application app, string directory)
        {
            string root = Path.GetFullPath(directory);
            // Require a fresh launcher-generated marker and exact directory name; all outputs stay here.
            if (!Path.GetFileName(root).StartsWith("revit-selftest-", StringComparison.Ordinal) || !File.Exists(Path.Combine(root, "request.json")))
                throw new InvalidOperationException("Self-test requires a dedicated launcher directory.");
            if (app.Documents.Size != 0) throw new InvalidOperationException("Self-test refuses a session with open documents.");
            var assertions = new List<Assertion>();
            Document? doc = null;
            void Check(string name, object expected, object? actual, bool passed, string evidence)
                => assertions.Add(new Assertion { TestName = name, Expected = Convert.ToString(expected) ?? string.Empty, Actual = Convert.ToString(actual) ?? string.Empty, Passed = passed, Evidence = evidence });
            try
            {
                var request = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, "request.json"))) ?? throw new InvalidOperationException("Invalid fixture request.");
                string template = request["ProjectTemplate"];
                string familyTemplate = request["FamilyTemplate"];
                // NewProjectDocument creates a new document; no existing RVT is accepted.
                if (!template.EndsWith(".rte", StringComparison.OrdinalIgnoreCase) || !familyTemplate.EndsWith(".rft", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Only templates are accepted.");
                doc = app.NewProjectDocument(template);
                if (doc.IsWorkshared || doc.IsLinked) throw new InvalidOperationException("Fixture must be standalone.");
                FamilySymbol beam = MakeBoxFamily(app, doc, familyTemplate, root, "FixtureBeam", BuiltInCategory.OST_StructuralFraming, 8, 1, 2);
                FamilySymbol column = MakeBoxFamily(app, doc, familyTemplate, root, "FixtureColumn", BuiltInCategory.OST_StructuralColumns, 1, 1, 10);
                Level fl1, fl2; Pipe wallPipe, beamPipe; Duct duct; Conduit conduit;
                using (var tx = new Transaction(doc, "Create disposable CoordinationFixture"))
                {
                    tx.Start();
                    // Remove template contents in the newly created fixture only.
                    var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
                    fl1 = levels.FirstOrDefault() ?? Level.Create(doc, 0); fl1.Name = "FL1"; fl1.Elevation = 0;
                    fl2 = levels.Skip(1).FirstOrDefault() ?? Level.Create(doc, 12); fl2.Name = "FL2"; fl2.Elevation = 12;
                    var wallType = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().First(t => t.Kind == WallKind.Basic);
                    Wall.Create(doc, Line.CreateBound(new XYZ(0,-5,0),new XYZ(0,5,0)), wallType.Id, fl1.Id, 10, 0, false, false);
                    var floorType = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().First();
                    var loop = Rectangle(30,-4,38,4,6);
                    var floor = Floor.Create(doc, new List<CurveLoop> { loop }, floorType.Id, fl1.Id);
                    floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM).Set(6);
                    beam.Activate(); column.Activate(); doc.Regenerate();
                    doc.Create.NewFamilyInstance(new XYZ(12,0,4),beam,fl1,StructuralType.NonStructural);
                    doc.Create.NewFamilyInstance(new XYZ(45,0,0),column,fl1,StructuralType.NonStructural);
                    var pipeType = new FilteredElementCollector(doc).OfClass(typeof(PipeType)).Cast<PipeType>().First();
                    var ps = new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).Cast<PipingSystemType>().FirstOrDefault()
                        ?? PipingSystemType.Create(doc, MEPSystemClassification.DomesticColdWater, "FixtureWater");
                    wallPipe = Pipe.Create(doc,ps.Id,pipeType.Id,fl1.Id,new XYZ(-3,0,3),new XYZ(3,0,3));
                    beamPipe = Pipe.Create(doc,ps.Id,pipeType.Id,fl1.Id,new XYZ(16,-3,5),new XYZ(16,3,5));
                    wallPipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(100 / 304.8);
                    beamPipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(100 / 304.8);
                    var ductType = new FilteredElementCollector(doc).OfClass(typeof(DuctType)).Cast<DuctType>().First(t => t.Shape == ConnectorProfileType.Rectangular);
                    var ds = new FilteredElementCollector(doc).OfClass(typeof(MechanicalSystemType)).Cast<MechanicalSystemType>().FirstOrDefault()
                        ?? MechanicalSystemType.Create(doc,MEPSystemClassification.SupplyAir,"FixtureAir");
                    duct = Duct.Create(doc,ds.Id,ductType.Id,fl1.Id,new XYZ(34,0,2),new XYZ(34,0,9));
                    duct.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM).Set(200 / 304.8);
                    duct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM).Set(100 / 304.8);
                    var conduitType = new FilteredElementCollector(doc).OfClass(typeof(ConduitType)).FirstElement();
                    conduit = Conduit.Create(doc,conduitType.Id,new XYZ(60,0,3),new XYZ(65,0,3),fl1.Id);
                    tx.Commit();
                }
                var service = new CoordinationService();
                CoordinationRequest Query(string mep, string host) => new CoordinationRequest { MepCategory=mep, HostCategory=host, LevelName="FL1", OpeningCandidates=true, ClearanceMm=25 };
                var walls = service.Scan(doc,Query("Pipes","Walls"));
                Check("pipe_wall_count",1,walls.TotalMatchedCount,walls.TotalMatchedCount==1,"Fixture wall and crossing pipe");
                if(walls.Rows.Count>0)
                {
                    var row=walls.Rows[0];
                    Check("opening_diameter",150,row.DiameterMm,row.DiameterMm.HasValue&&Math.Abs(row.DiameterMm.Value-150)<0.001,"100 mm + 2 * 25 mm");
                    Check("element_readback",true,doc.GetElement(CoordinationService.Id(row.Mep.ElementId))!=null,doc.GetElement(CoordinationService.Id(row.Mep.ElementId))?.UniqueId==row.Mep.UniqueId,"UniqueId read-back");
                    Check("review_honesty",true,row.ReviewRequired,row.ReviewRequired&&row.OpeningBottomMm==null,"Unknown solid edge and opening bottom remain warnings");
                }
                var beams=service.Scan(doc,Query("Pipes","StructuralFraming"));
                Check("pipe_beam_count",1,beams.TotalMatchedCount,beams.TotalMatchedCount==1,"Generated structural-category family");
                Check("beam_review",true,beams.Rows.Any(r=>r.WarningCodes.Contains("structural_framing_review")),beams.Rows.Any(r=>r.WarningCodes.Contains("structural_framing_review")),"Beam review code required");
                var floors=service.Scan(doc,Query("Ducts","Floors"));
                Check("duct_floor_count",1,floors.TotalMatchedCount,floors.TotalMatchedCount==1,"Vertical duct through slab");
                Check("conduit_no_clash",0,service.Scan(doc,Query("Conduits","Walls")).TotalMatchedCount,service.Scan(doc,Query("Conduits","Walls")).TotalMatchedCount==0,"Remote conduit");
                var otherLevel=Query("Pipes","Walls");otherLevel.LevelName="FL2";
                Check("level_scope",0,service.Scan(doc,otherLevel).TotalMatchedCount,service.Scan(doc,otherLevel).TotalMatchedCount==0,"Pre-geometry MEP reference-level filter");
                var clashRequest=Query("Pipes","Walls");clashRequest.OpeningCandidates=false;clashRequest.ClearanceMm=null;
                Check("clash_scan",1,service.Scan(doc,clashRequest).TotalMatchedCount,service.Scan(doc,clashRequest).TotalMatchedCount==1,"Clash workflow does not require clearance");
                using(var tx=new Transaction(doc,"Add oblique regression sample"))
                {
                    tx.Start();
                    var pt=new FilteredElementCollector(doc).OfClass(typeof(PipeType)).FirstElement();
                    var sys=new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).FirstElement();
                    Pipe.Create(doc,sys.Id,pt.Id,fl1.Id,new XYZ(-3,-2,3),new XYZ(3,1,3));
                    tx.Commit();
                }
                var oblique=service.Scan(doc,Query("Pipes","Walls"));
                Check("oblique_review",true,oblique.Rows.Any(r=>r.WarningCodes.Contains("oblique_penetration")),oblique.Rows.Any(r=>r.WarningCodes.Contains("oblique_penetration")),"Non-orthogonal pipe through wall");
                var capped=Query("Pipes","Walls");capped.MaxResults=1;
                var limited=service.Scan(doc,capped);
                Check("explicit_truncation","total=2, returned=1, truncated=true",$"{limited.TotalMatchedCount}/{limited.ReturnedCount}/{limited.IsTruncated}",limited.TotalMatchedCount==2&&limited.ReturnedCount==1&&limited.IsTruncated,"Total includes matches beyond display cap");
                // Independent translated MEP link created entirely from a new fixture document.
                Document linkDoc=app.NewProjectDocument(template);
                string linkPath=Path.Combine(root,"translated-link.rvt");
                try
                {
                    using(var tx=new Transaction(linkDoc,"Create disposable link"))
                    {
                        tx.Start();
                        var lev=new FilteredElementCollector(linkDoc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l=>l.Elevation).First();lev.Name="FL1";
                        var pt=new FilteredElementCollector(linkDoc).OfClass(typeof(PipeType)).FirstElement();
                        var sys=new FilteredElementCollector(linkDoc).OfClass(typeof(PipingSystemType)).FirstElement();
                        Pipe.Create(linkDoc,sys.Id,pt.Id,lev.Id,new XYZ(-103,0,3),new XYZ(-97,0,3));tx.Commit();
                    }
                    linkDoc.SaveAs(linkPath,new SaveAsOptions { OverwriteExistingFile=false });
                }
                finally { linkDoc.Close(false); }
                long linkId;
                using(var tx=new Transaction(doc,"Load disposable translated link"))
                {
                    tx.Start();
                    var loaded=RevitLinkType.Create(doc,ModelPathUtils.ConvertUserVisiblePathToModelPath(linkPath),new RevitLinkOptions(false));
                    var instance=RevitLinkInstance.Create(doc,loaded.ElementId);
                    ElementTransformUtils.MoveElement(doc,instance.Id,new XYZ(100,0,0));linkId=instance.Id.GetIdValue();tx.Commit();
                }
                var linked=Query("Pipes","Walls");linked.MepLinkId=linkId;
                var linkedResult=service.Scan(doc,linked);
                Check("translated_link",1,linkedResult.TotalMatchedCount,linkedResult.TotalMatchedCount==1,"MEP link translated +100 ft; pipe center resolves to host origin");
                Check("translated_point",0,linkedResult.Rows.FirstOrDefault()?.Xmm,linkedResult.Rows.Count==1&&Math.Abs(linkedResult.Rows[0].Xmm)<1,"Host-coordinate center mm");
                string fixturePath=Path.Combine(root,"CoordinationFixture.rvt");doc.SaveAs(fixturePath,new SaveAsOptions { OverwriteExistingFile=false });
                Check("fixture_saved",true,File.Exists(fixturePath),File.Exists(fixturePath),"Disposable artifact only");
            }
            catch(Exception ex) { Check("runtime_exception","No exception",ex.ToString(),false,"Actual Revit API exception"); }
            finally
            {
                if(doc!=null&&doc.IsValidObject)doc.Close(false);
                var assembly=Assembly.GetExecutingAssembly().Location;
                using(var sha=SHA256.Create())
                using(var stream=File.OpenRead(assembly))
                {
                    var report=new { TestRunId=Path.GetFileName(root),Timestamp=DateTimeOffset.UtcNow,RevitVersion=app.VersionNumber,
                        BuildHash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-",""),FixtureVersion,CreatedBy="RevitMCP Self-Test Lab",ExpectedSnapshotVersion=1,
                        GateA="SEE_STATIC_REPORT",GateB="SEE_LOGIC_REPORT",GateC=assertions.All(a=>a.Passed)&&assertions.Count>=12?"PASS":"FAIL",GateD="N/A (read-only production workflow)",
                        Passed=assertions.Count(a=>a.Passed),Failed=assertions.Count(a=>!a.Passed),Skipped=0,Warnings=new[]{"CoreFixture, TakeoffFixture, DrawingFixture are planned only"},Assertions=assertions };
                    File.WriteAllText(Path.Combine(root,"runtime.json"),JsonConvert.SerializeObject(report,Formatting.Indented));
                    File.WriteAllText(Path.Combine(root,"runtime.md"),"# Revit Self-Test\n\n"+string.Join("\n",assertions.Select(a=>$"- {(a.Passed?"PASS":"FAIL")} {a.TestName}: expected {a.Expected}; actual {a.Actual}")));
                }
            }
        }
        private static CurveLoop Rectangle(double x0,double y0,double x1,double y1,double z)
        {
            var points=new[]{new XYZ(x0,y0,z),new XYZ(x1,y0,z),new XYZ(x1,y1,z),new XYZ(x0,y1,z)};
            var loop=new CurveLoop();for(int i=0;i<4;i++)loop.Append(Line.CreateBound(points[i],points[(i+1)%4]));return loop;
        }
        private static FamilySymbol MakeBoxFamily(Autodesk.Revit.ApplicationServices.Application app,Document project,string template,string root,string name,BuiltInCategory category,double length,double width,double height)
        {
            var family=app.NewFamilyDocument(template);string file=Path.Combine(root,name+".rfa");
            try
            {
                using(var tx=new Transaction(family,"Create test family"))
                {
                    tx.Start();family.OwnerFamily.FamilyCategory=Category.GetCategory(family,category);
                    var curves=new CurveArray();foreach(var curve in Rectangle(0,-width/2,length,width/2,0))curves.Append(curve);
                    var profile=new CurveArrArray();profile.Append(curves);
                    var plane=SketchPlane.Create(family,Plane.CreateByNormalAndOrigin(XYZ.BasisZ,XYZ.Zero));
                    family.FamilyCreate.NewExtrusion(true,profile,plane,height);tx.Commit();
                }
                family.SaveAs(file,new SaveAsOptions { OverwriteExistingFile=false });
            }
            finally{family.Close(false);}
            using(var tx=new Transaction(project,"Load fixture family"))
            {tx.Start();if(!project.LoadFamily(file,out Family loaded))throw new InvalidOperationException("Cannot load fixture family");tx.Commit();return (FamilySymbol)project.GetElement(loaded.GetFamilySymbolIds().First());}
        }
    }
}
