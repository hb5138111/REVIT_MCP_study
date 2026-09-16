using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCP.Core;
using RevitMCP.Models;

namespace RevitMCP.UI
{
    public sealed class CoordinationViewModel : INotifyPropertyChanged
    {
        private readonly PanelReadOnlyDispatcher dispatcher;
        private readonly CoordinationService service = new CoordinationService();
        private readonly Dictionary<string, CoordinationProjectSettings> settings = new Dictionary<string, CoordinationProjectSettings>();
        private Document? sourceDocument;
        private string documentIdentity = string.Empty;
        private string search = "";
        private bool warningsOnly;
        private CoordinationSource? mepSource;
        private CoordinationRow? selectedRow;
        internal CoordinationViewModel(PanelReadOnlyDispatcher dispatcher, bool opening)
        {
            this.dispatcher = dispatcher; OpeningCandidates = opening;
            RefreshSourcesCommand = Command(RefreshSources);
            ScanCommand = Command(Scan);
            SaveSettingsCommand = Command(SaveSettings);
            ResetSettingsCommand = Command(() => Submit(app => { Anchor(app); settings[documentIdentity] = new CoordinationProjectSettings(); ClearanceText = ""; Changed(nameof(ClearanceText)); StatusMessage = "已重設本專案預留量。"; }));
            HighlightMepCommand = Command(() => Navigate(0));
            HighlightHostCommand = Command(() => Navigate(1));
            HighlightBothCommand = Command(() => Navigate(2));
            PreviousCommand = Command(() => Move(-1)); NextCommand = Command(() => Move(1));
            ExportCommand = Command(Export);
            dispatcher.BusyChanged += (_, __) => CommandManager.InvalidateRequerySuggested();
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool OpeningCandidates { get; }
        public IReadOnlyList<CoordinationSource> Sources { get; private set; } = Array.Empty<CoordinationSource>();
        public IReadOnlyList<string> Levels { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<CategoryOption> MepCategories { get; } = new[] { Option("Pipes", "管"), Option("Ducts", "風管"), Option("CableTrays", "電纜架"), Option("Conduits", "電管") };
        public IReadOnlyList<CategoryOption> HostCategories { get; } = new[] { Option("Walls", "牆"), Option("Floors", "樓板"), Option("StructuralFraming", "梁"), Option("StructuralColumns", "柱") };
        public CoordinationSource? MepSource { get => mepSource; set { mepSource = value; LevelName = string.Empty; Levels = Array.Empty<string>(); Changed(); Changed(nameof(LevelName)); Changed(nameof(Levels)); } }
        public CoordinationSource? HostSource { get; set; }
        public CategoryOption? MepCategory { get; set; }
        public CategoryOption? HostCategory { get; set; }
        public string LevelName { get; set; } = string.Empty;
        public string SystemContains { get; set; } = string.Empty;
        public string ClearanceText { get; set; } = string.Empty;
        public int MaxResults { get; set; } = 200;
        public string UnitsDescription { get; private set; } = "依專案長度單位輸入（可包含單位）；設定僅保存於本次 Revit 工作階段。";
        private string status = "請先讀取來源，再選來源、分類與 MEP 樓層。";
        public string StatusMessage { get => status; private set { status = value; Changed(); } }
        public string Summary => Result == null ? "尚未掃描" : $"符合 {Result.TotalMatchedCount}；顯示 {Result.ReturnedCount}；需人工複核 {Result.Rows.Count(r => r.ReviewRequired)}；警告 {Result.Warnings.Count}；截斷：{(Result.IsTruncated ? "是（巡覽僅含顯示結果）" : "否")}。";
        public string Warnings => Result == null ? "" : string.Join("\n", Result.Warnings);
        public CoordinationResult? Result { get; private set; }
        public ICollectionView? RowsView { get; private set; }
        public CoordinationRow? SelectedRow { get => selectedRow; set { selectedRow = value; Changed(); } }
        public string Search { get => search; set { search = value ?? ""; RowsView?.Refresh(); Changed(); } }
        public bool WarningsOnly { get => warningsOnly; set { warningsOnly = value; RowsView?.Refresh(); Changed(); } }
        public ICommand RefreshSourcesCommand { get; }
        public ICommand ScanCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand ResetSettingsCommand { get; }
        public ICommand HighlightMepCommand { get; }
        public ICommand HighlightHostCommand { get; }
        public ICommand HighlightBothCommand { get; }
        public ICommand PreviousCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand ExportCommand { get; }
        private void RefreshSources() => Submit(app =>
        {
            var doc = app.ActiveUIDocument?.Document ?? throw new InvalidOperationException("請先開啟專案。");
            bool same = ReferenceEquals(sourceDocument, doc);
            var oldMep = same ? MepSource?.LinkInstanceId ?? 0 : 0;
            var oldHost = same ? HostSource?.LinkInstanceId ?? 0 : 0;
            sourceDocument = doc; documentIdentity = TypeInstanceLocatorService.GetDocumentIdentity(doc);
            Sources = service.GetSources(doc);
            MepSource = Sources.FirstOrDefault(s => s.LinkInstanceId == oldMep) ?? Sources[0];
            HostSource = Sources.FirstOrDefault(s => s.LinkInstanceId == oldHost) ?? Sources[0];
            Levels = service.GetLevels(doc, MepSource.LinkInstanceId);
            Result = null; RowsView = null; SelectedRow = null;
            ClearanceText = settings.TryGetValue(documentIdentity, out var configured) && configured.OpeningClearanceMm.HasValue
                ? UnitFormatUtils.Format(doc.GetUnits(), SpecTypeId.Length, configured.OpeningClearanceMm.Value / 304.8, false) : "";
            Changed(nameof(Sources)); Changed(nameof(HostSource)); Changed(nameof(Levels)); Changed(nameof(ClearanceText)); Publish();
            StatusMessage = "來源已更新。切換 MEP 來源後請再按讀取來源，以取得該來源樓層。";
        });
        private Document Anchor(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null || !ReferenceEquals(sourceDocument, doc) || TypeInstanceLocatorService.GetDocumentIdentity(doc) != documentIdentity)
                throw new InvalidOperationException("目前模型已變更，請重新讀取來源。");
            return doc;
        }
        private void SaveSettings() => Submit(app =>
        {
            var doc = Anchor(app);
            if (!UnitFormatUtils.TryParse(doc.GetUnits(), SpecTypeId.Length, ClearanceText ?? "", out double feet))
                throw new ArgumentException("請依專案長度單位輸入有效預留量。");
            CoordinationRules.OpeningSize(1, feet * 304.8);
            settings[documentIdentity] = new CoordinationProjectSettings { OpeningClearanceMm = feet * 304.8 };
            StatusMessage = "已保存本專案每側預留量：" + UnitFormatUtils.Format(doc.GetUnits(), SpecTypeId.Length, feet, false);
        });
        private void Scan()
        {
            if (MepSource == null || HostSource == null) { StatusMessage = "請先讀取來源。"; return; }
            var request = new CoordinationRequest { MepLinkId = MepSource.LinkInstanceId, HostLinkId = HostSource.LinkInstanceId,
                MepCategory = MepCategory?.Value ?? string.Empty, HostCategory = HostCategory?.Value ?? string.Empty, LevelName = LevelName,
                SystemContains = SystemContains, MaxResults = MaxResults, OpeningCandidates = OpeningCandidates };
            Submit(app =>
            {
                var doc = Anchor(app);
                request.ClearanceMm = settings.TryGetValue(documentIdentity, out var configured) ? configured.OpeningClearanceMm : null;
                Result = null; RowsView = null; SelectedRow = null; Publish();
                Result = service.Scan(doc, request);
                RowsView = CollectionViewSource.GetDefaultView(Result.Rows);
                RowsView.Filter = item => item is CoordinationRow row && (!WarningsOnly || row.ReviewRequired) &&
                    (row.Mep + " " + row.Host + " " + row.System + " " + row.Status + " " + row.Warnings).IndexOf(Search, StringComparison.OrdinalIgnoreCase) >= 0;
                Publish(); StatusMessage = "掃描完成；結果僅供協調與人工複核。";
            });
        }
        private void Move(int delta)
        {
            var visible = RowsView?.Cast<CoordinationRow>().ToList();
            if (visible == null || visible.Count == 0) return;
            int index = SelectedRow == null ? (delta > 0 ? 0 : visible.Count - 1) : visible.IndexOf(SelectedRow) + delta;
            if (index < 0 || index >= visible.Count) return;
            SelectedRow = visible[index]; Navigate(2);
        }
        private void Navigate(int which)
        {
            var row = SelectedRow; var result = Result;
            if (row == null || result == null) return;
            Submit(app =>
            {
                var doc = Anchor(app);
                if (result.DocumentIdentity != documentIdentity) throw new InvalidOperationException("結果已失效，請重新掃描。");
                var lookups = which == 0 ? new[] { row.Mep } : which == 1 ? new[] { row.Host } : new[] { row.Mep, row.Host };
                var ids = lookups.Select(l => CoordinationService.ResolveNavigation(doc, l).Id).Distinct().ToList();
                app.ActiveUIDocument.Selection.SetElementIds(ids); app.ActiveUIDocument.ShowElements(ids);
                StatusMessage = lookups.Any(l => l.LinkInstanceId != 0) ? "已亮顯連結實例；連結內元素 ID 請見結果明細。" : "已定位所選構件。";
            });
        }
        private void Export()
        {
            if (Result == null) return;
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "施工協調.csv" };
            if (dialog.ShowDialog() != true) return;
            string Cell(object value)
            {
                string s = Convert.ToString(value) ?? "";
                if (s.Length > 0 && "=+-@".Contains(s[0])) s = "'" + s;
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            var lines = new List<string> { "MEP,主體,系統,樓層,MEP尺寸,建議尺寸,交集mm,狀態,警告" };
            lines.AddRange(Result.Rows.Select(r => string.Join(",", new object[] { r.Mep, r.Host, r.System, r.Level, r.NominalSizeDisplay, r.SizeDisplay, r.IntersectionLengthMm, r.Status, r.Warnings }.Select(Cell))));
            System.IO.File.WriteAllLines(dialog.FileName, lines, new System.Text.UTF8Encoding(true));
            StatusMessage = "已匯出目前取得的結果；" + Summary;
        }
        private void Publish() { Changed(nameof(Result)); Changed(nameof(RowsView)); Changed(nameof(Summary)); Changed(nameof(Warnings)); }
        private void Submit(Action<UIApplication> action) => dispatcher.TrySubmit(PanelReadOnlyRequestKind.CoordinationScan, action, error => StatusMessage = error);
        private ICommand Command(Action action) => new ActionCommand(action, () => !dispatcher.IsBusy);
        private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public sealed class CategoryOption { public string Value { get; set; } = string.Empty; public string Name { get; set; } = string.Empty; public override string ToString() => Name; }
        private static CategoryOption Option(string value, string name) => new CategoryOption { Value = value, Name = name };
        private sealed class ActionCommand : ICommand
        {
            private readonly Action action; private readonly Func<bool> enabled;
            public ActionCommand(Action action, Func<bool> enabled) { this.action = action; this.enabled = enabled; }
            public bool CanExecute(object? parameter) => enabled(); public void Execute(object? parameter) => action();
            public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
        }
    }
}
