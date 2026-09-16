using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCP.Models;

namespace RevitMCP.Core
{
    /// <summary>UI-only navigation in an ExternalEvent. Never mutates document or view settings.</summary>
    internal sealed class CoordinationNavigationService
    {
        internal const string NoThreeD = "目前模型沒有可用的 3D 視圖，無法執行 3D 定位。";
        private static bool Usable(View3D? view) => view != null && view.IsValidObject && !view.IsTemplate && view.CanBePrinted;
        private static CoordinationViewOption Option(View3D view) => new CoordinationViewOption
        { Id = view.Id.GetIdValue(), Usable = Usable(view), Perspective = view.IsPerspective };
        private static long? Resolve(UIDocument ui, CoordinationNavigationSession session, bool keepSession) =>
            CoordinationViewPolicy.Resolve(ui.ActiveView?.Id.GetIdValue() ?? -1, session.CoordinationViewId, keepSession,
                id => ui.Document.GetElement(CoordinationService.Id(id)) is View3D view ? Option(view) : null,
                () => new FilteredElementCollector(ui.Document).OfClass(typeof(View3D)).Cast<View3D>().Select(Option));
        public bool IsAvailable(UIDocument ui, CoordinationNavigationSession session) =>
            new FilteredElementCollector(ui.Document).OfClass(typeof(View3D)).Cast<View3D>().Any(Usable);
        public CoordinationNavigationResult Locate(UIDocument ui, CoordinationRow row, bool mep, bool host,
            CoordinationNavigationSession session, bool keepSession)
        {
            var document = ui.Document;
            // Both references must still refer to the scanned elements, including linked document identities.
            var mepElement = CoordinationService.ResolveNavigation(document, row.Mep);
            var hostElement = CoordinationService.ResolveNavigation(document, row.Host);
            var ids = new List<ElementId>();
            if (mep) ids.Add(mepElement.Id);
            if (host) ids.Add(hostElement.Id);
            return LocateResolved(ui,row,ids,session,keepSession);
        }
        public CoordinationNavigationResult LocateElement(UIDocument ui,ElementId id,CoordinationNavigationSession session)
        {
            var element=ui.Document.GetElement(id)??throw new InvalidOperationException("地形已不存在，請重新選取。");
            var bounds=element.get_BoundingBox(null)??throw new InvalidOperationException("元素沒有可定位範圍。");
            var center=bounds.Transform.OfPoint((bounds.Min+bounds.Max)*.5);
            var row=new CoordinationRow{Xmm=center.X*304.8,Ymm=center.Y*304.8,Zmm=center.Z*304.8,IntersectionLengthMm=bounds.Min.DistanceTo(bounds.Max)*304.8};
            var result=LocateResolved(ui,row,new List<ElementId>{id},session,true);
            result.Message=result.Message.Replace("交點","地形中心");return result;
        }
        private CoordinationNavigationResult LocateResolved(UIDocument ui,CoordinationRow row,List<ElementId> ids,CoordinationNavigationSession session,bool keepSession)
        {
            var document=ui.Document;
            ids = ids.Distinct().ToList();
            long? target = Resolve(ui, session, keepSession);
            if (!target.HasValue)
            {
                session.CoordinationViewId = null;
                Select(ui, ids);
                return new CoordinationNavigationResult { Message = NoThreeD + "已僅選取可安全選取的構件。" + LinkNote(row) };
            }
            var view = (View3D)document.GetElement(CoordinationService.Id(target.Value));
            var previous = ui.ActiveView;
            if (previous == null || previous.Id != view.Id) ui.ActiveView = view;
            if (ui.ActiveView.Id != view.Id) throw new InvalidOperationException("無法切換至協調 3D 視圖；請重新定位。");
            if (previous != null && !(previous is View3D) && !session.PreviousViewId.HasValue) session.PreviousViewId = previous.Id.GetIdValue();
            session.CoordinationViewId = target;
            Select(ui, ids);
            var result = new CoordinationNavigationResult { ThreeDAvailable = true };
            try
            {
                var center = new XYZ(row.Xmm / 304.8, row.Ymm / 304.8, row.Zmm / 304.8);
                if (!Finite(center.X) || !Finite(center.Y) || !Finite(center.Z)) throw new InvalidOperationException("交點座標無效。");
                var uiView = ui.GetOpenUIViews().Single(v => v.ViewId == view.Id);
                double size = new[] { row.IntersectionLengthMm, row.NominalDiameterMm ?? 0, row.NominalWidthMm ?? 0, row.NominalHeightMm ?? 0 }.Max();
                if (!Finite(size)) throw new InvalidOperationException("交點尺寸無效。");
                double radius = Math.Max(3, size / 304.8 * 1.5);
                var offset = (view.RightDirection + view.UpDirection) * radius;
                uiView.ZoomAndCenterRectangle(center - offset, center + offset);
                bool framed = FocusContains(ui, row);
                bool clipped = false;
                if (view.IsSectionBoxActive)
                {
                    var box = view.GetSectionBox(); var local = box.Transform.Inverse.OfPoint(center);
                    clipped = local.X < box.Min.X || local.X > box.Max.X || local.Y < box.Min.Y || local.Y > box.Max.Y || local.Z < box.Min.Z || local.Z > box.Max.Z;
                }
                result.FocusVerified = framed && !clipped && ui.ActiveView.Id == view.Id;
                result.Message = result.FocusVerified
                    ? "已在 3D 視圖定位交點；構件可見性仍受既有視圖設定影響。"
                    : "已選取構件，但交點焦點未通過驗證；請檢查既有視圖的剖面框或顯示設定。";
            }
            catch (Exception)
            {
                result.Message = "已切換 3D 並選取構件，但此視圖無法完成交點縮放；請手動調整視圖或選另一個既有 3D 視圖。";
            }
            result.Message += LinkNote(row);
            return result;
        }
        private static bool Finite(double number) => !double.IsNaN(number) && !double.IsInfinity(number);
        private static string LinkNote(CoordinationRow row) => row.Mep.LinkInstanceId != 0 || row.Host.LinkInstanceId != 0
            ? "連結構件以 Link instance 選取；明細保留原 linked Element ID。" : "";
        private static void Select(UIDocument ui, List<ElementId> ids)
        {
            ui.Selection.SetElementIds(ids);
            if (!ui.Selection.GetElementIds().OrderBy(i => i.GetIdValue()).SequenceEqual(ids.OrderBy(i => i.GetIdValue())))
                throw new InvalidOperationException("選取 read-back 不一致，請重新整理後再試。");
        }
        internal static bool FocusContains(UIDocument ui, CoordinationRow row)
        {
            if (!(ui.ActiveView is View3D view)) return false;
            var viewport = ui.GetOpenUIViews().Single(v => v.ViewId == view.Id);
            var corners = viewport.GetZoomCorners();
            var point = new XYZ(row.Xmm / 304.8, row.Ymm / 304.8, row.Zmm / 304.8);
            return new[] { view.RightDirection, view.UpDirection }.All(axis =>
            {
                double a = corners[0].DotProduct(axis), b = corners[1].DotProduct(axis), p = point.DotProduct(axis);
                return p >= Math.Min(a, b) - 1e-6 && p <= Math.Max(a, b) + 1e-6;
            });
        }
        public string Return(UIDocument ui, CoordinationNavigationSession session)
        {
            var view = session.PreviousViewId.HasValue ? ui.Document.GetElement(CoordinationService.Id(session.PreviousViewId.Value)) as View : null;
            if (view == null || !view.IsValidObject || view.IsTemplate)
            { session.Clear(); return "原視圖已失效，已清除導航記錄。"; }
            ui.ActiveView = view;
            if (ui.ActiveView.Id != view.Id) throw new InvalidOperationException("返回原視圖未通過驗證。");
            session.PreviousViewId = null;
            return "已返回原視圖。";
        }
    }
}
