#if REVIT2026
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace RevitMCP.Core.Drawing
{
    internal static class DrawingFixtureIsolation
    {
        private static readonly Guid SchemaId=new("60349486-5280-4C38-9209-E70C9D167367");
        public static bool DeveloperMode=>string.Equals(Environment.GetEnvironmentVariable("REVIT_MCP_SELFTEST_DRAWING_WORKFLOW"),"True",StringComparison.OrdinalIgnoreCase);
        public static void Mark(Element element)
        {
            var schema=Schema.Lookup(SchemaId);
            if(schema==null){var builder=new SchemaBuilder(SchemaId);builder.SetSchemaName("DrawingFixtureMarker");builder.AddSimpleField("Kind",typeof(string));schema=builder.Finish();}
            var entity=new Entity(schema);entity.Set("Kind","Fixture");element.SetEntity(entity);
        }
        public static bool Visible(Element element)
        {
            if(DeveloperMode)return true;
            var schema=Schema.Lookup(SchemaId);return schema==null||!element.GetEntity(schema).IsValid();
        }
        public static bool Visible(DrawingTemplateProfile profile)=>DeveloperMode||profile.ProfileKind!=DrawingProfileKind.Fixture&&profile.Blueprint.TitleBlockFamilyName!="DrawingFixtureTitleBlock";
    }
}
#endif
