using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCP.Core;

namespace RevitMCP.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class RunCoordinationSelfTestCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                string? directory = Environment.GetEnvironmentVariable("REVIT_MCP_SELFTEST_DIR");
                if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("請從 Self-Test launcher 啟動專用測試工作階段。");
                CoordinationSelfTest.Run(data.Application.Application, directory);
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }
}
