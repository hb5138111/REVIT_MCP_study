#if REVIT2026
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
using ComboBox=System.Windows.Controls.ComboBox;
using Binding=System.Windows.Data.Binding;
using Ellipse=System.Windows.Shapes.Ellipse;

namespace RevitMCP.UI
{
    internal sealed class SiteTerrainControl : UserControl
    {
        public SiteTerrainControl(SiteTerrainViewModel vm)
        {
            DataContext=vm;
            var root=new StackPanel{Margin=new Thickness(10)};Content=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            root.Children.Add(new TextBlock{Text="智慧基地地形／土方中心",FontSize=18,FontWeight=FontWeights.SemiBold});
            void Heading(string text)=>root.Children.Add(new TextBlock{Text=text,FontWeight=FontWeights.Bold,Margin=new Thickness(0,12,0,4)});
            void Text(string label,string property){root.Children.Add(new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap});var box=new TextBox{Margin=new Thickness(0,2,0,4)};box.SetBinding(TextBox.TextProperty,new Binding(property){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged,ValidatesOnExceptions=true});root.Children.Add(box);}
            void Choice(string label,string property,string[] values){root.Children.Add(new TextBlock{Text=label});var c=new ComboBox{ItemsSource=values};c.SetBinding(ComboBox.SelectedItemProperty,new Binding(property){Mode=BindingMode.TwoWay});root.Children.Add(c);}
            void Tick(string text,string property){var c=new CheckBox{Content=text,Margin=new Thickness(0,4,0,4)};c.SetBinding(CheckBox.IsCheckedProperty,new Binding(property){Mode=BindingMode.TwoWay});root.Children.Add(c);}
            void Action(string text,Action action,string? enabled=null){var b=new Button{Content=text,Margin=new Thickness(0,4,0,4),Padding=new Thickness(6)};b.Click+=(_,__)=>action();if(enabled!=null)b.SetBinding(Button.IsEnabledProperty,new Binding(enabled));root.Children.Add(b);}
            void Option(string label,string source,string property){root.Children.Add(new TextBlock{Text=label});var c=new ComboBox{DisplayMemberPath="Name",SelectedValuePath="Id"};c.SetBinding(ComboBox.ItemsSourceProperty,new Binding(source));c.SetBinding(ComboBox.SelectedValueProperty,new Binding(property){Mode=BindingMode.TwoWay});root.Children.Add(c);}
            Heading("1. 地形資料（CSV / TXT）");
            Action("選擇測量點檔",()=>{var dialog=new OpenFileDialog{Filter="Survey points|*.csv;*.txt",CheckFileExists=true};if(dialog.ShowDialog()==true)vm.FilePath=dialog.FileName;});
            Text("來源檔案",nameof(vm.FilePath));Choice("分隔符",nameof(vm.Delimiter),new[]{"comma","semicolon","tab","space"});Tick("第一列是 header",nameof(vm.HasHeader));
            Text("Column mapping：X,Y,Z,ID,Code（0 起算；選填 -1）",nameof(vm.Mapping));Choice("原始檔案單位（不自動猜測）",nameof(vm.Units),new[]{"m","mm","ft"});
            Action("分析匯入 / Point QA",async()=>await vm.ImportAsync());
            Heading("2. 座標定位（不移動建築）");
            Action("檢查建築／基地座標",vm.RefreshContext);
            Choice("Coordinate basis",nameof(vm.CoordinateMode),new[]{"SharedCoordinates","ControlPointAlignment","LocalCoordinates"});
            Text("控制點：surveyX,Y,Z,internalX,Y,Z；全部 m；多點以 ; 分隔",nameof(vm.ControlPoints));
            Text("Tool tolerance（m；非公司測量規範）",nameof(vm.ToleranceMetres));
            Heading("3. 地形建立");
            Option("Toposolid Type","Context.Types",nameof(vm.TypeId));Option("Level","Context.Levels",nameof(vm.LevelId));
            Choice("減點模式（保留邊界、極值與 Code 點）",nameof(vm.ReductionMode),new[]{"原始","平衡","高效能","自訂"});Text("自訂 grid（m）",nameof(vm.GridMetres));
            Tick("我已複核拒絕列、重複與 QA 提醒（XY 衝突仍阻擋）",nameof(vm.AcknowledgeDiagnostics));Tick("明確覆核：允許超過 20k 點（tool performance risk）",nameof(vm.LargePointOverride));
            Action("預覽地形（不建立模型元素）",async()=>await vm.PreviewAsync());
            var preview=new Canvas{Height=200,Background=Brushes.AliceBlue,ClipToBounds=true};root.Children.Add(preview);
            void Draw(){preview.Children.Clear();var points=vm.PreviewPoints;if(points.Count==0)return;double minx=points.Min(p=>p.X),miny=points.Min(p=>p.Y),dx=Math.Max(1e-9,points.Max(p=>p.X)-minx),dy=Math.Max(1e-9,points.Max(p=>p.Y)-miny);double w=Math.Max(100,preview.ActualWidth);int step=Math.Max(1,(int)Math.Ceiling(points.Count/2000.0));for(int i=0;i<points.Count;i+=step){var dot=new Ellipse{Width=3,Height=3,Fill=Brushes.SteelBlue};Canvas.SetLeft(dot,5+(w-10)*(points[i].X-minx)/dx);Canvas.SetTop(dot,195-190*(points[i].Y-miny)/dy);preview.Children.Add(dot);}}
            preview.SizeChanged+=(_,__)=>Draw();vm.PropertyChanged+=(_,__)=>Draw();
            root.Children.Add(new TextBlock{Text="2D 顯示最多 2000 個等間距樣本；實際點數與 QA 見詳細資訊。",TextWrapping=TextWrapping.Wrap});
            Tick("我已檢視本次 Preview，確認執行模型寫入",nameof(vm.Confirmed));
            Action("建立地形（WRITE）",vm.Create,nameof(vm.CanExecuteCreate));
            Heading("4. 土方計算");Action("由目前選取取得 Terrain",()=>vm.UseSelection(true));Text("Existing host Toposolid ElementId",nameof(vm.TerrainId));Action("由目前選取取得 Cutter",()=>vm.UseSelection(false));Text("Cutter ElementId（Floor / Roof / Toposolid）",nameof(vm.CutterId));
            Action("預覽 Cutter 開挖（Revit 試算後 rollback）",vm.PreviewExcavation);Action("執行開挖（WRITE；需重新勾選確認）",vm.ExecuteExcavation,nameof(vm.CanExcavate));
            Text("凸 boundary：internal m，x,y;x,y;…",nameof(vm.Boundary));Text("Target elevation（internal m）",nameof(vm.TargetElevation));
            Action("分析 Boundary Cut / Fill（唯讀 TIN）",vm.CalculateBoundary);
            root.Children.Add(new TextBlock{Text="Existing / Proposed surface：experimental，尚未開放正式數量。",TextWrapping=TextWrapping.Wrap});
            foreach(string property in new[]{nameof(vm.Status),nameof(vm.Detail),nameof(vm.Result)}){var text=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};text.SetBinding(TextBlock.TextProperty,new Binding(property));root.Children.Add(text);}
        }
    }
}
#endif
