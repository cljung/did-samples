namespace DidWebDomainProxy; 
public class CacheMetadata {
    public string method { get; set; }
    public string url { get; set; }
    public int StatusCode {get; set; }
    public Dictionary<string,string> Headers { get; set; }
    public string cachedDate { get; set; }
    public long cacheHits { get; set; }
    public DateTimeOffset expiry { get; set; }
}
