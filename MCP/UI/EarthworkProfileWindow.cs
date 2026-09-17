#if REVIT2026
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RevitMCP.Core.Site;
using TextBox=System.Windows.Controls.TextBox;
using ComboBox=System.Windows.Controls.ComboBox;
using Button=System.Windows.Controls.Button;
using Grid=System.Windows.Controls.Grid;
namespace RevitMCP.UI
{
    internal sealed class EarthworkProfileWindow : Window
    {
        public EarthworkProjectProfile? Profile {get;private set;}
        internal EarthworkProfileEditor Editor {get;}
        public EarthworkProfileWindow(EarthworkProjectProfile? source)
        {
            Editor=new(source);Title="土方成本設定檔";Width=650;Height=820;MinWidth=540;MinHeight=600;WindowStartupLocation=WindowStartupLocation.CenterScreen;
            var root=new DockPanel{Margin=new Thickness(18)};Content=root;
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
            buttons.Children.Add(new Button{Content="取消",IsCancel=true,Padding=new Thickness(18,8,18,8),Margin=new Thickness(6)});
            var save=new Button{Content="檢查並預覽儲存",Padding=new Thickness(18,8,18,8),Margin=new Thickness(6)};buttons.Children.Add(save);
            var error=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Firebrick,Margin=new Thickness(0,8,0,8)};DockPanel.SetDock(error,Dock.Bottom);root.Children.Add(error);
            var body=new StackPanel();root.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
            StackPanel Group(string title){var p=new StackPanel{Margin=new Thickness(12)};body.Children.Add(new GroupBox{Header=title,Content=p,Margin=new Thickness(0,0,0,12)});return p;}
            var units=new ComboBox{ItemsSource=new[]{"m³","ft³"},SelectedIndex=Editor.Unit==EarthworkVolumeUnit.CubicFeet?1:0,Margin=new Thickness(0,4,0,4)};
            var unitLabels=new List<(TextBlock Label,string Basis)>();var soil=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray};var truck=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DarkSlateBlue};bool ready=false;
            void Refresh()
            {
                if(!ready)return;Editor.Unit=units.SelectedIndex==0?EarthworkVolumeUnit.CubicMetres:EarthworkVolumeUnit.CubicFeet;
                foreach(var item in unitLabels)item.Label.Text=item.Basis switch{"volume"=>Editor.UnitLabel,"price"=>Editor.Values["Currency"]+"／"+Editor.UnitLabel,"trip"=>Editor.Values["Currency"]+"／車次",_=>Editor.Values["Currency"]};
                soil.Text=Editor.SoilPreview;truck.Text=Editor.TruckPreview;
                try{Editor.Build();error.Text="輸入有效；儲存前仍需確認。";save.IsEnabled=true;}catch(ArgumentException ex){error.Text=ex.Message;save.IsEnabled=false;}
            }
            void Input(StackPanel p,string key,string label,string basis="")
            {
                var grid=new Grid{Margin=new Thickness(0,4,0,4)};grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(150)});grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(120)});p.Children.Add(grid);
                grid.Children.Add(new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap});var box=new TextBox{Text=Editor.Values[key],Padding=new Thickness(5)};Grid.SetColumn(box,1);grid.Children.Add(box);
                var suffix=new TextBlock{Text=basis=="percent"?"%":"",Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};Grid.SetColumn(suffix,2);grid.Children.Add(suffix);
                if(basis!=""&&basis!="percent")unitLabels.Add((suffix,basis));box.TextChanged+=(_,__)=>{Editor.Values[key]=box.Text;Refresh();};
            }
            var basic=Group("基本設定");Input(basic,"Name","設定檔名稱");Input(basic,"Currency","幣別");basic.Children.Add(new TextBlock{Text="車斗容量及體積單價基準"});basic.Children.Add(units);units.SelectionChanged+=(_,__)=>Refresh();
            var material=Group("土方性質");Input(material,"Swell","開挖鬆方係數");Input(material,"Fill","回填需求係數");Input(material,"Reuse","可再利用率","percent");material.Children.Add(soil);
            var transport=Group("運輸設定");Input(transport,"Truck","車型");Input(transport,"Capacity","車斗容量","volume");Input(transport,"Util","平均裝載率","percent");transport.Children.Add(truck);
            var prices=Group("單價設定");Input(prices,"Excavation","挖土／原地量","price");Input(prices,"Loading","裝車／外運鬆方","price");Input(prices,"Haul","外運車資","trip");Input(prices,"Disposal","棄土／外運鬆方","price");Input(prices,"Imported","外購填料／鬆方","price");Input(prices,"Backfill","回填施工／設計量","price");Input(prices,"Compaction","夯實／設計量","price");
            var other=Group("其他成本");Input(other,"Mobilization","動員費（選填）","money");other.Children.Add(new TextBlock{Text="每區計入一次；留白不計。",Foreground=Brushes.DimGray});
            body.Children.Add(new Expander{Header="設定檔詳細資料",Content=new TextBlock{Text=source==null?"儲存時建立新身分與 V1。":$"版本 V{source.ProfileVersion}\nGUID {source.ProfileGuid}\n最後修改 {source.UpdatedAt:yyyy-MM-dd HH:mm:ss}",Margin=new Thickness(8)}});
            body.Children.Add(new TextBlock{Text=EarthworkEstimator.Disclaimer,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)});
            save.Click+=(_,__)=>{try{Profile=Editor.Build();DialogResult=true;}catch(ArgumentException ex){error.Text=ex.Message;}};ready=true;Refresh();
        }
    }
}
#endif
