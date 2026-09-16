#if REVIT2026
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using RevitMCP.Core.Site;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
using ComboBox=System.Windows.Controls.ComboBox;
using Binding=System.Windows.Data.Binding;
using Point=System.Windows.Point;
using Ellipse=System.Windows.Shapes.Ellipse;
using MessageBox=System.Windows.MessageBox;
using Panel=System.Windows.Controls.Panel;
using Color=System.Windows.Media.Color;
using Grid=System.Windows.Controls.Grid;

namespace RevitMCP.UI
{
    internal sealed class SiteTerrainControl : UserControl
    {
        private readonly SiteTerrainViewModel vm;
        private readonly StackPanel[] pages=Enumerable.Range(0,4).Select(_=>new StackPanel{Margin=new Thickness(12)}).ToArray();
        private readonly Button[] steps=new Button[4];
        private readonly string[] titles={"地形資料","座標定位","建立地形","土方計算"};
        private readonly Canvas preview=new(){Height=270,Background=Brushes.AliceBlue,ClipToBounds=true};
        private readonly StackPanel csv=new(),cad=new();
        private readonly StackPanel pointCoordinates=new();
        private readonly TextBlock cadCoordinates=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)};
        private readonly DataGrid samples=new(){IsReadOnly=true,AutoGenerateColumns=false,MaxHeight=170};
        private readonly ComboBox[] mapping=new ComboBox[5];
        private readonly ComboBox boundary=new(){DisplayMemberPath="Name",MinWidth=130};
        private readonly DataGrid controls=new(){AutoGenerateColumns=false,CanUserAddRows=false,Height=165};
        private readonly TextBlock coordinates=new(){TextWrapping=TextWrapping.Wrap};
        private readonly TextBlock results=new(){TextWrapping=TextWrapping.Wrap,FontSize=15};
        private readonly System.Windows.Controls.Primitives.UniformGrid resultCards=new(){Columns=2};
        private readonly StackPanel custom=new();
        private string previewMode="平面";
        private bool refreshing;
        public SiteTerrainControl(SiteTerrainViewModel vm)
        {
            this.vm=vm;DataContext=vm;Background=Brushes.White;Foreground=Brushes.Black;
            var root=new DockPanel{LastChildFill=true};Content=root;
            var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
            header.Children.Add(new TextBlock{Text="智慧基地地形／土方中心",FontSize=18,Margin=new Thickness(12),FontWeight=FontWeights.SemiBold});
            var navigation=new System.Windows.Controls.Primitives.UniformGrid{Columns=4};header.Children.Add(navigation);
            for(int i=0;i<4;i++){int index=i;steps[i]=new Button{Content=$"{i+1}. {titles[i]}",Padding=new Thickness(3,8,3,8)};steps[i].Click+=(_,__)=>{if(index==2)vm.RefreshContext();vm.GoToStep(index);};navigation.Children.Add(steps[i]);}
            var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
            Bound(footer,nameof(vm.Status));var previous=new Button{Content="上一步",Margin=new Thickness(6)};previous.Click+=(_,__)=>vm.GoToStep(vm.Step-1);
            var next=new Button{Content="下一步",Margin=new Thickness(6)};next.Click+=(_,__)=>{if(vm.Step==0&&vm.Context==null||vm.Step==1)vm.RefreshContext();vm.GoToStep(vm.Step+1);};
            var actions=new StackPanel{Orientation=Orientation.Horizontal};actions.Children.Add(previous);actions.Children.Add(next);footer.Children.Add(actions);
            var content=new Grid();root.Children.Add(content);foreach(var p in pages)content.Children.Add(new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});

            var p0=pages[0];Label(p0,"選擇測量點或 CAD。CAD 分析完成後會回復模型，不保留匯入元素。");
            Action(p0,"選擇來源檔案…",()=>{var dialog=new OpenFileDialog{Filter="地形來源|*.csv;*.txt;*.dwg;*.dxf",CheckFileExists=true};if(dialog.ShowDialog()==true){vm.FilePath=dialog.FileName;if(!vm.IsCad){vm.ReadColumns();RefreshColumns();}}});
            Bound(p0,nameof(vm.SourceSummary));Choice(p0,"來源單位",nameof(vm.Units),new[]{("m","公尺"),("mm","毫米"),("ft","英呎")});
            p0.Children.Add(csv);p0.Children.Add(cad);
            Choice(csv,"分隔符號",nameof(vm.Delimiter),new[]{("comma","逗號"),("semicolon","分號"),("tab","定位字元"),("space","空白")});
            var headerCheck=new CheckBox{Content="第一列為欄位名稱",Margin=new Thickness(0,6,0,6)};headerCheck.SetBinding(CheckBox.IsCheckedProperty,new Binding(nameof(vm.HasHeader)){Mode=BindingMode.TwoWay});csv.Children.Add(headerCheck);
            Action(csv,"讀取欄位與前 10 列",()=>{vm.ReadColumns();RefreshColumns();});
            string[] roles={"東向／X","北向／Y","高程／Z","點號（選填）","分類代碼（選填）"};
            for(int i=0;i<5;i++){int role=i;Label(csv,roles[i]);mapping[i]=new ComboBox();mapping[i].SelectionChanged+=(_,__)=>{if(!refreshing)vm.SetColumn(role,mapping[role].SelectedIndex-(role>=3?1:0));};csv.Children.Add(mapping[i]);}
            csv.Children.Add(samples);Action(csv,"分析測量點",async()=>await vm.ImportAsync());
            Label(cad,"CAD 定位方式");var placement=new ComboBox{ItemsSource=new[]{"原點對原點","共用座標"},SelectedIndex=0};placement.SelectionChanged+=(_,__)=>vm.Placement=placement.SelectedIndex==1?CadPlacement.Shared:CadPlacement.Origin;cad.Children.Add(placement);
            Action(cad,"分析 CAD 圖層",vm.AnalyzeCad);
            var layers=new DataGrid{ItemsSource=vm.CadLayers,AutoGenerateColumns=false,CanUserAddRows=false,Height=220,EnableRowVirtualization=true};
            layers.Columns.Add(new DataGridCheckBoxColumn{Header="使用",Binding=new Binding("Selected"){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged}});
            foreach(var column in new[]{("圖層","Name"),("幾何","Summary.GeometryTypes"),("數量","Summary.GeometryCount"),("點數","Summary.ValidPointCount"),("高程","Elevation")})layers.Columns.Add(new DataGridTextColumn{Header=column.Item1,Binding=new Binding(column.Item2),IsReadOnly=true});
            cad.Children.Add(layers);Label(cad,"土方邊界候選（選填，不自動選取）");cad.Children.Add(boundary);
            Action(cad,"套用勾選圖層",()=>vm.ApplyCadLayers((boundary.SelectedItem as CadBoundary)?.Name));
            Label(cad,"文字標高配對、AEC／Proxy 與曲線自動加密尚未支援；需複核資料不自動轉成測量点。");
            Details(p0,"資料提醒與診斷",nameof(vm.Detail));

            var p1=pages[1];Action(p1,"重新讀取建築／基地座標",vm.RefreshContext);p1.Children.Add(coordinates);
            p1.Children.Add(cadCoordinates);p1.Children.Add(pointCoordinates);
            Choice(pointCoordinates,"測量點定位方式",nameof(vm.CoordinateMode),new[]{("SharedCoordinates","依共用座標"),("ControlPointAlignment","控制點對位"),("LocalCoordinates","資料已在模型原點座標")});
            Label(pointCoordinates,"控制點表格 — 數值使用下方專案長度單位");controls.ItemsSource=vm.ControlRows;
            foreach(var column in new[]{("點名","Name"),("測量 E","Easting"),("測量 N","Northing"),("測量 Z","Elevation"),("模型 X","ModelX"),("模型 Y","ModelY"),("模型 Z","ModelZ"),("誤差","Residual")})controls.Columns.Add(new DataGridTextColumn{Header=column.Item1,Binding=new Binding(column.Item2){Mode=column.Item2=="Residual"?BindingMode.OneWay:BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},IsReadOnly=column.Item2=="Residual"});
            controls.CellEditEnding+=(_,__)=>Dispatcher.BeginInvoke(new Action(vm.ApplyControlRows));pointCoordinates.Children.Add(controls);
            Action(pointCoordinates,"新增控制點",()=>{vm.ControlRows.Add(new SiteControlRow{Name=$"控制點 {vm.ControlRows.Count+1}"});vm.ApplyControlRows();});
            Action(pointCoordinates,"刪除選取控制點",()=>{if(controls.SelectedItem is SiteControlRow row){vm.ControlRows.Remove(row);vm.ApplyControlRows();}});
            Action(pointCoordinates,"在模型拾取選取列的位置",()=>{if(controls.SelectedItem is SiteControlRow row)vm.PickControl(row);});
            var advanced=new StackPanel();Bound(advanced,nameof(vm.DisplayLengthUnit));Text(advanced,"工具容差（非測量、公司或法規標準）",nameof(vm.DisplayTolerance));p1.Children.Add(new Expander{Header="進階：工具容差",Content=advanced});
            Action(p1,"驗證定位並更新預覽",async()=>await vm.PreviewAsync());Details(p1,"座標差值、RMS 與最大誤差",nameof(vm.Detail));

            var p2=pages[2];Option(p2,"地形類型","Context.Types",nameof(vm.TypeId));Option(p2,"參考樓層","Context.Levels",nameof(vm.LevelId));
            Action(p2,"重新整理類型與樓層",vm.RefreshContext);
            Choice(p2,"品質模式",nameof(vm.ReductionMode),new[]{("精細","精細 — 保留所有有效點"),("平衡","平衡 — 1 m 網格"),("高效能","高效能 — 5 m 網格"),("自訂","自訂網格")});
            Text(custom,"網格間距（專案長度單位）",nameof(vm.DisplayGrid));p2.Children.Add(custom);
            Action(p2,"更新地形預覽",async()=>await vm.PreviewAsync());
            var previewChoice=new ComboBox{ItemsSource=new[]{"平面","高程著色","控制點"},SelectedIndex=0};previewChoice.SelectionChanged+=(_,__)=>{previewMode=(string)previewChoice.SelectedItem;Draw();};p2.Children.Add(previewChoice);p2.Children.Add(preview);
            Label(p2,"預覽最多 2,000 點及 5,000 線段；完整輸入與使用數量見摘要。預覽不建立模型元素。北箭頭依目前位置的真北方向。地形採點集凸包，CAD 線不等同 breakline 約束。");
            Bound(p2,nameof(vm.PreviewSummary));
            Bound(p2,nameof(vm.ConfirmationSummary));Details(p2,"定位與減點品質",nameof(vm.Detail));
            Action(p2,"建立地形…",()=>
            {
                if(MessageBox.Show(vm.ConfirmationSummary,"確認建立地形",MessageBoxButton.OKCancel,MessageBoxImage.Warning)!=MessageBoxResult.OK)return;
                if(vm.PreviewPoints.Count>20000){if(MessageBox.Show("本次使用超過 20,000 點，可能耗時或影響模型效能。仍要執行？","大量點覆核",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;vm.LargePointOverride=true;}
                vm.ConfirmCreate();
            },nameof(vm.CanConfirmCreate));

            var p3=pages[3];Action(p3,"使用目前選取的地形",()=>vm.UseSelection(true));Bound(p3,nameof(vm.TerrainName));Action(p3,"使用目前選取的開挖構件",()=>vm.UseSelection(false));Bound(p3,nameof(vm.CutterName));
            Label(p3,"開挖構件：本模型的樓板、屋頂或地形。分析不改變構件位置。");Action(p3,"試算開挖量",vm.PreviewExcavation);
            Action(p3,"確認執行開挖…",()=>{if(vm.ExcavationPreview.HasValue&&MessageBox.Show($"試算開挖量 {vm.ExcavationPreview:F4} m³。確認修改地形？","確認開挖",MessageBoxButton.OKCancel,MessageBoxImage.Warning)==MessageBoxResult.OK){vm.Confirmed=true;vm.ExecuteExcavation();}});
            Label(p3,"邊界與設計高程");Action(p3,"由選取樓板／閉合模型線取得邊界",vm.UseBoundarySelection);Text(p3,"設計高程（專案長度單位；模型原點基準）",nameof(vm.DisplayTarget));Bound(p3,nameof(vm.DisplayLengthUnit));
            Action(p3,"計算邊界內挖填方",vm.CalculateBoundary);p3.Children.Add(resultCards);p3.Children.Add(results);Action(p3,"3D 定位地形",vm.LocateTerrain);
            Action(p3,"匯出 JSON／CSV／Markdown…",()=>
            {
                if(vm.Result==null)return;var dialog=new SaveFileDialog{Filter="JSON 稽核|*.json|CSV 稽核|*.csv|Markdown 報告|*.md",FileName="terrain-result",AddExtension=true};
                if(dialog.ShowDialog()!=true)return;string audit=Newtonsoft.Json.JsonConvert.SerializeObject(vm.Audit()),result=Newtonsoft.Json.JsonConvert.SerializeObject(vm.Result);
                string json=Newtonsoft.Json.JsonConvert.SerializeObject(new{Audit=vm.Audit(),Result=vm.Result},Newtonsoft.Json.Formatting.Indented);
                string Quote(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
                System.IO.File.WriteAllText(dialog.FileName,dialog.FilterIndex==2?"Field,Value\r\nAudit,"+Quote(audit)+"\r\nResult,"+Quote(result)+"\r\n":dialog.FilterIndex==3?"# 基地土方報告\n\n"+vm.Result+"\n\n```json\n"+json+"\n```\n":json);
            });
            Details(p3,"結果明細與來源識別",nameof(vm.Result));
            vm.PropertyChanged+=(_,__)=>Refresh();preview.SizeChanged+=(_,__)=>Draw();Loaded+=(_,__)=>{if(vm.Context==null)vm.RefreshContext();};Refresh();
        }
        private void Refresh()
        {
            if(refreshing)return;refreshing=true;
            for(int i=0;i<4;i++){((ScrollViewer)pages[i].Parent).Visibility=i==vm.Step?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;steps[i].FontWeight=i==vm.Step?FontWeights.Bold:FontWeights.Normal;string status=vm.StepState(i) switch{SiteStepState.Complete=>"已完成",SiteStepState.Ready=>"可繼續",SiteStepState.Warning=>"需複核",SiteStepState.Stale=>"資料已失效",_=>"尚未開始"};steps[i].Content=$"{i+1}. {titles[i]}\n{status}";}
            csv.Visibility=vm.IsCad?System.Windows.Visibility.Collapsed:System.Windows.Visibility.Visible;cad.Visibility=vm.IsCad?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;custom.Visibility=vm.ReductionMode=="自訂"?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;
            pointCoordinates.Visibility=vm.IsCad?System.Windows.Visibility.Collapsed:System.Windows.Visibility.Visible;cadCoordinates.Visibility=vm.IsCad?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;
            cadCoordinates.Text=vm.CadAnalysis==null?"CAD 尚未分析。請回地形資料步驟選擇定位方式並分析。":vm.CadAnalysis.Placement==CadPlacement.Shared?"CAD 已依共用座標定位；XY、真北與垂直基準已驗證。不重複套用座標轉換。":"CAD 已依原點对原點定位。若需共用座標，請回地形資料步驟變更定位方式並重新分析。";
            var candidate=boundary.SelectedItem as CadBoundary;boundary.ItemsSource=vm.CadAnalysis?.Boundaries;boundary.SelectedItem=(boundary.ItemsSource as System.Collections.Generic.IReadOnlyList<CadBoundary>)?.FirstOrDefault(b=>b.Name==candidate?.Name);
            coordinates.Text=(vm.Context?.CoordinateEvidence??"尚未讀取模型座標。")+"\n長度單位："+vm.DisplayLengthUnit+(vm.Context?.Types.Count==0?"\n模型沒有可用地形類型，請先載入。":"")+(vm.Context?.Levels.Count==0?"\n模型没有可用樓層。":"");
            if(vm.Alignment!=null)for(int i=0;i<Math.Min(vm.ControlRows.Count,vm.Alignment.Residuals.Length);i++)vm.ControlRows[i].Residual=$"{vm.Alignment.Residuals[i]/vm.DisplayFactor:F4} {vm.DisplayLengthUnit}";
            if(!controls.IsKeyboardFocusWithin)controls.Items.Refresh();
            resultCards.Children.Clear();
            if(vm.Result is SiteEarthworkSummary summary)
            {
                foreach(var card in new[]{("面積",summary.Area),("挖方",summary.Cut),("填方",summary.Fill),("淨土方（填－挖）",summary.Net),("最大深度",summary.MaximumDepth)})
                    resultCards.Children.Add(new Border{Background=Brushes.AliceBlue,Padding=new Thickness(10),Margin=new Thickness(3),Child=new TextBlock{Text=card.Item1+"\n"+card.Item2,FontSize=16,TextWrapping=TextWrapping.Wrap}});
                results.Text=$"方法：{summary.Method}\n來源：{summary.Source}\n設計高程：{summary.Target}\n{summary.Warnings}";
            }
            else results.Text=vm.ExcavationPreview.HasValue?$"試算開挖：{vm.ExcavationPreview:F4} m³":"計算結果將顯示於此。";
            refreshing=false;Draw();
        }
        private void RefreshColumns()
        {
            refreshing=true;for(int i=0;i<5;i++){mapping[i].ItemsSource=i>=3?new[]{"不使用"}.Concat(vm.Columns).ToArray():vm.Columns;mapping[i].SelectedIndex=vm.GetColumn(i)+(i>=3?1:0);}
            samples.Columns.Clear();for(int i=0;i<vm.Columns.Count;i++)samples.Columns.Add(new DataGridTextColumn{Header=vm.Columns[i],Binding=new Binding($"[{i}]")});samples.ItemsSource=vm.SampleRows;refreshing=false;
        }
        private void Draw()
        {
            preview.Children.Clear();var points=vm.PreviewPoints;if(points.Count==0)return;var b=TerrainBounds.Of(points);double w=Math.Max(150,preview.ActualWidth),scale=Math.Min((w-30)/Math.Max(1e-9,b.Max.X-b.Min.X),230/Math.Max(1e-9,b.Max.Y-b.Min.Y));
            Point Map(SitePoint p)=>new((w-(b.Max.X-b.Min.X)*scale)/2+(p.X-b.Min.X)*scale,250-(p.Y-b.Min.Y)*scale);
            int stride=Math.Max(1,(int)Math.Ceiling(points.Count/2000d));
            for(int i=0;i<points.Count;i+=stride){var p=points[i];double z=(p.Z-b.Min.Z)/Math.Max(1e-9,b.Max.Z-b.Min.Z);var dot=new Ellipse{Width=3,Height=3,Fill=previewMode=="高程著色"?new SolidColorBrush(Color.FromRgb((byte)(255*z),90,(byte)(255*(1-z)))):Brushes.SteelBlue};var at=Map(p);Canvas.SetLeft(dot,at.X);Canvas.SetTop(dot,at.Y);preview.Children.Add(dot);}
            int segments=0;
            void Line(SitePoint a,SitePoint c,Brush brush){if(++segments>5000)return;var p=Map(a);var q=Map(c);preview.Children.Add(new System.Windows.Shapes.Line{X1=p.X,Y1=p.Y,X2=q.X,Y2=q.Y,Stroke=brush,StrokeThickness=1});}
            if(vm.CadAnalysis!=null)foreach(var g in vm.CadAnalysis.Geometry.Where(g=>vm.CadLayers.Any(l=>l.Selected&&l.Name==g.Layer)))for(int i=1;i<g.Points.Count&&segments<5000;i++)Line(g.Points[i-1],g.Points[i],Brushes.Teal);
            var corners=new SitePoint[]{new(b.Min.X,b.Min.Y,0),new(b.Max.X,b.Min.Y,0),new(b.Max.X,b.Max.Y,0),new(b.Min.X,b.Max.Y,0)};for(int i=0;i<4;i++)Line(corners[i],corners[(i+1)%4],Brushes.LightSlateGray);
            var loop=vm.BoundaryPoints;for(int i=0;i<loop.Count;i++)Line(loop[i],loop[(i+1)%loop.Count],Brushes.DarkOrange);
            if(previewMode=="控制點")foreach(var row in vm.ControlRows){var p=Map(new(row.ModelX*vm.DisplayFactor,row.ModelY*vm.DisplayFactor,row.ModelZ*vm.DisplayFactor));var marker=new TextBlock{Text="＋"+row.Name,Foreground=Brushes.DarkRed};Canvas.SetLeft(marker,p.X);Canvas.SetTop(marker,p.Y);preview.Children.Add(marker);}
            double angle=vm.Context?.SharedToInternal.Rotation??0;preview.Children.Add(new System.Windows.Shapes.Line{X1=w-35,Y1=43,X2=w-35-Math.Sin(angle)*24,Y2=43-Math.Cos(angle)*24,Stroke=Brushes.DarkSlateGray,StrokeThickness=2});
            var north=new TextBlock{Text="N 真北",Foreground=Brushes.DarkSlateGray};Canvas.SetRight(north,8);Canvas.SetTop(north,0);preview.Children.Add(north);
        }
        private static void Label(Panel panel,string text)=>panel.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,7,0,4)});
        private static void Bound(Panel panel,string property){var text=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,6)};text.SetBinding(TextBlock.TextProperty,new Binding(property));panel.Children.Add(text);}
        private static void Text(Panel panel,string label,string property){Label(panel,label);var box=new TextBox{Margin=new Thickness(0,2,0,5)};box.SetBinding(TextBox.TextProperty,new Binding(property){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.LostFocus,ValidatesOnExceptions=true});panel.Children.Add(box);}
        private static void Choice(Panel panel,string label,string property,(string,string)[] choices){Label(panel,label);var box=new ComboBox{ItemsSource=choices.Select(p=>new{Value=p.Item1,Name=p.Item2}).ToArray(),DisplayMemberPath="Name",SelectedValuePath="Value"};box.SetBinding(ComboBox.SelectedValueProperty,new Binding(property){Mode=BindingMode.TwoWay});panel.Children.Add(box);}
        private static void Option(Panel panel,string label,string source,string property){Label(panel,label);var box=new ComboBox{DisplayMemberPath="Name",SelectedValuePath="Id"};box.SetBinding(ComboBox.ItemsSourceProperty,new Binding(source));box.SetBinding(ComboBox.SelectedValueProperty,new Binding(property){Mode=BindingMode.TwoWay});panel.Children.Add(box);}
        private static void Action(Panel panel,string text,Action action,string? enabled=null){var button=new Button{Content=text,Margin=new Thickness(0,5,0,5),Padding=new Thickness(7)};button.Click+=(_,__)=>action();if(enabled!=null)button.SetBinding(Button.IsEnabledProperty,new Binding(enabled));panel.Children.Add(button);}
        private static void Details(Panel panel,string title,string property){var text=new TextBlock{TextWrapping=TextWrapping.Wrap};text.SetBinding(TextBlock.TextProperty,new Binding(property));panel.Children.Add(new Expander{Header=title,Content=text,Margin=new Thickness(0,6,0,6)});}
    }
}
#endif
