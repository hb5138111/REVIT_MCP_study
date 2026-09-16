using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root=Path.GetFullPath(args.Length>0?args[0]:".");
string Relative(string p)=>Path.GetRelativePath(root,p).Replace('\\','/');
var options=new CSharpParseOptions(preprocessorSymbols:new[]{"REVIT2026","REVIT2026_OR_GREATER","REVIT2025_OR_GREATER","REVIT2024_OR_GREATER","REVIT2023_OR_GREATER","REVIT2022_OR_GREATER","NET8_0","NET8_0_OR_GREATER"});
var trees=Directory.GetFiles(Path.Combine(root,"MCP"),"*.cs",SearchOption.AllDirectories)
    .Where(p=>!Relative(p).Split('/').Any(s=>s is "obj" or "bin")).OrderBy(p=>p)
    .Select(p=>CSharpSyntaxTree.ParseText(File.ReadAllText(p),options,p)).ToArray();
using var references=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"test-artifacts/source-audit/references.json")));
var refs=references.RootElement.GetProperty("Items").GetProperty("ReferencePath").EnumerateArray().Select(e=>e.GetProperty("Identity").GetString()!).ToArray();var compilation=CSharpCompilation.Create("SourceAudit",trees,refs.Select(p=>MetadataReference.CreateFromFile(p)),new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
string Key(ISymbol symbol)=>(symbol is IMethodSymbol m && m.ReducedFrom != null ? m.ReducedFrom : symbol).OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
var nodes=new Dictionary<string,Method>();
var sections=new List<(string Command,SemanticModel Model,SwitchSectionSyntax Section)>();
foreach(var tree in trees){
 var model=compilation.GetSemanticModel(tree,true);
 foreach(var n in tree.GetRoot().DescendantNodes().OfType<BaseMethodDeclarationSyntax>()){
  if(model.GetDeclaredSymbol(n) is not IMethodSymbol symbol)continue;
  var item=new Method{Name=symbol.Name,Owner=symbol.ContainingType.ToDisplayString(),Id=Key(symbol),File=Relative(tree.FilePath),Line=n.GetLocation().GetLineSpan().StartLinePosition.Line+1};
  foreach(var call in n.DescendantNodes().OfType<InvocationExpressionSyntax>()){
   var info=model.GetSymbolInfo(call);
   if(info.Symbol is IMethodSymbol target){
    if(target.Locations.Any(l=>l.IsInSource))item.Calls.Add(Key(target));
    else {var full=Key(target);if(full.StartsWith("Autodesk.Revit."))item.RevitApi.Add(full);}
   }else if(info.CandidateSymbols.Length>0){foreach(var candidate in info.CandidateSymbols.Where(s=>s.Locations.Any(l=>l.IsInSource)))item.Calls.Add(Key(candidate));item.Unresolved.Add(call.Expression.ToString());}
   else item.Unresolved.Add(call.Expression.ToString());
  }
  foreach(var creation in n.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()){
   var type=model.GetTypeInfo(creation).Type?.ToDisplayString()??creation.Type.ToString();
   if(creation.Type.ToString().Split('.').Last() is "Transaction" or "SubTransaction" or "TransactionGroup")item.Transactions.Add("Autodesk.Revit.DB."+creation.Type.ToString().Split('.').Last());
   if(type.StartsWith("Autodesk.Revit."))item.RevitApi.Add("new "+type);
   if(model.GetSymbolInfo(creation).Symbol is IMethodSymbol ctor&&ctor.Locations.Any(l=>l.IsInSource))item.Calls.Add(Key(ctor));
  }
  foreach(var element in n.DescendantNodes().OfType<ElementAccessExpressionSyntax>())foreach(var arg in element.ArgumentList.Arguments)
   if(arg.Expression is LiteralExpressionSyntax literal&&literal.IsKind(SyntaxKind.StringLiteralExpression))item.Fields.Add(literal.Token.ValueText);
  item.TransportBound=symbol.Parameters.Any(p=>p.Type.ToDisplayString().Contains("Newtonsoft.Json"));
  item.LinkEvidence=n.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(c=>c.Expression.ToString().EndsWith("GetLinkDocument")||c.Expression.ToString().EndsWith("GetTotalTransform")||c.Expression.ToString().EndsWith("GetTransform")).Select(c=>c.ToString()).Distinct().ToList();
  item.ModelWriteEvidence=item.RevitApi.Where(s=>s.Contains(".Set(")||s.Contains(".Delete(")||s.Contains("ElementTransformUtils.")||s.Contains(".Create(")||s.Contains(".ChangeTypeId(")||s.Contains(".SetElementOverrides(")).ToList();
  nodes[item.Id]=item;
 }
 foreach(var sw in tree.GetRoot().DescendantNodes().OfType<SwitchStatementSyntax>().Where(s=>s.Expression.ToString().Contains("request.CommandName")))
  foreach(var section in sw.Sections)foreach(var label in section.Labels.OfType<CaseSwitchLabelSyntax>())if(label.Value is LiteralExpressionSyntax literal)sections.Add((literal.Token.ValueText,model,section));
}
// Preserve conservative same-type edges when Roslyn leaves an invocation unbound.
// No name-only cross-type inference: those remain explicit audit boundaries.
foreach(var method in nodes.Values)foreach(var unresolved in method.Unresolved){
 if(!unresolved.Contains('.') && !unresolved.Contains('(')){
  var name=unresolved.Split('<')[0];
  foreach(var target in nodes.Values.Where(m=>m.Owner==method.Owner && m.Name==name))method.Calls.Add(target.Id);
 }
}
var commands=sections.Select(s=>{
 var entries=s.Section.DescendantNodes().OfType<InvocationExpressionSyntax>().Select(c=>s.Model.GetSymbolInfo(c).Symbol).OfType<IMethodSymbol>().Where(m=>m.Locations.Any(l=>l.IsInSource)).Select(Key).Distinct().ToArray();
 var visited=new HashSet<string>();var todo=new Stack<string>(entries);
 while(todo.Count>0){var id=todo.Pop();if(!visited.Add(id)||!nodes.TryGetValue(id,out var n))continue;foreach(var next in n.Calls)todo.Push(next);}
 var methods=visited.Where(nodes.ContainsKey).Select(k=>nodes[k]).ToArray();
 var tx=methods.SelectMany(m=>m.Transactions).Distinct().Order().ToArray();
 return new {s.Command,EntryMethods=entries,Methods=methods.Select(m=>new{m.Id,m.File,m.Line}).OrderBy(m=>m.Id).ToArray(),
  TransportBound=methods.Any(m=>m.TransportBound),Transactions=tx,ModelWriteEvidence=methods.SelectMany(m=>m.ModelWriteEvidence).Distinct().Order().ToArray(),
  ReadOnly=tx.Length==0&&!methods.Any(m=>m.ModelWriteEvidence.Count>0),
  RequiredFieldEvidence=methods.SelectMany(m=>m.Fields).Distinct().Order().ToArray(),
  LinkEvidence=methods.SelectMany(m=>m.LinkEvidence.Select(e=>new{m.File,m.Line,Expression=e})).ToArray(),
  RevitApi=methods.SelectMany(m=>m.RevitApi).Distinct().Order().ToArray(),
  UnresolvedCalls=methods.SelectMany(m=>m.Unresolved.Select(e=>new{m.File,m.Line,Expression=e})).ToArray()};
}).OrderBy(c=>c.Command).ToArray();
string output=Path.Combine(root,"test-artifacts/source-audit");Directory.CreateDirectory(output);
var report=new{SchemaVersion=1,Analysis="Roslyn symbol-based source call graph; conservative branch union, not runtime path proof. Delegates/reflection require manual review.",SourceFiles=trees.Select(t=>new{Path=Relative(t.FilePath),Sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(t.FilePath))).ToLowerInvariant()}),Commands=commands,Methods=nodes.Values.OrderBy(m=>m.Id),Diagnostics=compilation.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error).Select(d=>d.ToString()).ToArray()};
File.WriteAllText(Path.Combine(output,"backend.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(new{SourceFiles=trees.Length,Methods=nodes.Count,Commands=commands.Length,Output=Relative(Path.Combine(output,"backend.json"))}));
sealed class Method{
 public string Name{get;set;}="";public string Owner{get;set;}="";public string Id{get;set;}="";public string File{get;set;}="";public int Line{get;set;}
 public HashSet<string> Calls{get;set;}=new();public HashSet<string> RevitApi{get;set;}=new();public HashSet<string> Transactions{get;set;}=new();public HashSet<string> Fields{get;set;}=new();public HashSet<string> Unresolved{get;set;}=new();public bool TransportBound{get;set;}public List<string> LinkEvidence{get;set;}=new();public List<string> ModelWriteEvidence{get;set;}=new();
}
