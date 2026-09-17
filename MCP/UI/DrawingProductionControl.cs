#if REVIT2026
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using RevitMCP.Core.Drawing;
using Binding = System.Windows.Data.Binding;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace RevitMCP.UI
{
    internal sealed class DrawingProductionControl : UserControl
    {
        private readonly DrawingProductionViewModel vm;
        private readonly StackPanel body=new();
        private readonly ScrollViewer scroll=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8)};
        private int shownStep=-1;
        private int shownEpoch=-1;
        private object? shownData;
        public DrawingProductionControl(DrawingProductionViewModel vm)
        {
            this.vm=vm;DataContext=vm;
            var root=new DockPanel();var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);
            header.Children.Add(new TextBlock{Text="施工圖生產中心",FontSize=20,Margin=new Thickness(8)});
            var steps=new WrapPanel();string[] names={"1 圖紙樣板","2 出圖範圍","3 圖紙計畫","4 預覽建立","5 圖面 QA"};
            for(int i=0;i<names.Length;i++){int step=i;steps.Children.Add(Button(names[i],()=>vm.GoToStep(step)));}header.Children.Add(steps);
            root.Children.Add(header);DockPanel.SetDock(status,Dock.Bottom);root.Children.Add(status);scroll.Content=body;root.Children.Add(scroll);Content=root;
            vm.PropertyChanged+=(_,__)=>{status.Text=vm.Status;if(shownStep!=vm.Step||shownEpoch!=vm.RenderEpoch||!ReferenceEquals(shownData,CurrentData()))Render();};Render();
        }
        private object CurrentData()=>vm.Step switch{0=>vm.Data,1=>vm.Zones,2=>vm.Plan??(object)vm.Package,4=>vm.Issues,_=>vm.Package};
        private static Button Button(string text,Action action){var b=new Button{Content=text,Margin=new Thickness(4),Padding=new Thickness(8,5,8,5)};b.Click+=(_,__)=>action();return b;}
        private void Label(string text)=>body.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8,8,8,2)});
        private TextBox Entry(string label,string initial,Action<string> change)
        {Label(label);var box=new TextBox{Text=initial,Margin=new Thickness(8)};box.TextChanged+=(_,__)=>{change(box.Text);vm.Invalidate();};body.Children.Add(box);return box;}
        private ComboBox Choice(string label,System.Collections.IEnumerable items,Action<object> change)
        {Label(label);var combo=new ComboBox{ItemsSource=items,Margin=new Thickness(8)};combo.SelectionChanged+=(_,__)=>{if(combo.SelectedItem!=null)change(combo.SelectedItem);};body.Children.Add(combo);return combo;}
        private static DataGrid Table(object rows)
        {var grid=new DataGrid{ItemsSource=(System.Collections.IEnumerable)rows,AutoGenerateColumns=false,IsReadOnly=true,Height=280,Margin=new Thickness(8),EnableRowVirtualization=true,EnableColumnVirtualization=true};return grid;}
        private static void Column(DataGrid grid,string title,string path)=>grid.Columns.Add(new DataGridTextColumn{Header=title,Binding=new Binding(path)});
        private void Render()
        {
            if(shownStep!=vm.Step)scroll.ScrollToTop();
            shownStep=vm.Step;shownEpoch=vm.RenderEpoch;shownData=CurrentData();body.Children.Clear();
            switch(vm.Step)
            {
                case 0:
                    body.Children.Add(Button("新增出圖包",vm.StartNewPackage));
                    Label("推薦使用 Revit 圖框 RFA；既有圖紙可沿用完整排版；CAD 可快速轉換為幾何圖框。");
                    var modes=new[]{"目前專案圖紙","RFA 圖框","RVT 樣板圖紙（尚未啟用）","DWG / DXF 圖框"};
                    var mode=new ComboBox{ItemsSource=modes,SelectedIndex=(int)vm.SourceKind,Margin=new Thickness(8)};
                    mode.SelectionChanged+=(_,__)=>{if(mode.SelectedIndex>=0)vm.SelectSource((TemplateSourceKind)mode.SelectedIndex);};body.Children.Add(mode);
                    if(vm.SourceKind==TemplateSourceKind.ExternalRvt){Label("外部 RVT：PARTIAL。跨文件樣板引用尚未通過驗證，請改選目前專案圖紙或 RFA。");break;}
                    if(vm.SourceKind!=TemplateSourceKind.CurrentSheet){ExternalSource();break;}
                    Label("此來源包含完整圖紙排版，可沿用 Viewport / Legend / Schedule 位置。");
                    body.Children.Add(Button("讀取專案圖紙與設定",vm.Refresh));
                    long source=0;Choice("樣板圖紙",vm.Sheets,item=>source=((DrawingChoice)item).Id);
                    body.Children.Add(Button("擷取樣板",()=>{if(source>0)vm.Extract(source);}));
                    var blueprint=vm.Package.Profile.Blueprint;
                    if(blueprint.SourceSheetId>0)Label($"樣板：{blueprint.SourceSheetNumber} {blueprint.SourceSheetName}\n圖框：{blueprint.TitleBlockFamilyName} {blueprint.TitleBlockTypeName}\n主要視埠 {blueprint.Viewports.Count}／圖例 {blueprint.Legends.Count}／明細表 {blueprint.Schedules.Count}");
                    foreach(var warning in blueprint.Warnings)Label(warning);
                    Label("未啟用：自動尺寸、Matchline／View Reference、共用詳圖及既有公司圖紙納管。");
                    if(blueprint.SourceSheetId>0)body.Children.Add(Button("從樣板圖紙重新擷取",()=>vm.Extract(blueprint.SourceSheetId)));
                    foreach(var shared in blueprint.Legends.Concat(blueprint.Schedules))
                    {var reuse=new CheckBox{Content="每張圖紙沿用："+shared.SourceViewName,IsChecked=shared.Reuse,Margin=new Thickness(8,2,8,2)};reuse.Checked+=(_,__)=>{shared.Reuse=true;vm.Invalidate();};reuse.Unchecked+=(_,__)=>{shared.Reuse=false;vm.Invalidate();};body.Children.Add(reuse);}
                    Choice("已保存樣板",vm.Data.Profiles,item=>vm.UseProfile(RevitDrawingService.Clone((DrawingTemplateProfile)item)));
                    Choice("更新既有出圖包",vm.Data.Packages,item=>vm.UsePackage(RevitDrawingService.Clone((DrawingPackageDefinition)item)));
                    Entry("樣板名稱",vm.Package.Profile.ProfileName,v=>vm.Package.Profile.ProfileName=v);
                    body.Children.Add(Button("保存為出圖樣板／新版本",vm.SaveProfile));
                    body.Children.Add(Button("下一步",()=>vm.GoToStep(1)));break;
                case 1:
                    Entry("出圖包名稱",vm.Package.PackageName,v=>vm.Package.PackageName=v);
                    Entry("專業",vm.Package.Discipline,v=>vm.Package.Discipline=v);
                    Entry("出圖類型",vm.Package.DrawingType,v=>vm.Package.DrawingType=v);
                    int advancedStart=body.Children.Count;
                    Entry("階段名稱（僅命名 token）",vm.Package.Phase,v=>vm.Package.Phase=v);
                    var strategy=Choice("視圖方式",new[]{"建立分區從屬視圖","複製來源視圖（不複製詳圖）","使用現有視圖（保留既有設定）"},item=>{vm.Package.Profile.ViewStrategy=(string)item=="建立分區從屬視圖"?DrawingViewStrategy.Dependent:(string)item=="使用現有視圖（保留既有設定）"?DrawingViewStrategy.Existing:DrawingViewStrategy.Duplicate;vm.Invalidate();});
                    strategy.SelectedIndex=vm.Package.Profile.ViewStrategy==DrawingViewStrategy.Dependent?0:vm.Package.Profile.ViewStrategy==DrawingViewStrategy.Existing?2:1;
                    var advanced=new StackPanel();while(body.Children.Count>advancedStart){var child=body.Children[advancedStart];body.Children.RemoveAt(advancedStart);advanced.Children.Add(child);}
                    Label("樓層依高程排序；唯一平面視圖自動選取；多個來源時才需要指定。");
                    var levelChecks=new System.Collections.Generic.List<(DrawingChoice Level,CheckBox Check)>();
                    body.Children.Add(Button("全選樓層",()=>{foreach(var entry in levelChecks)entry.Check.IsChecked=true;}));
                    body.Children.Add(Button("清除樓層選取",()=>{foreach(var entry in levelChecks)entry.Check.IsChecked=false;}));
                    var levelFrom=Choice("起始樓層",vm.Levels,_=>{});var levelTo=Choice("結束樓層",vm.Levels,_=>{});
                    body.Children.Add(Button("選取樓層範圍",()=>{if(levelFrom.SelectedItem is DrawingChoice from&&levelTo.SelectedItem is DrawingChoice to)foreach(var entry in levelChecks)entry.Check.IsChecked=entry.Level.Elevation>=Math.Min(from.Elevation,to.Elevation)&&entry.Level.Elevation<=Math.Max(from.Elevation,to.Elevation);}));
                    foreach(var level in vm.Levels)
                    {
                        var line=new StackPanel{Margin=new Thickness(8)};var check=new CheckBox{Content=$"{level.Name}（{level.Elevation*304.8:0.###} mm）",IsChecked=vm.Package.Levels.Any(l=>l.Id==level.Id)};line.Children.Add(check);levelChecks.Add((level,check));
                        var available=vm.LevelSources.TryGetValue(level.Id,out var candidates)?candidates:Array.Empty<DrawingChoice>();
                        var sourceViews=new ComboBox{Margin=new Thickness(12,3,0,3),ItemsSource=available,SelectedItem=available.FirstOrDefault(v=>vm.Package.SourceViewsByLevel.TryGetValue(level.Id,out var id)&&id==v.Id)};if(available.Length!=1)line.Children.Add(sourceViews);
                        check.Checked+=(_,__)=>{vm.SelectLevel(level,true);sourceViews.SelectedItem=available.FirstOrDefault(v=>vm.Package.SourceViewsByLevel.TryGetValue(level.Id,out var id)&&id==v.Id);};
                        check.Unchecked+=(_,__)=>vm.SelectLevel(level,false);
                        sourceViews.SelectionChanged+=(_,__)=>{if(sourceViews.SelectedItem is DrawingChoice view){vm.Package.SourceViewsByLevel[level.Id]=view.Id;vm.Invalidate();}};
                        line.Children.Add(new TextBlock{Text=available.Length==0?"缺少來源視圖":available.Length==1?"自動使用："+available[0].Name:"請選擇此樓層的來源平面視圖"});body.Children.Add(line);
                    }
                    Label("分區：未勾選任何分區即為「不分區」，每個樓層建立一張圖紙。");
                    foreach(var zone in vm.Zones)
                    {var check=new CheckBox{Content=zone.ZoneName,IsChecked=vm.Package.Zones.Any(z=>z.ZoneId==zone.ZoneId),Margin=new Thickness(8,2,8,2)};check.Checked+=(_,__)=>{if(!vm.Package.Zones.Any(z=>z.ZoneId==zone.ZoneId))vm.Package.Zones.Add(zone);vm.Invalidate();};check.Unchecked+=(_,__)=>{vm.Package.Zones.RemoveAll(z=>z.ZoneId==zone.ZoneId);vm.Invalidate();};body.Children.Add(check);}
                    var gridPanel=new StackPanel();gridPanel.Children.Add(new TextBlock{Text="選四條正交直線網格（兩條 X 邊界、兩條 Y 邊界）"});
                    var gridList=new ListBox{ItemsSource=vm.Grids,SelectionMode=SelectionMode.Multiple,Height=120};gridPanel.Children.Add(gridList);
                    var zoneName=new TextBox{ToolTip="分區名稱",Margin=new Thickness(4)};gridPanel.Children.Add(new TextBlock{Text="分區名稱"});gridPanel.Children.Add(zoneName);
                    var padding=new TextBox{Text="0",Margin=new Thickness(4)};gridPanel.Children.Add(new TextBlock{Text="外擴距離（mm）"});gridPanel.Children.Add(padding);
                    gridPanel.Children.Add(Button("加入網格分區",()=>{if(double.TryParse(padding.Text,out double mm))vm.AddGridZone(zoneName.Text,gridList.SelectedItems.Cast<DrawingChoice>().Select(g=>g.Id).ToArray(),mm);}));
                    body.Children.Add(new Expander{Header="以網格新增分區",Content=gridPanel,Margin=new Thickness(8)});
                    Entry("圖號規則（例如 {Discipline}-{Sequence:000}）",vm.Package.Profile.NumberingRule,v=>vm.Package.Profile.NumberingRule=v);
                    Entry("圖名規則",vm.Package.Profile.NamingRule,v=>vm.Package.Profile.NamingRule=v);
                    advancedStart=body.Children.Count;
                    Entry("視圖名稱規則",vm.Package.Profile.ViewNamingRule,v=>vm.Package.Profile.ViewNamingRule=v);
                    if(vm.Package.Profile.Blueprint.Viewports.Count==1)
                    {
                        var slot=vm.Package.Profile.Blueprint.Viewports[0];Label($"目前比例 1:{slot.Scale}"+(slot.ScaleControlled?"（由 View Template 控制）":""));
                        var templates=new ComboBox{Margin=new Thickness(8)};body.Children.Add(templates);body.Children.Add(Button("讀取可用視圖樣板",()=>vm.ReadViewTemplates(choices=>{templates.ItemsSource=choices;templates.SelectedItem=choices.FirstOrDefault(c=>c.Id==slot.ViewTemplateId);})));
                        var scaleBox=new TextBox{Text=slot.Scale.ToString(),Margin=new Thickness(8)};body.Children.Add(scaleBox);
                        body.Children.Add(Button("套用視圖樣板／比例規則",()=>{if(templates.SelectedItem is DrawingChoice selected&&int.TryParse(scaleBox.Text,out int scale))vm.ConfigureViewRule(selected.Id,scale);}));
                    }
                    var preserve=new CheckBox{Content="保留人工修改（取消後，須重新預覽並確認套用樣板）",IsChecked=vm.Package.PreserveManualChanges,Margin=new Thickness(8)};
                    preserve.Checked+=(_,__)=>{vm.Package.PreserveManualChanges=true;vm.Invalidate();};preserve.Unchecked+=(_,__)=>{vm.Package.PreserveManualChanges=false;vm.Invalidate();};body.Children.Add(preserve);
                    Label("沿用樣板圖紙資訊（僅明確勾選項目）");
                    foreach(var parameter in vm.Package.Profile.Blueprint.SheetParameterCopyPolicy.Where(p=>p.Policy==DrawingCopyPolicy.UserSelectable))
                    {
                        var check=new CheckBox{Content=(parameter.Owner=="View"?"視圖／":parameter.Owner=="TitleBlock"?"圖框／":"圖紙／")+parameter.Name+"："+parameter.Value,IsChecked=parameter.Selected,Margin=new Thickness(8,2,8,2)};check.Checked+=(_,__)=>{parameter.Selected=true;vm.Invalidate();};check.Unchecked+=(_,__)=>{parameter.Selected=false;vm.Invalidate();};body.Children.Add(check);
                        string[] fields={"","Level","Zone","DrawingType","Discipline","Phase"};var mapping=new ComboBox{ItemsSource=new[]{"沿用樣板值","映射樓層","映射分區","映射圖別","映射專業","映射階段"},SelectedIndex=Array.IndexOf(fields,parameter.SemanticField),Margin=new Thickness(20,0,8,4)};
                        mapping.SelectionChanged+=(_,__)=>{if(mapping.SelectedIndex>=0){parameter.SemanticField=fields[mapping.SelectedIndex];vm.Invalidate();}};body.Children.Add(mapping);
                    }
                    while(body.Children.Count>advancedStart){var child=body.Children[advancedStart];body.Children.RemoveAt(advancedStart);advanced.Children.Add(child);}
                    body.Children.Add(new Expander{Header="進階：視圖方式、樣板、比例、參數",Content=advanced,IsExpanded=false,Margin=new Thickness(8)});
                    var generate=Button("產生圖紙計畫",vm.GeneratePlan);generate.SetBinding(IsEnabledProperty,new Binding(nameof(vm.CanGenerate)));body.Children.Add(generate);var generateReason=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8)};generateReason.SetBinding(TextBlock.TextProperty,new Binding(nameof(vm.GenerateBlockedReason)));body.Children.Add(generateReason);break;
                case 2:
                    Label(vm.Summary);if(vm.Plan==null)break;
                    Label("圖框："+vm.Package.Profile.Blueprint.TitleBlockFamilyName+" / "+vm.Package.Profile.Blueprint.TitleBlockTypeName);
                    var grid=Table(vm.Plan.Rows);Column(grid,"狀態","Status");Column(grid,"圖號","SheetNumber");Column(grid,"圖名","SheetName");Column(grid,"樓層","Level.Name");Column(grid,"分區","Zone.ZoneName");Column(grid,"來源平面","SourceViewName");Column(grid,"圖框","TitleBlockName");Column(grid,"樣板","ProfileName");Column(grid,"視圖","ViewName");Column(grid,"版面差異","DifferenceText");Column(grid,"需檢查","IssueText");body.Children.Add(grid);
                    Label("樣板："+vm.Package.Profile.ProfileName+" V"+vm.Package.Profile.ProfileVersion);
                    body.Children.Add(Button("此張套用樣板（重新預覽）",()=>{if(grid.SelectedItem is DrawingPlanRow row){vm.Package.ApplyTemplateKeys.Add(row.Key);vm.GeneratePlan();}}));
                    body.Children.Add(Button("此張保留人工修改（重新預覽）",()=>{if(grid.SelectedItem is DrawingPlanRow row){vm.Package.ApplyTemplateKeys.Remove(row.Key);vm.Package.PreserveManualChanges=true;vm.GeneratePlan();}}));
                    var search=new TextBox{Margin=new Thickness(8)};search.TextChanged+=(_,__)=>grid.ItemsSource=vm.Plan.Rows.Where(r=>(r.SheetNumber+" "+r.SheetName).Contains(search.Text,StringComparison.OrdinalIgnoreCase)).ToArray();body.Children.Add(search);
                    var numberEdit=new TextBox{Margin=new Thickness(8)};var nameEdit=new TextBox{Margin=new Thickness(8)};Label("選取圖紙後調整圖號／圖名，再重新預覽");body.Children.Add(numberEdit);body.Children.Add(nameEdit);
                    grid.SelectionChanged+=(_,__)=>{if(grid.SelectedItem is DrawingPlanRow row){numberEdit.Text=row.SheetNumber;nameEdit.Text=row.SheetName;}};
                    body.Children.Add(Button("套用此張圖號／圖名並重新預覽",()=>{if(grid.SelectedItem is DrawingPlanRow row){vm.Package.NumberOverrides[row.Key]=numberEdit.Text;vm.Package.NameOverrides[row.Key]=nameEdit.Text;vm.GeneratePlan();}}));
                    var rowSources=new ComboBox{Margin=new Thickness(8)};body.Children.Add(rowSources);
                    body.Children.Add(Button("讀取此樓層可用視圖",()=>{if(grid.SelectedItem is DrawingPlanRow row)vm.ReadSources(row.Level.Id,choices=>rowSources.ItemsSource=choices);}));
                    body.Children.Add(Button("指定此張來源視圖並重新預覽",()=>{if(grid.SelectedItem is DrawingPlanRow row&&rowSources.SelectedItem is DrawingChoice sourceView){vm.Package.SourceViewsByKey[row.Key]=sourceView.Id;vm.GeneratePlan();}}));
                    Label("選取圖紙查看依分區與比例估計的版面。灰色：圖框；白色：可用出圖區；藍色：預計視埠；紅色：超框／重疊；橙色：標題位置示意，文字包絡須人工複核。建立後仍驗證實際視埠。");
                    var layoutHost=new ContentControl{Content=Layout(vm.Plan.Package.Profile.Blueprint,null,vm.Plan.Package.Profile)};body.Children.Add(layoutHost);
                    grid.SelectionChanged+=(_,__)=>{if(grid.SelectedItem is DrawingPlanRow row)layoutHost.Content=Layout(vm.Plan.Package.Profile.Blueprint,row.Zone,vm.Plan.Package.Profile,row.PreviewBounds);};
                    body.Children.Add(Button("排除此張並重新預覽",()=>{if(grid.SelectedItem is DrawingPlanRow row){vm.Package.ExcludedKeys.Add(row.Key);vm.GeneratePlan();}}));
                    var confirm=Button("確認計畫",()=>vm.GoToStep(3));confirm.IsEnabled=vm.Plan.CanApply;body.Children.Add(confirm);Label(vm.ApplyBlockedReason);break;
                case 3:
                    Label(vm.Summary);Label("將建立圖紙、視圖、視埠與追溯資料。人工修改預設保留。任一建立或 read-back 失敗會回復整批。");
                    var apply=Button("確認建立／更新",()=>{if(MessageBox.Show(vm.ConfirmationSummary+"\n圖框："+vm.Package.Profile.Blueprint.TitleBlockFamilyName+"\n"+string.Join("\n",vm.Package.Profile.Blueprint.Warnings),"確認建立施工圖",MessageBoxButton.OKCancel,MessageBoxImage.Question)==MessageBoxResult.OK)vm.ConfirmAndApply();});apply.SetBinding(IsEnabledProperty,new Binding(nameof(vm.CanApply)));body.Children.Add(apply);var applyReason=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8)};applyReason.SetBinding(TextBlock.TextProperty,new Binding(nameof(vm.ApplyBlockedReason)));body.Children.Add(applyReason);break;
                case 4:
                    Label("圖面 QA 僅為工具檢查結果，不代表施工核准。");body.Children.Add(Button("重新檢查",vm.RunQa));
                    Label(vm.QaSummary);var sheetList=Table(vm.SheetList);Column(sheetList,"圖號","SheetNumber");Column(sheetList,"圖名","SheetName");Column(sheetList,"樓層","Level");Column(sheetList,"分區","Zone");Column(sheetList,"圖別","DrawingType");Column(sheetList,"QA 狀態","Readiness");body.Children.Add(sheetList);
                    body.Children.Add(Button("開啟目錄選取圖紙",()=>{if(sheetList.SelectedItem is DrawingSheetStatus row)vm.Open(row.SheetId);}));
                    var qa=Table(vm.Issues);Column(qa,"圖號","SheetNumber");Column(qa,"等級","Severity");Column(qa,"問題","Message");body.Children.Add(qa);
                    body.Children.Add(Button("開啟問題圖紙",()=>{if(qa.SelectedItem is DrawingQaIssue issue&&issue.SheetId>0)vm.Open(issue.SheetId);}));
                    body.Children.Add(Button("開啟第一張圖",()=>{if(vm.ResultIds.Length>0)vm.Open(vm.ResultIds[0]);}));break;
            }
        }
        private void ExternalSource()
        {
            bool cad=vm.SourceKind==TemplateSourceKind.Cad;
            body.Children.Add(Button("選擇檔案",()=>{var dialog=new Microsoft.Win32.OpenFileDialog{Filter=cad?"CAD 圖框|*.dwg;*.dxf":"Revit 圖框|*.rfa",CheckFileExists=true};if(dialog.ShowDialog()==true)vm.SelectExternalFile(dialog.FileName);}));
            Label(string.IsNullOrEmpty(vm.ExternalPath)?"尚未選擇檔案":System.IO.Path.GetFileName(vm.ExternalPath));
            if(cad)
            {
                var units=new[]{"Auto","mm","cm","m","inch","ft"};var combo=new ComboBox{ItemsSource=units,SelectedItem=vm.CadUnit,Margin=new Thickness(8)};
                combo.SelectionChanged+=(_,__)=>{if(combo.SelectedItem is string unit&&unit!=vm.CadUnit)vm.SetCadUnit(unit);};Label("CAD 單位（Auto 為建議，需確認）");body.Children.Add(combo);
                body.Children.Add(Button("選擇 Revit 圖框族樣板 .rft",()=>{var dialog=new Microsoft.Win32.OpenFileDialog{Filter="圖框族樣板|*.rft",CheckFileExists=true};if(dialog.ShowDialog()==true)vm.SetRft(dialog.FileName);}));
            }
            body.Children.Add(Button("分析圖框",vm.AnalyzeExternal));
            if(vm.ExternalAnalysis is ExternalTitleBlockAnalysis a)
            {
                Label($"Family：{a.FamilyName}\n偵測大小：{a.Bounds.Width*304.8:0.##} × {a.Bounds.Height*304.8:0.##} mm\n建議尺寸：{a.SizeSuggestion}（僅建議，請確認）");
                var expectedSize=Choice("預期紙張尺寸",new[]{"A0","A1","A2","A3","A4"},item=>{vm.ExpectedPaperSize=(string)item;vm.SizeAndUnitConfirmed=false;});expectedSize.SelectedItem=vm.ExpectedPaperSize;
                var sizeText=new TextBlock{Text=vm.SizeComparison,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8)};body.Children.Add(sizeText);expectedSize.SelectionChanged+=(_,__)=>sizeText.Text=vm.SizeComparison;
                var types=Choice("圖框類型",a.Types,item=>vm.SelectExternalType((string)item));types.SelectedItem=vm.SelectedExternalType;types.SelectionChanged+=(_,__)=>sizeText.Text=vm.SizeComparison;
                Label("可用參數："+string.Join("、",a.Parameters));if(cad)Label("CAD 圖層："+string.Join("、",a.Layers));
                body.Children.Add(Layout(new SheetTemplateBlueprint{TitleBlockBounds=a.Bounds}));
                var accepted=new CheckBox{Content="我已確認圖框尺寸、方向及匯入單位",Margin=new Thickness(8)};accepted.SetBinding(CheckBox.IsCheckedProperty,new Binding(nameof(vm.SizeAndUnitConfirmed)){Mode=BindingMode.TwoWay});body.Children.Add(accepted);
                if(a.ExistingFamily)Label("同名圖框已存在：僅可使用目前專案版本；不覆寫公司 Family。取消可重新選檔。");
                body.Children.Add(Button(a.ExistingFamily?"使用目前專案圖框":cad?"建立 Revit 圖框":"載入圖框",()=>{if(MessageBox.Show("載入圖框："+a.FamilyName+" / "+vm.SelectedExternalType+(a.ExistingFamily?"\n使用專案現有版本。":""),"確認圖框",MessageBoxButton.OKCancel,MessageBoxImage.Question)==MessageBoxResult.OK)vm.LoadExternal(a.ExistingFamily,true);}));
            }
            if(vm.Package.Profile.Blueprint.TitleBlockTypeId>0)
            {
                Label("此來源只有圖框，工具將使用自動單一主視圖配置。可用出圖區由下列邊距決定；不會自動辨識資訊欄。");
                var p=vm.Package.Profile;Entry("樣板名稱",p.ProfileName,v=>p.ProfileName=v);
                void Margin(string label,double initial,Action<double> assign)=>Entry(label,initial.ToString(System.Globalization.CultureInfo.InvariantCulture),v=>assign(double.TryParse(v,out var n)?n:double.NaN));
                Margin("左邊距（mm）",p.MarginLeftMm,v=>p.MarginLeftMm=v);Margin("右邊距（mm）",p.MarginRightMm,v=>p.MarginRightMm=v);Margin("上邊距（mm）",p.MarginTopMm,v=>p.MarginTopMm=v);Margin("下邊距／資訊欄（mm）",p.MarginBottomMm,v=>p.MarginBottomMm=v);
                Margin("水平位移（mm，向右為正）",p.OffsetXmm,v=>p.OffsetXmm=v);Margin("垂直位移（mm，向上為正）",p.OffsetYmm,v=>p.OffsetYmm=v);
                foreach(var warning in p.Blueprint.Warnings)Label(warning);
                body.Children.Add(Button("保存出圖樣板",vm.SaveProfile));body.Children.Add(Button("用這個圖框開始",()=>vm.GoToStep(1)));
            }
        }
        private static Canvas Layout(SheetTemplateBlueprint blueprint,DrawingZone? zone=null,DrawingTemplateProfile? profile=null,DrawingBounds? projected=null)
        {
            var canvas=new Canvas{Width=360,Height=270,Background=Brushes.WhiteSmoke,Margin=new Thickness(8)};var bounds=blueprint.TitleBlockBounds;if(!bounds.Valid)return canvas;
            double scale=Math.Min(340/bounds.Width,250/bounds.Height);
            void Draw(DrawingBounds b,Brush stroke){var rectangle=new Rectangle{Width=Math.Max(1,b.Width*scale),Height=Math.Max(1,b.Height*scale),Stroke=stroke,StrokeThickness=2};Canvas.SetLeft(rectangle,10+(b.MinX-bounds.MinX)*scale);Canvas.SetTop(rectangle,10+(bounds.MaxY-b.MaxY)*scale);canvas.Children.Add(rectangle);}
            Draw(bounds,Brushes.Gray);
            DrawingBounds safe;
            try{safe=profile!=null&&blueprint.AutoLayout?AutoSheetLayoutService.SafeBounds(profile):bounds;}
            catch(ArgumentException e){canvas.Children.Add(new TextBlock{Text=e.Message,Foreground=Brushes.Red,TextWrapping=TextWrapping.Wrap});return canvas;}
            if(profile!=null&&blueprint.AutoLayout){Draw(safe,Brushes.LightGray);((Rectangle)canvas.Children[canvas.Children.Count-1]).Fill=Brushes.White;}
            var slots=blueprint.Slots.Select(RevitDrawingService.Clone).ToArray();
            if(zone!=null&&!zone.IsUnzoned)foreach(var slot in slots.Where(s=>s.Role=="MAIN_PLAN"&&s.Scale>0)){double w=Math.Max(slot.Bounds.Width,zone.Bounds.Width/slot.Scale),h=Math.Max(slot.Bounds.Height,zone.Bounds.Height/slot.Scale);slot.Bounds=new(slot.AbsoluteX-w/2,slot.AbsoluteY-h/2,slot.AbsoluteX+w/2,slot.AbsoluteY+h/2);}
            if(projected!=null)foreach(var main in slots.Where(s=>s.Role=="MAIN_PLAN"))main.Bounds=projected;
            foreach(var slot in slots)
            {
                Draw(slot.Bounds,!safe.Contains(slot.Bounds)||slots.Any(s=>!ReferenceEquals(s,slot)&&s.Bounds.Overlaps(slot.Bounds))?Brushes.Red:Brushes.DodgerBlue);
                if(slot.Role!="SCHEDULE")
                {var mark=new TextBlock{Text="標題",Foreground=Brushes.DarkOrange,FontSize=10};Canvas.SetLeft(mark,10+(slot.Bounds.MinX+slot.TitleOffset.X-bounds.MinX)*scale);Canvas.SetTop(mark,10+(bounds.MaxY-slot.Bounds.MinY-slot.TitleOffset.Y)*scale);canvas.Children.Add(mark);}
            }
            return canvas;
        }
    }
}
#endif
