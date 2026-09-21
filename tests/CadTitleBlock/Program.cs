using RevitMCP.Core.Drawing;
using System.Text.Json;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;

var output=Path.GetFullPath(args.Length>0?args[0]:"test-artifacts/v0611");Directory.CreateDirectory(output);
if(args.Length>1)
{
    var a=CadTitleBlockFileService.Analyze(args[1],"Auto");
    // Explicit local audit mode. No CAD text, private source path, or layer names are emitted.
    var anonymous=new{a.Unit,a.GlobalBounds,Count=a.Geometry.Length,Layers=a.Geometry.GroupBy(g=>g.Layer).Select((g,i)=>new{Layer="Layer-"+(i+1),Count=g.Count(),Types=g.GroupBy(e=>e.Kind).Select(t=>new{Type=t.Key,Count=t.Count()}),Bounds=CadGeometryClusterService.Union(g.Select(e=>e.Bounds))}),Clusters=a.Clusters.Select(c=>new{c.ClusterId,c.GeometryCount,c.Bounds,c.Centroid,c.DistanceFromLargestCluster,c.RemoteGeometryWarning}),Candidates=a.Candidates.Select(c=>new{c.CandidateId,c.ClusterId,c.Bounds,c.Width,c.Height,c.AspectRatio,c.Centroid,c.GeometryCount,c.DetectedPaperSize,c.SuggestedPurpose,c.ConfidenceEvidence}),Geometry=a.Geometry.Select(g=>new{g.Id,g.Kind,g.Bounds})};
    File.WriteAllText(Path.Combine(output,"actual-analysis.json"),JsonSerializer.Serialize(anonymous,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new{Candidates=a.Candidates.Count,Geometry=a.Geometry.Length}));return 0;
}
var checks=new List<object>();int failures=0;
void Check(string name,bool pass,object? evidence=null){checks.Add(new{Name=name,Passed=pass,Evidence=evidence});if(!pass){failures++;Console.WriteLine("FAIL "+name);}}
CadGeometry Border(string id,double x,double y,double w=420,double h=297,string layer="FRAME")=>new(id,layer,"LwPolyline",new(x,y,x+w,y+h),new[]{new DrawingPoint(x,y),new DrawingPoint(x+w,y),new DrawingPoint(x+w,y+h),new DrawingPoint(x,y+h)},true);
CadGeometry Remote()=>new("REMOTE","STRAY","Line",new(500000000,500000000,500000001,500000001),new[]{new DrawingPoint(500000000,500000000),new DrawingPoint(500000001,500000001)});
CadTitleBlockAnalysis Analyze(params CadGeometry[] g)=>CadTitleBlockAnalyzer.Analyze(g,"mm",1);
var a1=Analyze(Border("A",0,0),Remote());Check("A_remote_preserves_A3",a1.Candidates.Count==1&&a1.Candidates[0].DetectedPaperSize=="A3"&&a1.Clusters.Any(c=>c.RemoteGeometryWarning));
var b=Analyze(Border("B",500000000,700000000)).Candidates.Single();var bn=CadTitleBlockAnalyzer.Normalize(b,new());Check("B_large_coordinates_translation_only",bn.UniformScale==1&&bn.TranslationRequired&&bn.TargetBounds.Width==420);
var c=Analyze(Border("C",0,0,420000,297000)).Candidates.Single();Check("C_wrong_units_suggested",c.DetectedPaperSize=="A3"&&c.SuggestedUniformScale==.001);var cn=CadTitleBlockAnalyzer.Normalize(c,new(){Mode=CadNormalizationMode.UniformToPaper,TargetPaper="A3"});Check("C_uniform_normalization",Math.Abs(cn.UniformScale-.001)<1e-12&&!cn.ReviewRequired);
var d=Analyze(Border("D",0,0,520,297)).Candidates.Single();Check("D_invalid_ratio_no_auto_A3",d.DetectedPaperSize=="Custom"&&CadTitleBlockAnalyzer.Normalize(d,new(){Mode=CadNormalizationMode.UniformToPaper,TargetPaper="A3"}).ReviewRequired);
var e=Analyze(Border("E",0,0),Border("TABLE",10,10,50,20));Check("E_outer_not_internal_table",e.Candidates.Count==1&&e.Candidates[0].GeometryCount==2&&e.Candidates[0].Width==420);
var f=Analyze(Border("F",0,0),Border("LOGO",10,10,40,20,"LOGO"),Remote());Check("F_layer_evidence",f.Candidates.Single().ContainedLayers.Length==2&&f.Clusters.Count==2);
var g=Analyze(Border("G1",0,0),Border("G2",1000,0));Check("G_equal_size_candidates",g.Candidates.Count==2&&g.Candidates.Select(c=>c.CandidateId).Distinct().Count()==2&&g.Candidates.All(c=>c.DetectedPaperSize=="A3"));
var h=Analyze(Border("H1",0,0),Border("H2",1000,0),Remote());Check("H_two_frames_and_remote",h.Candidates.Count==2&&h.Clusters.Count==3);
var i=Analyze(Border("I1",0,0),Border("I2",1000,0));Check("I_without_text_candidates_survive",i.Candidates.Count==2&&i.Candidates.All(c=>c.SuggestedPurpose==TitleBlockPurpose.Custom));
foreach(var paper in new[]{"A0","A1","A2","A3","A4"}){var size=AutoSheetLayoutService.PaperSize(paper);Check("ISO_"+paper,CadTitleBlockAnalyzer.Recognize(size.Width,size.Height).Paper==paper&&CadTitleBlockAnalyzer.Recognize(size.Height,size.Width).Paper==paper);}
Check("deterministic_order",JsonSerializer.Serialize(h)==JsonSerializer.Serialize(Analyze(Remote(),Border("H2",1000,0),Border("H1",0,0))));
var rectangularLines=Border("lines",0,0).Points.Select((p,index)=>{var next=Border("lines",0,0).Points[(index+1)%4];return new CadGeometry("L"+index,"FRAME","Line",new(Math.Min(p.X,next.X),Math.Min(p.Y,next.Y),Math.Max(p.X,next.X),Math.Max(p.Y,next.Y)),new[]{p,next});}).Append(new CadGeometry("DETACHED","TEXT","TextEntity",new(100,100,100,100),Array.Empty<DrawingPoint>(),false,TitleBlockPurpose.ConstructionDrawing)).ToArray();
Check("detached_text_inside_line_loop_preserved",Analyze(rectangularLines).Candidates.Single().GeometryCount==5);
var contam=new CadConversionSelection{CandidateId=g.Candidates[0].CandidateId,SelectedLayers=new[]{"FRAME"},AdditionalGeometryIds=g.Candidates[1].GeometryIds};bool contamRejected=false;try{CadTitleBlockAnalyzer.SelectedGeometry(g,contam);}catch(ArgumentException){contamRejected=true;}Check("other_candidate_override_rejected",contamRejected);
var manual=CadTitleBlockAnalyzer.Manual(h,new(0,0,420,297));Check("manual_border_does_not_delete",h.Geometry.Length==3&&manual.GeometryCount==1);
var layerSelection=new CadConversionSelection{CandidateId=f.Candidates[0].CandidateId,SelectedLayers=new[]{"FRAME"}};Check("layer_selection_keeps_paper_bounds",CadTitleBlockAnalyzer.SelectedGeometry(f,layerSelection).Length==1&&CadTitleBlockAnalyzer.Selected(f,layerSelection).Bounds.Width==420);
layerSelection.AdditionalGeometryIds=new[]{"REMOTE"};layerSelection.SelectedLayers=new[]{"FRAME","STRAY"};Check("remote_explicit_override",CadTitleBlockAnalyzer.SelectedGeometry(f,layerSelection).Length==2);

