using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows.Input;
using RevitMCP.Models;

namespace RevitMCP.UI
{
    public interface ICoordinationContext
    {
        string DocumentIdentity { get; }
        IReadOnlyList<CoordinationSource> GetSources();
        IReadOnlyList<CoordinationLevel> GetLevels(long linkId);
        IReadOnlyList<string> GetMepCategories(long linkId);
        double ParseClearance(string text);
        CoordinationResult Scan(CoordinationRequest request);
        void Highlight(CoordinationRow row, bool mep, bool host);
    }
    public interface ICoordinationHost
    {
        bool IsBusy { get; }
        event EventHandler BusyChanged;
        bool Submit(Action<ICoordinationContext> action, Action<string> failure);
    }
    public sealed class WorkflowCommand : ICommand
    {
        private readonly Action action;
        private readonly Func<bool> canExecute;
        public WorkflowCommand(Action action, Func<bool> canExecute) { this.action = action; this.canExecute = canExecute; }
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => canExecute();
        public void Execute(object? parameter) { if (CanExecute(parameter)) action(); }
        public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>Production Native workflow. No Revit or WPF dependencies; all API work is queued through the host.</summary>
    public sealed class CoordinationViewModel : INotifyPropertyChanged
    {
        private readonly ICoordinationHost host;
        private readonly List<WorkflowCommand> commands = new List<WorkflowCommand>();
        private readonly Dictionary<string, CoordinationProjectSettings> settings = new Dictionary<string, CoordinationProjectSettings>();
        private bool refreshPending, initialized, sourcesReady, publishing, documentSwitched;
        private int generation;
        private CoordinationSource? mepSource, hostSource;
        private CoordinationLevel? level;
        private string mepCategory = "", hostCategory = "", system = "", search = "", filter = "全部", clearanceText = "";
        private bool openings;
        private int maxResults = 200;
        private CoordinationRow? selectedRow;
        public CoordinationViewModel(ICoordinationHost host)
        {
            this.host = host;
            RefreshSourcesCommand = Command(RefreshSources, () => !IsBusy);
            ScanCommand = Command(ExecuteScan, () => CanScan);
            SaveSettingsCommand = Command(SaveSettings, () => !IsBusy && sourcesReady && !string.IsNullOrWhiteSpace(ClearanceText));
            HighlightMepCommand = Command(() => Navigate(true, false), () => CanNavigate);
            HighlightHostCommand = Command(() => Navigate(false, true), () => CanNavigate);
            HighlightBothCommand = Command(() => Navigate(true, true), () => CanNavigate);
            PreviousCommand = Command(() => Move(-1), () => CanNavigate && RowsView.IndexOf(SelectedRow!) > 0);
            NextCommand = Command(() => Move(1), () => CanNavigate && RowsView.IndexOf(SelectedRow!) < RowsView.Count - 1);
            ExportCommand = Command(() => ExportRequested?.Invoke(this, EventArgs.Empty), () => !IsBusy && Result != null && Result.DocumentIdentity == DocumentIdentity);
            host.BusyChanged += (_, __) => { Notify(); if (!IsBusy && refreshPending) RefreshSources(); };
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? ExportRequested;
        private WorkflowCommand Command(Action action, Func<bool> can) { var command = new WorkflowCommand(action, can); commands.Add(command); return command; }
        public ICommand RefreshSourcesCommand { get; }
        public ICommand ScanCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand HighlightMepCommand { get; }
        public ICommand HighlightHostCommand { get; }
        public ICommand HighlightBothCommand { get; }
        public ICommand PreviousCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand ExportCommand { get; }
        public bool IsBusy => host.IsBusy;
        public string DocumentIdentity { get; private set; } = "";
        public string StatusMessage { get; private set; } = "正在準備模型來源。";
        public IReadOnlyList<CoordinationSource> Sources { get; private set; } = Array.Empty<CoordinationSource>();
        public IReadOnlyList<CoordinationLevel> Levels { get; private set; } = Array.Empty<CoordinationLevel>();
        public IReadOnlyList<string> MepCategories { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<string> HostCategories { get; } = new[] { "Walls", "Floors", "StructuralFraming", "StructuralColumns" };
        public IReadOnlyList<string> Filters { get; } = new[] { "全部", "開孔候選", "穿梁候選", "一般碰撞", "需人工複核" };
        public CoordinationSource? MepSource
        {
            get => mepSource;
            set { if (publishing || mepSource?.LinkInstanceId == value?.LinkInstanceId) return; mepSource = value; system = ""; level = null; Levels = Array.Empty<CoordinationLevel>(); sourcesReady = false; Invalidate(); ScheduleRefresh(); }
        }
        public CoordinationSource? HostSource { get => hostSource; set { if (publishing || hostSource?.LinkInstanceId == value?.LinkInstanceId) return; hostSource = value; Invalidate(); } }
        public CoordinationLevel? SelectedLevel { get => level; set { if (publishing || level == value) return; level = value; Invalidate(); } }
        public string MepCategory { get => mepCategory; set { if (publishing) return; mepCategory = value ?? ""; system = ""; Invalidate(); } }
        public string HostCategory { get => hostCategory; set { if (publishing) return; hostCategory = value ?? ""; Invalidate(); } }
        public string SystemContains { get => system; set { if (publishing) return; system = value ?? ""; Invalidate(); } }
        public bool HasSystemFilter => MepCategory == "Pipes" || MepCategory == "Ducts";
        public bool OpeningCandidates { get => openings; set { if (publishing) return; openings = value; Invalidate(); } }
        public bool NeedsClearance => OpeningCandidates && HostCategories.Contains(HostCategory);
        public string ClearanceText { get => clearanceText; set { if (publishing) return; clearanceText = value ?? ""; Notify(); } }
        public int MaxResults { get => maxResults; set { if (publishing) return; maxResults = value; Invalidate(); } }
        public CoordinationResult? Result { get; private set; }
        public CoordinationRequest? LastRequest { get; private set; }
        public List<CoordinationRow> RowsView { get; private set; } = new List<CoordinationRow>();
        public CoordinationRow? SelectedRow { get => selectedRow; set { if (publishing) return; selectedRow = value != null && RowsView.Contains(value) ? value : null; Notify(); } }
        public string Search { get => search; set { if (publishing) return; search = value ?? ""; FilterRows(); } }
        public string SelectedFilter { get => filter; set { if (publishing) return; filter = value ?? "全部"; FilterRows(); } }
        public double? ClearanceMm => settings.TryGetValue(DocumentIdentity, out var value) ? value.OpeningClearanceMm : null;
        public string SavedSetting => ClearanceMm.HasValue ? $"目前設定：每側 {ClearanceMm.Value:0.###} mm（本模型 session）" : "本模型尚未設定預留量。";
        public string CostWarning => SelectedLevel?.Id == null ? "全部樓層：大型來源可能超過計算上限；可選樓層縮小範圍。" : "";
        public string ScanDisabledReason => IsBusy ? "正在處理上一個要求。" : !sourcesReady ? "正在讀取模型來源；若失敗請按重新整理。"
            : MepSource == null ? "請選擇 MEP 來源。" : HostSource == null ? "請選擇主體來源。"
            : Levels.Count <= 1 ? "目前來源沒有可用樓層。" : !MepCategories.Contains(MepCategory) ? "請選擇可用 MEP 分類。"
            : !HostCategories.Contains(HostCategory) ? "請選擇主體分類。" : NeedsClearance && !ClearanceMm.HasValue ? "開孔候選需要先設定預留量。"
            : MaxResults < 1 || MaxResults > 1000 ? "顯示上限須為 1 至 1000。" : "";
        public bool CanScan => ScanDisabledReason.Length == 0;
        public bool CanNavigate => !IsBusy && Result != null && Result.DocumentIdentity == DocumentIdentity && SelectedRow != null && RowsView.Contains(SelectedRow);
        public string Summary => Result == null ? "尚未掃描。" : $"掃描 {Result.TotalScanned} 個 MEP；總問題 {Result.TotalIssues}；開孔 {Count(CoordinationKind.OpeningCandidate)}；穿梁 {Count(CoordinationKind.BeamPenetration)}；碰撞 {Count(CoordinationKind.Clash)}；需複核 {(Result.CountsByStatus.TryGetValue("需人工複核", out int reviews) ? reviews : 0)}；顯示 {Result.ReturnedCount}，篩選後 {RowsView.Count}" + (Result.IsTruncated ? "（已達顯示上限，總數仍完整）" : "");
        private int Count(CoordinationKind kind) => Result != null && Result.CountsByKind.TryGetValue(kind, out int count) ? count : 0;
        public string Warnings => Result == null ? "採中心線穿越法；不含管件、保溫、實體擦碰。結果不代表結構核准。" : string.Join("；", Result.Warnings);
        public void Initialize() { if (initialized) return; initialized = true; ScheduleRefresh(); }
        public void DocumentChanged(string identity, bool contentsChanged = false)
        {
            if (!initialized) return;
            if (identity == DocumentIdentity && !contentsChanged) return;
            bool switched = identity != DocumentIdentity;
            documentSwitched |= switched;
            if (switched) { mepSource = null; hostSource = null; hostCategory = ""; system = ""; clearanceText = ""; }
            sourcesReady = false; DocumentIdentity = identity; Invalidate(); ScheduleRefresh();
        }
        private void ScheduleRefresh() { refreshPending = true; if (!IsBusy) RefreshSources(); else Notify(); }
        public void RefreshSources()
        {
            if (IsBusy) { refreshPending = true; return; }
            refreshPending = false; sourcesReady = false;
            int revision = ++generation;
            bool accepted = host.Submit(context =>
            {
                if (revision != generation) { refreshPending = true; return; }
                bool same = DocumentIdentity == context.DocumentIdentity;
                long mepId = same ? mepSource?.LinkInstanceId ?? 0 : 0;
                long hostId = same ? hostSource?.LinkInstanceId ?? 0 : 0;
                DocumentIdentity = context.DocumentIdentity;
                Sources = context.GetSources();
                mepSource = Sources.FirstOrDefault(s => s.LinkInstanceId == mepId) ?? Sources.FirstOrDefault(s => s.LinkInstanceId == 0);
                hostSource = Sources.FirstOrDefault(s => s.LinkInstanceId == hostId) ?? Sources.FirstOrDefault(s => s.LinkInstanceId == 0);
                if (mepSource == null) throw new InvalidOperationException("沒有可用 MEP 來源。");
                Levels = new[] { new CoordinationLevel() }.Concat(context.GetLevels(mepSource.LinkInstanceId)).ToArray();
                level = Levels[0];
                MepCategories = context.GetMepCategories(mepSource.LinkInstanceId);
                if (!MepCategories.Contains(mepCategory)) mepCategory = MepCategories.FirstOrDefault() ?? "";
                if (!same) { hostCategory = ""; system = ""; clearanceText = ""; }
                Result = null; selectedRow = null; LastRequest = null; RowsView.Clear();
                sourcesReady = true; StatusMessage = documentSwitched ? "模型已切換，來源已重新整理。" : "模型來源已重新整理。"; documentSwitched = false; Notify();
            }, error => { if (revision == generation) { sourcesReady = false; StatusMessage = "無法重新讀取模型來源，請按重新整理。 " + error; Notify(); } });
            if (!accepted) { sourcesReady = false; StatusMessage = "無法排程來源更新，請按重新整理。"; Notify(); }
        }
        private void Invalidate() { generation++; Result = null; LastRequest = null; selectedRow = null; RowsView = new List<CoordinationRow>(); Notify(); }
        private void Anchor(ICoordinationContext context)
        {
            if (context.DocumentIdentity == DocumentIdentity) return;
            DocumentChanged(context.DocumentIdentity);
            throw new InvalidOperationException("模型已切換，正在重新整理來源。");
        }
        public void SaveSettings()
        {
            if (IsBusy || !sourcesReady) return;
            string input = ClearanceText;
            Submit(context => { Anchor(context); double mm = context.ParseClearance(input); CoordinationRules.OpeningSize(1, mm);
                settings[DocumentIdentity] = new CoordinationProjectSettings { OpeningClearanceMm = mm }; Invalidate(); StatusMessage = "預留量已保存。"; Notify(); });
        }
        public CoordinationRequest BuildRequest()
        {
            if (!CanScan) throw new InvalidOperationException(ScanDisabledReason);
            var request = new CoordinationRequest { MepLinkId = MepSource!.LinkInstanceId, HostLinkId = HostSource!.LinkInstanceId,
                MepCategory = MepCategory, HostCategory = HostCategory, LevelId = SelectedLevel?.Id, SystemContains = HasSystemFilter ? SystemContains : "",
                MaxResults = MaxResults, OpeningCandidates = OpeningCandidates, ClearanceMm = NeedsClearance ? ClearanceMm : null };
            CoordinationRules.Validate(request); return request;
        }
        public void ExecuteScan()
        {
            if (!CanScan) return;
            var request = BuildRequest(); int revision = generation;
            Result = null; RowsView.Clear(); selectedRow = null; LastRequest = request;
            Submit(context => { Anchor(context); if (revision != generation) return;
                var result = context.Scan(request); if (revision != generation || result.DocumentIdentity != DocumentIdentity) return;
                Result = result; StatusMessage = "協調掃描完成。"; FilterRows(); });
        }
        private void Submit(Action<ICoordinationContext> action)
        {
            if (!host.Submit(action, error => { StatusMessage = error; Notify(); })) { StatusMessage = "Revit 未接受要求，請稍後再試。"; Notify(); }
        }
        private void FilterRows()
        {
            RowsView = (Result?.Rows ?? new List<CoordinationRow>()).Where(row =>
                (filter == "全部" || filter == "需人工複核" && row.ReviewRequired || row.KindDisplay == filter) &&
                (search.Length == 0 || (row.Detail + row.MepLabel + row.HostLabel + row.System + row.Level + row.Explanation).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            if (selectedRow == null || !RowsView.Contains(selectedRow)) selectedRow = RowsView.FirstOrDefault();
            Notify();
        }
        private void Move(int direction) { int next = RowsView.IndexOf(selectedRow!) + direction; if (next < 0 || next >= RowsView.Count) return; SelectedRow = RowsView[next]; Navigate(true, true); }
        private void Navigate(bool mep, bool structure)
        {
            if (!CanNavigate) return; var row = SelectedRow!; int revision = generation;
            Submit(context => { Anchor(context); if (revision != generation) return; context.Highlight(row, mep, structure);
                StatusMessage = "已亮顯。" + ((mep && row.Mep.LinkInstanceId != 0) || (structure && row.Host.LinkInstanceId != 0) ? "連結元素以 Link instance 定位；明細保留原元素 ID。" : ""); Notify(); });
        }
        public string ExportCsv()
        {
            if (Result == null || Result.DocumentIdentity != DocumentIdentity) throw new InvalidOperationException("請先重新掃描。");
            var text = new StringBuilder("ResultKind,Status,MEP Source,MEP Element,MEP Category,System,MEP Size,Host Source,Host Element,Host Category,Level,Suggested Opening Size,Penetration,Warnings,Explanation\r\n");
            foreach (var row in RowsView) text.AppendLine(string.Join(",", new[] { row.ResultKind.ToString(), row.Status, row.MepSource, row.Mep.ToString(), row.MepCategory, row.System, row.NominalSizeDisplay, row.HostSource, row.Host.ToString(), row.HostCategory, row.Level, row.SizeDisplay, row.IntersectionLengthMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), string.Join(";", row.WarningCodes), row.Explanation }.Select(CsvCell)));
            return text.ToString();
        }
        private static string CsvCell(string value) { if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value; return "\"" + value.Replace("\"", "\"\"") + "\""; }
        public void ReportExport(string message) { StatusMessage = message; Notify(); }
        private void Notify() { publishing = true; try { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); } finally { publishing = false; } foreach (var command in commands) command.Refresh(); }
    }
}
