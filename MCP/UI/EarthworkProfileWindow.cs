#if REVIT2026
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using RevitMCP.Core.Site;
using TextBox=System.Windows.Controls.TextBox;
using ComboBox=System.Windows.Controls.ComboBox;

namespace RevitMCP.UI
{
    internal sealed class EarthworkProfileWindow : Window
    {
        public EarthworkProjectSettings? Profile {get;private set;}
        public EarthworkProfileWindow(EarthworkProjectSettings? source)
        {
            Title="土方 Project Profile（所有係數／單價自行設定）";Width=570;Height=750;WindowStartupLocation=WindowStartupLocation.CenterScreen;
            var root=new DockPanel{Margin=new Thickness(16)};Content=root;var save=new Button{Content="檢查並預覽儲存",Padding=new Thickness(8)};DockPanel.SetDock(save,Dock.Bottom);root.Children.Add(save);
            var error=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=System.Windows.Media.Brushes.Firebrick};DockPanel.SetDock(error,Dock.Bottom);root.Children.Add(error);
            var panel=new StackPanel();root.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});var boxes=new Dictionary<string,TextBox>();
            void Input(string key,string label,object? value){panel.Children.Add(new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,2)});var box=new TextBox{Text=Convert.ToString(value,CultureInfo.CurrentCulture)??""};boxes[key]=box;panel.Children.Add(box);}
            Input("Name","Profile 名稱",source?.ProfileName);Input("Currency","幣別（不自動換匯）",source?.Currency);
            panel.Children.Add(new TextBlock{Text="車斗容量與所有體積單價的單位基準",Margin=new Thickness(0,8,0,2)});
            var units=new ComboBox{ItemsSource=new[]{"m³","ft³"},SelectedIndex=source?.VolumeUnit==EarthworkVolumeUnit.CubicFeet?1:0};panel.Children.Add(units);
            Input("Swell","鬆方係數 = 挖方鬆方量 ÷ 挖方原地量",source?.SwellFactor);Input("Fill","回填需求係數 = 需要的鬆方量 ÷ 設計填方量",source?.FillLooseFactor);Input("Reuse","再利用率（0～1；例如 0.5 = 50%，不是預設值）",source?.ReusableRate);
            Input("Truck","車型",source?.TruckName);Input("Capacity","車斗容量（依上方 m³ / ft³）",source?.TruckCapacity);Input("Util","裝載率（大於 0 且 ≤1）",source?.TruckLoadUtilization);
            Input("Excavation","挖土單價／原地量",source?.ExcavationUnitCost);Input("Loading","裝載單價／外運鬆方",source?.LoadingUnitCost);Input("Haul","運輸單價／外運車次",source?.HaulCostPerTrip);Input("Disposal","棄土單價／外運鬆方",source?.DisposalCostPerVolume);Input("Imported","外購土單價／外購鬆方",source?.ImportedFillCostPerVolume);Input("Backfill","回填施工單價／設計填方",source?.BackfillPlacementCostPerVolume);Input("Compaction","夯實單價／設計填方",source?.CompactionCostPerVolume);Input("Mobilization","動員費（選填；每區計入一次，空白不計）",source?.MobilizationCost);
            panel.Children.Add(new TextBlock{Text=EarthworkEstimator.Disclaimer,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)});
            double? Number(string key)=>string.IsNullOrWhiteSpace(boxes[key].Text)?null:double.Parse(boxes[key].Text,NumberStyles.Float,CultureInfo.CurrentCulture);
            decimal? Price(string key)=>string.IsNullOrWhiteSpace(boxes[key].Text)?null:decimal.Parse(boxes[key].Text,NumberStyles.Number,CultureInfo.CurrentCulture);
            save.Click+=(_,__)=>{try{var p=new EarthworkProjectSettings{ProfileGuid=source?.ProfileGuid??Guid.NewGuid(),ProfileName=boxes["Name"].Text.Trim(),Currency=boxes["Currency"].Text.Trim(),VolumeUnit=units.SelectedIndex==0?EarthworkVolumeUnit.CubicMetres:EarthworkVolumeUnit.CubicFeet,SwellFactor=Number("Swell"),FillLooseFactor=Number("Fill"),ReusableRate=Number("Reuse"),TruckName=boxes["Truck"].Text.Trim(),TruckCapacity=Number("Capacity"),TruckLoadUtilization=Number("Util"),ExcavationUnitCost=Price("Excavation"),LoadingUnitCost=Price("Loading"),HaulCostPerTrip=Price("Haul"),DisposalCostPerVolume=Price("Disposal"),ImportedFillCostPerVolume=Price("Imported"),BackfillPlacementCostPerVolume=Price("Backfill"),CompactionCostPerVolume=Price("Compaction"),MobilizationCost=Price("Mobilization")};p.Validate();Profile=p;DialogResult=true;}catch(Exception ex){error.Text=ex.Message;}};
        }
    }
}
#endif
