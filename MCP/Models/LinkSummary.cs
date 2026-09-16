namespace RevitMCP.Models
{
    public sealed class LinkSummary
    {
        public long LinkInstanceId { get; set; }
        public string LinkTypeName { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public bool IsLoaded { get; set; }
        public LinkTransformSummary Transform { get; set; }
    }

    public sealed class LinkTransformSummary
    {
        public double OriginX { get; set; }
        public double OriginY { get; set; }
        public double OriginZ { get; set; }
        public bool IsIdentity { get; set; }
    }

}