// Real DWG serialization, independent internal geometry and text, then isolated DXF read-back.
var doc=new CadDocument();doc.Header.InsUnits=ACadSharp.Types.Units.UnitsType.Millimeters;
void AddBorder(double x){doc.Entities.Add(new LwPolyline(new[]{new XY(x,0),new XY(x+420,0),new XY(x+420,297),new XY(x,297)}){IsClosed=true});}
AddBorder(0);AddBorder(1000);doc.Entities.Add(new Line(new XYZ(10,10,0),new XYZ(50,10,0)));doc.Entities.Add(new Circle{Center=new XYZ(1050,40,0),Radius=10});doc.Entities.Add(new TextEntity("施工圖"){InsertPoint=new XYZ(30,20,0),Height=3});doc.Entities.Add(new TextEntity("竣工圖"){InsertPoint=new XYZ(1030,20,0),Height=3});
var source=Path.Combine(output,"synthetic-multiple.dwg");DwgWriter.Write(source,doc);
foreach(var fixture in new[]{("A",new[]{Border("A",0,0),Remote()},1),("B",new[]{Border("B",500000000,700000000)},1),("C",new[]{Border("C",0,0,420000,297000)},1),("D",new[]{Border("D",0,0,520,297)},1),("E",new[]{Border("E",0,0),Border("TABLE",10,10,50,20)},1),("F",new[]{Border("F",0,0),Border("LOGO",10,10,40,20,"LOGO"),Remote()},1),("G",new[]{Border("G1",0,0),Border("G2",1000,0)},2),("H",new[]{Border("H1",0,0),Border("H2",1000,0),Remote()},2)})
{
    var cadDoc=new CadDocument();cadDoc.Header.InsUnits=ACadSharp.Types.Units.UnitsType.Millimeters;
    foreach(var geometry in fixture.Item2)
    {Entity entity=geometry.Closed?new LwPolyline(geometry.Points.Select(p=>new XY(p.X,p.Y))){IsClosed=true}:new Line(new XYZ(geometry.Points[0].X,geometry.Points[0].Y,0),new XYZ(geometry.Points[1].X,geometry.Points[1].Y,0));entity.Layer=new ACadSharp.Tables.Layer(geometry.Layer);cadDoc.Entities.Add(entity);}
    var path=Path.Combine(output,"case-"+fixture.Item1+".dwg");DwgWriter.Write(path,cadDoc);var read=CadTitleBlockFileService.Analyze(path,"Auto");Check("DWG_"+fixture.Item1,read.Candidates.Count==fixture.Item3&&read.Geometry.Length==fixture.Item2.Length);
}
var j=CadTitleBlockFileService.Analyze(source,"Auto");Check("J_real_DWG_two_candidates",j.Candidates.Count==2,j.Candidates.Count);
var blockDoc=new CadDocument();blockDoc.Header.InsUnits=ACadSharp.Types.Units.UnitsType.Millimeters;var block=new ACadSharp.Tables.BlockRecord("RotatedFrame");
block.Entities.Add(new LwPolyline(new[]{new XY(0,0),new XY(420,0),new XY(420,297),new XY(0,297)}){IsClosed=true});block.Entities.Add(new TextEntity("施工圖"){InsertPoint=new XYZ(30,20,0),AlignmentPoint=new XYZ(35,20,0),HorizontalAlignment=TextHorizontalAlignment.Middle,Height=3});
blockDoc.Entities.Add(new Insert(block){InsertPoint=new XYZ(100000,200000,0),Rotation=Math.PI/6});var blockPath=Path.Combine(output,"rotated-block.dwg");DwgWriter.Write(blockPath,blockDoc);var blockAnalysis=CadTitleBlockFileService.Analyze(blockPath,"Auto");
Check("rotated_block_A3",blockAnalysis.Candidates.Count==1&&blockAnalysis.Candidates[0].DetectedPaperSize=="A3"&&Math.Abs(blockAnalysis.Candidates[0].Rotation-Math.PI/6)<1e-8);
var rotSelection=new CadConversionSelection{CandidateId=blockAnalysis.Candidates[0].CandidateId,SelectedLayers=blockAnalysis.Candidates[0].ContainedLayers,Previewed=true,GeometryFilterConfirmed=true,PurposeConfirmed=true,ProfileName="Rotation"};
var rotExternal=new ExternalTitleBlockAnalysis{FilePath=blockPath,FileHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(blockPath))),Unit="Auto",IsCad=true,Cad=blockAnalysis,CadSelection=rotSelection};rotExternal.CadPreviewSignature=CadTitleBlockAnalyzer.PreviewSignature(rotExternal);
var rotOutput=Path.Combine(output,"rotated-local-"+Guid.NewGuid().ToString("N")+".dxf");CadTitleBlockFileService.Export(rotExternal,rotOutput);var rotatedRead=CadTitleBlockFileService.Analyze(rotOutput,"mm");Check("rotated_block_translation_without_scaling",rotatedRead.Candidates.Count==1&&Math.Abs(rotatedRead.Candidates[0].Width-420)<1e-6&&Math.Abs(rotatedRead.Candidates[0].Height-297)<1e-6&&Math.Abs(rotatedRead.GlobalBounds.MinX)<1e-6&&Math.Abs(rotatedRead.GlobalBounds.MinY)<1e-6);
var blockText=DxfReader.Read(rotOutput).Entities.OfType<TextEntity>().Single();Check("rotated_block_second_point_normalized",Math.Abs(blockText.AlignmentPoint.X-35)<1e-6&&Math.Abs(blockText.AlignmentPoint.Y-20)<1e-6);
foreach(var candidate in j.Candidates)
{
    var s=new CadConversionSelection{CandidateId=candidate.CandidateId,SelectedLayers=candidate.ContainedLayers,Previewed=true,GeometryFilterConfirmed=true,PurposeConfirmed=true,Purpose=candidate.SuggestedPurpose,ProfileName="Synthetic "+candidate.SuggestedPurpose};
    var analysis=new ExternalTitleBlockAnalysis{FilePath=source,FileHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))),Unit="Auto",IsCad=true,Cad=j,CadSelection=s};analysis.CadPreviewSignature=CadTitleBlockAnalyzer.PreviewSignature(analysis);
    string path=Path.Combine(output,"isolated-"+Guid.NewGuid().ToString("N")+".dxf");CadTitleBlockFileService.Export(analysis,path);var read=DxfReader.Read(path);
    Check("J_isolation_"+candidate.SuggestedPurpose,read.Entities.Count==3&&read.Entities.OfType<TextEntity>().Single().Value==(candidate.SuggestedPurpose==TitleBlockPurpose.ConstructionDrawing?"施工圖":"竣工圖")&&read.Entities.Any(e=>candidate.SuggestedPurpose==TitleBlockPurpose.ConstructionDrawing?e is Line:e is Circle));
    s.ProfileName="changed";bool rejected=false;try{CadTitleBlockFileService.Export(analysis,Path.Combine(output,"should-not-exist.dxf"));}catch(InvalidOperationException){rejected=true;}Check("stale_preview_rejected_"+candidate.SuggestedPurpose,rejected);
}
var alignedDoc=new CadDocument();alignedDoc.Header.InsUnits=ACadSharp.Types.Units.UnitsType.Millimeters;
alignedDoc.Entities.Add(new LwPolyline(new[]{new XY(10000000,20000000),new XY(10420000,20000000),new XY(10420000,20297000),new XY(10000000,20297000)}){IsClosed=true});
alignedDoc.Entities.Add(new TextEntity("Centered"){InsertPoint=new XYZ(10010000,20020000,0),AlignmentPoint=new XYZ(10020000,20020000,0),HorizontalAlignment=TextHorizontalAlignment.Middle,Height=3000});
alignedDoc.Entities.Add(new AttributeDefinition{Value="Attribute",Tag="TEST",InsertPoint=new XYZ(10030000,20040000,0),AlignmentPoint=new XYZ(10040000,20040000,0),HorizontalAlignment=TextHorizontalAlignment.Middle,Height=3000});
var alignedPath=Path.Combine(output,"aligned-text.dwg");DwgWriter.Write(alignedPath,alignedDoc);var alignedAnalysis=CadTitleBlockFileService.Analyze(alignedPath,"Auto");var ac=alignedAnalysis.Candidates.Single();
var alignedExternal=new ExternalTitleBlockAnalysis{FilePath=alignedPath,FileHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(alignedPath))),Unit="Auto",IsCad=true,Cad=alignedAnalysis,CadSelection=new(){CandidateId=ac.CandidateId,SelectedLayers=ac.ContainedLayers,Mode=CadNormalizationMode.UniformToPaper,TargetPaper="A3",Previewed=true,GeometryFilterConfirmed=true,PurposeConfirmed=true,ProfileName="Aligned"}};alignedExternal.CadPreviewSignature=CadTitleBlockAnalyzer.PreviewSignature(alignedExternal);
var alignedOutput=Path.Combine(output,"aligned-local-"+Guid.NewGuid().ToString("N")+".dxf");CadTitleBlockFileService.Export(alignedExternal,alignedOutput);var alignedRead=DxfReader.Read(alignedOutput);
var centered=alignedRead.Entities.OfType<TextEntity>().Single(e=>e is not AttributeDefinition);var attribute=alignedRead.Entities.OfType<AttributeDefinition>().Single();
Check("centered_text_second_point_normalized",Math.Abs(centered.AlignmentPoint.X-20)<1e-6&&Math.Abs(centered.AlignmentPoint.Y-20)<1e-6&&Math.Abs(centered.Height-3)<1e-6);
Check("attribute_second_point_normalized",Math.Abs(attribute.AlignmentPoint.X-40)<1e-6&&Math.Abs(attribute.AlignmentPoint.Y-40)<1e-6&&Math.Abs(attribute.Height-3)<1e-6);
var sourceFiles=new[]{"MCP/Core/Drawing/CadTitleBlockGeometry.cs","MCP/Core/Drawing/CadTitleBlockFileService.cs","MCP/Core/Drawing/DrawingModels.cs","tests/CadTitleBlock/Program.cs","tests/CadTitleBlock/CadTitleBlock.csproj"}.Select(path=>new{Path=path,SHA256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))}).ToArray();
var buildPath="MCP/bin/Release.R26/RevitMCP.dll";var report=new{GateC5=failures==0?"PASS":"FAIL",BuildSHA256=File.Exists(buildPath)?Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(buildPath))):"",SourceFiles=sourceFiles,Passed=checks.Count-failures,Failed=failures,Assertions=checks,Timestamp=DateTimeOffset.UtcNow};File.WriteAllText(Path.Combine(output,"cad-c5.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"Gate C5 {report.GateC5}: {report.Passed} passed / {failures} failed");return failures==0?0:1;


