#if REVIT2026
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using RevitMCP.Core.Site;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
using ComboBox=System.Windows.Controls.ComboBox;
using Binding=System.Windows.Data.Binding;
using MessageBox=System.Windows.MessageBox;

namespace RevitMCP.UI
{
    internal sealed partial class SiteTerrainControl
    {
        private readonly StackPanel earthBoundary=new(),earthCutter=new(),earthDirectTarget=new(),earthLevelTarget=new();
        private readonly DataGrid earthworkRows=new(){AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,Height=240,EnableRowVirtualization=true,EnableColumnVirtualization=true,SelectionMode=DataGridSelectionMode.Single};
        private EarthworkRecord? selectedEarthworkRow;
        private readonly ComboBox profileSelector=new(){DisplayMemberPath="ProfileName",SelectedValuePath="ProfileGuid"};
        internal string[] FixtureProfileNames=>profileSelector.Items.Cast<EarthworkProjectProfile>().Select(p=>p.ProfileName).ToArray();
        private readonly Canvas earthworkBoundaryPreview=new(){Height=160,Background=System.Windows.Media.Brushes.AliceBlue,ClipToBounds=true};
        private sealed record DisplayRow(EarthworkRecord Record,string Number,string Name,string Area,string Cut,string Fill,string Export,string Import,long Trips,string Cost,string Calculation,string Review,string Profile);
        private void BuildEarthworkPage(StackPanel p)
        {
            Action(p,"新增土方區",vm.NewEarthworkZone);LiveText(p,"土方區編號",nameof(vm.ZoneNumber));LiveText(p,"土方區名稱",nameof(vm.ZoneName));
            Action(p,"重新讀取本專案 Profiles／土方紀錄與單位",vm.RefreshContext);
            Action(p,"使用目前選取的地形",()=>vm.UseSelection(true));Bound(p,nameof(vm.TerrainName));
            Choice(p,"計算方式",nameof(vm.EarthworkMode),new[]{("BoundaryTin","封閉範圍／Floor boundary／Model Curve loop"),("RevitCutter","Cutter 試算（模型回復，不執行開挖）")});
            p.Children.Add(earthBoundary);p.Children.Add(earthCutter);
            Action(earthBoundary,"由選取樓板／閉合模型線取得邊界",vm.UseBoundarySelection);Label(earthBoundary,"只接受無孔洞樓板或閉合直線凸平面邊界，不以 bounding box 代替。");
            Choice(earthBoundary,"設計高程來源",nameof(vm.TargetMode),new[]{("Direct","直接高程（模型原點基準）"),("LevelOffset","Level ＋ Offset")});
            earthBoundary.Children.Add(earthDirectTarget);earthBoundary.Children.Add(earthLevelTarget);
            Label(earthDirectTarget,"設計高程（專案長度單位；請明確輸入，可為 0）");earthworkTargetInput=new TextBox{Margin=new Thickness(0,2,0,5)};
            earthworkTargetInput.SetBinding(TextBox.TextProperty,new Binding(nameof(vm.TargetElevationText)){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});earthDirectTarget.Children.Add(earthworkTargetInput);
            Option(earthLevelTarget,"參考 Level","Context.Levels",nameof(vm.EarthworkLevelId));LiveText(earthLevelTarget,"Offset（專案長度單位；請明確輸入，可為 0）",nameof(vm.TargetOffsetText));
            Bound(earthBoundary,nameof(vm.DisplayLengthUnit));Bound(earthBoundary,nameof(vm.CalculationReadiness));
            Action(earthCutter,"使用目前選取的開挖構件",()=>vm.UseSelection(false));Bound(earthCutter,nameof(vm.CutterName));Label(earthCutter,"Floor／Roof／Toposolid。試算會完整回復模型；Cutter 只提供挖方，未分析設計填方與面積。");
            Label(p,"土方成本設定檔（自行選取）");var profiles=profileSelector;profiles.SetBinding(ComboBox.ItemsSourceProperty,new Binding(nameof(vm.EarthworkProfiles)));profiles.SetBinding(ComboBox.SelectedValueProperty,new Binding(nameof(vm.SelectedProfileGuid)){Mode=BindingMode.TwoWay});p.Children.Add(profiles);
            void EditProfile(bool edit)
            {
                var source=edit?vm.EarthworkProfiles.SingleOrDefault(x=>x.ProfileGuid==vm.SelectedProfileGuid):null;if(edit&&source==null)return;
                var dialog=new EarthworkProfileWindow(source);if(dialog.ShowDialog()!=true||dialog.Profile==null)return;
                var text=$"將儲存成本設定檔：{dialog.Profile.ProfileName}\n幣別：{dialog.Profile.Currency}；體積基準：{dialog.Profile.VolumeUnit}\n{dialog.Editor.SoilPreview}\n{dialog.Editor.TruckPreview}\n\n歷史土方區保留原設定版本與成本，需明確重新計算才更新。";
                if(MessageBox.Show(text,"確認儲存成本設定檔",MessageBoxButton.OKCancel,MessageBoxImage.Question)==MessageBoxResult.OK)vm.SaveEarthworkProfile(dialog.Profile,true);
            }
            Action(p,"新增成本設定檔…",()=>EditProfile(false));Action(p,"編輯所選設定檔…",()=>EditProfile(true));
            Action(p,"複製設定檔…",()=>{if(MessageBox.Show("以新的身分建立 V1 複本？原設定與歷史結果保留。","複製成本設定檔",MessageBoxButton.OKCancel)==MessageBoxResult.OK)vm.CloneEarthworkProfile(true);});
            Action(p,"封存所選設定檔…",()=>{if(MessageBox.Show("封存後不出現在新計算選單；歷史版本與結果仍保留。","封存成本設定檔",MessageBoxButton.OKCancel)==MessageBoxResult.OK)vm.ArchiveEarthworkProfile(true);});
            Action(p,"預覽土方",vm.PreviewEarthwork,nameof(vm.CanPreviewEarthwork));
            p.Children.Add(earthworkBoundaryPreview);Label(p,"平面範圍預覽；尚未提供分片挖填色圖，不以此圖取代 TIN 數量。3D 定位使用既有視圖，不新增模型幾何。");earthworkBoundaryPreview.SizeChanged+=(_,__)=>DrawEarthworkBoundary();
            var resultRegion=new StackPanel{MinHeight=270};resultRegion.Children.Add(resultCards);resultRegion.Children.Add(results);p.Children.Add(resultRegion);
            Bound(p,nameof(vm.CalculationStateText));Bound(p,nameof(vm.EstimateSummary));Details(p,"查看計算依據／費用拆分",nameof(vm.CalculationBasisText));
            Bound(p,nameof(vm.ProfileStaleWarning));Action(p,"使用最新設定重新計算",vm.RecalculateLatestProfile);
            Action(p,"3D 定位本區邊界／Cutter",vm.LocateEarthworkZone);
            Action(p,"加入／更新土方明細…",()=>{if(MessageBox.Show($"將儲存區 {vm.ZoneNumber}／{vm.ZoneName} 的分析紀錄；不修改 Terrain 或 Cutter。\n\n{vm.EstimateSummary}","確認儲存土方區",MessageBoxButton.OKCancel,MessageBoxImage.Question)==MessageBoxResult.OK)vm.SaveEarthworkZone(true);},nameof(vm.CanSaveZone));
            var write=new StackPanel();Action(write,"確認執行 Revit 開挖…",()=>{if(vm.ExcavationPreview.HasValue&&MessageBox.Show($"將實際修改 Terrain。試算量 {vm.FormatEarthworkVolume(vm.ExcavationPreview.Value)}；確認後執行並 read-back。","確認開挖",MessageBoxButton.OKCancel,MessageBoxImage.Warning)==MessageBoxResult.OK){vm.Confirmed=true;vm.ExecuteExcavation();}});p.Children.Add(new Expander{Header="實際開挖（修改模型，與分析分開）",Content=write});
            Label(p,"土方明細（點選區可載入來源並 3D 定位；需重新預覽才更新）");
            foreach(var col in new[]{("區號","Number"),("區名","Name"),("面積","Area"),("挖方","Cut"),("填方","Fill"),("外運","Export"),("外購","Import"),("外運車次","Trips"),("預估成本","Cost"),("計算狀態","Calculation"),("複核狀態","Review"),("設定檔／版本","Profile")})earthworkRows.Columns.Add(new DataGridTextColumn{Header=col.Item1,Binding=new Binding(col.Item2)});
            earthworkRows.SelectionChanged+=(_,__)=>{if(!refreshing&&earthworkRows.SelectedItem is DisplayRow row){selectedEarthworkRow=row.Record;vm.SelectEarthworkZone(row.Record,true);}};p.Children.Add(earthworkRows);Bound(p,nameof(vm.EarthworkProjectSummary));
            Action(p,"刪除所選分析紀錄…",()=>{if(selectedEarthworkRow==null)return;var row=selectedEarthworkRow;if(MessageBox.Show($"刪除區 {row.ZoneNumber}／{row.ZoneName} 的分析資料，以及本工具管理的對應 Schedule 紀錄（若存在）。\nTerrain 與 Cutter 不會刪除。","確認刪除分析紀錄",MessageBoxButton.OKCancel,MessageBoxImage.Warning)==MessageBoxResult.OK)vm.DeleteEarthworkZone(row.ZoneGuid,true,true);});
            Action(p,"標記已複核…",()=>{if(selectedEarthworkRow!=null&&MessageBox.Show("只標記 BIM 工具複核，不代表工程數量、合約或測量核准。","確認複核",MessageBoxButton.OKCancel)==MessageBoxResult.OK)vm.MarkEarthworkReviewed(selectedEarthworkRow.ZoneGuid,true);});
            Bound(p,nameof(vm.SummaryScheduleState));Action(p,"開啟摘要明細表",()=>vm.OpenEarthworkSchedule(EarthworkScheduleKind.Summary));Action(p,"建立／更新摘要明細表",()=>vm.PreviewEarthworkSchedule(EarthworkScheduleKind.Summary),nameof(vm.CanCreateSchedule));
            Bound(p,nameof(vm.DetailScheduleState));Action(p,"開啟完整明細表",()=>vm.OpenEarthworkSchedule(EarthworkScheduleKind.Detail));Action(p,"建立／更新完整明細表",vm.PreviewEarthworkSchedule,nameof(vm.CanCreateSchedule));
            Bound(p,nameof(vm.SchedulePreviewText));Details(p,"進階：完整欄位預覽",nameof(vm.ScheduleAdvancedText));
            Action(p,"確認建立／更新 Revit 土方明細表…",()=>{if(MessageBox.Show(vm.SchedulePreviewText,"確認寫入 Revit Schedule",MessageBoxButton.OKCancel,MessageBoxImage.Warning)==MessageBoxResult.OK)vm.ConfirmEarthworkSchedule(true);},nameof(vm.CanConfirmSchedule));
            Action(p,"匯出土方明細 CSV／JSON／Markdown…",ExportEarthwork);
        }
        private static void LiveText(StackPanel p,string label,string property){Label(p,label);var box=new TextBox{Margin=new Thickness(0,2,0,5)};box.SetBinding(TextBox.TextProperty,new Binding(property){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});p.Children.Add(box);}
        private void RefreshEarthwork()
        {
            earthBoundary.Visibility=vm.EarthworkMode=="BoundaryTin"?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;earthCutter.Visibility=vm.EarthworkMode=="RevitCutter"?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;
            earthDirectTarget.Visibility=vm.TargetMode=="Direct"?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;earthLevelTarget.Visibility=vm.TargetMode=="LevelOffset"?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;
            var selected=selectedEarthworkRow?.ZoneGuid;
            var rows=vm.EarthworkRecords.Select(r=>new DisplayRow(r,r.ZoneNumber,r.ZoneName,vm.FormatEarthworkArea(r.Quantity.Area),vm.FormatEarthworkVolume(r.Quantity.CutBankVolume),vm.FormatEarthworkVolume(r.Quantity.FillDesignVolume),vm.FormatEarthworkVolume(r.Logistics.ExportLooseVolume),vm.FormatEarthworkVolume(r.Logistics.ImportLooseVolume),r.Logistics.ExportTruckTrips,EarthworkPresentation.Money(r.Cost.TotalEstimatedCost,r.Cost.Currency),r.CalculationLabel,r.ReviewLabel,$"{r.ProfileSnapshot.ProfileName} V{r.ProfileSnapshot.ProfileVersion}")).ToArray();
            earthworkRows.ItemsSource=rows;earthworkRows.SelectedItem=rows.FirstOrDefault(r=>r.Record.ZoneGuid==selected);if(earthworkRows.SelectedItem==null)selectedEarthworkRow=null;
            DrawEarthworkBoundary();
        }
        private void DrawEarthworkBoundary()
        {
            earthworkBoundaryPreview.Children.Clear();var points=vm.BoundaryPoints;if(vm.EarthworkMode!="BoundaryTin"||points.Count<3||points.Count>2000)return;
            var b=TerrainBounds.Of(points);double width=Math.Max(100,earthworkBoundaryPreview.ActualWidth),scale=Math.Min((width-20)/Math.Max(1e-9,b.Max.X-b.Min.X),140/Math.Max(1e-9,b.Max.Y-b.Min.Y));
            var polygon=new System.Windows.Shapes.Polygon{Stroke=System.Windows.Media.Brushes.DarkOrange,StrokeThickness=2,Fill=System.Windows.Media.Brushes.Transparent};
            foreach(var point in points)polygon.Points.Add(new System.Windows.Point(10+(point.X-b.Min.X)*scale,150-(point.Y-b.Min.Y)*scale));earthworkBoundaryPreview.Children.Add(polygon);
        }
        private void ExportEarthwork()
        {
            if(vm.EarthworkRecords.Count==0)return;var dialog=new Microsoft.Win32.SaveFileDialog{Filter="Excel-friendly CSV|*.csv|JSON|*.json|Markdown|*.md",FileName="earthwork-records",AddExtension=true};if(dialog.ShowDialog()!=true)return;
            try{EarthworkExport.Write(dialog.FileName,vm.EarthworkRecords.ToArray(),dialog.FilterIndex);}catch(Exception e){MessageBox.Show(e.Message,"匯出失敗");}
        }
    }
}
#endif
