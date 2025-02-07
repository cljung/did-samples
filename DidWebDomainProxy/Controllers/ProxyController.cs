using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Authorization;
using System.Security.Cryptography;
using System.Net;
using System;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Reflection.PortableExecutable;

namespace DidWebDomainProxy.Controllers
{
    //[Route("api/[controller]/[action]")]
    public class ProxyController : Controller
    {
        protected IMemoryCache _cache;
        protected readonly ILogger<ProxyController> _log;
        private IConfiguration _configuration;
        private IHttpClientFactory _httpClientFactory;
        private HttpClient _httpClient;
        private int _cacheTtl;
        private string[] _contentTypesToCache = null;
        private string[] _acceptedRequestHeaders = null;

        public ProxyController(IConfiguration configuration, IMemoryCache memoryCache, ILogger<ProxyController> log, IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _cache = memoryCache;
            _log = log;
            _httpClientFactory = httpClientFactory;
            _cacheTtl = _configuration.GetValue<int>( "AppSettings:CacheExpiresInSeconds", 300 );
            _contentTypesToCache = _configuration.GetValue<string>( "AppSettings:cacheRule-Content-Type", "application/json;image/" ).Split(";");
            _acceptedRequestHeaders = _configuration.GetValue<string>( "AppSettings:acceptedRequestHeaders", "Accept-Encoding;Accept-Language" ).Split( ";" );
            
        }
        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // some helper functions
        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private string GetRequestHostName() {
            string scheme = "https";// : this.Request.Scheme;
            string originalHost = this.Request.Headers["x-original-host"];
            string hostname = "";
            if (!string.IsNullOrEmpty( originalHost ))
                hostname = string.Format( "{0}://{1}", scheme, originalHost );
            else hostname = string.Format( "{0}://{1}", scheme, this.Request.Host );
            return hostname;
        }
        private string RewriteUrl() {
            string host = GetRequestHostName().Replace("https://", "");
            string mappedHost = _configuration[$"AppSettings:domainMap-{host}"];  
            if ( mappedHost == null ) {
                mappedHost = _configuration[$"AppSettings:domainMap-{host}/"];
            }
            // we are not configured to handle this remote domain/host
            if ( mappedHost == null ) {
                _log.LogTrace( $"Hostname not configured to be supported: {host}");
                return null;
            }
            if ( mappedHost.EndsWith("/")) {
                mappedHost = mappedHost.TrimEnd('/');
            }
            return $"{mappedHost}{this.Request.GetEncodedPathAndQuery()}";
        }
        private bool IsCachable( string url, Dictionary<string,string> headers ) {
            if ( _cacheTtl <= 0 ) {
                return false;
            }
            if ( headers.ContainsKey("Content-Type")  ) {
                string contentType = headers["Content-Type"];
                foreach( var ct in _contentTypesToCache ) {
                    if ( contentType.StartsWith(ct) ) {
                        return true;    
                    }
                }
            }
            return false;
        }
        private bool TryGetRequestHeaderValue( string key, out string value ) {
            value = null;
            if (this.Request.Headers.ContainsKey( key )) {
                value = this.Request.Headers[key].ToString();
                return true;
            }
            return false;
        }
        private bool CheckRequestkHeaderNoCache() {
            if ( TryGetRequestHeaderValue( "Cache-Control", out string buf ) ) {
                return buf.Contains("no-cache");
            }
            return false;
        }
        private void CacheRemoteContent( CacheMetadata metadata, string content ) {
            _cache.Set( metadata.url, JsonConvert.SerializeObject( metadata ), metadata.expiry );
            if (!string.IsNullOrEmpty( content )) {
                _cache.Set( $"{metadata.url}@", content, metadata.expiry );
            }
        }
        private bool GetRemoteContentFromCache( string url, out CacheMetadata metadata, out string content ) {
            metadata = null;
            content = null;
            if (_cache.TryGetValue( url, out string md )) {
                _cache.TryGetValue( $"{url}@", out content );
                if (!string.IsNullOrWhiteSpace( md ) && !string.IsNullOrWhiteSpace( content )) {
                    metadata = JsonConvert.DeserializeObject<CacheMetadata>( md );
                    return true;
                }
            }
            return false;
        }
        private ActionResult ReturnContentFromCache( string url, CacheMetadata metadata, string content ) {
            foreach (var hdr in metadata.Headers) {
                this.Response.Headers.Append( hdr.Key, hdr.Value );
            }
            metadata.cacheHits++;
            CacheRemoteContent( metadata, null );
            this.Response.Headers.Append( "x-dwdp-cached", metadata.cachedDate );
            _log.LogTrace( $"Using cached content for {url}\nCached at: {metadata.cachedDate}\nExpiry: {metadata.expiry}\nCache hits: {metadata.cacheHits}" );
            return new ContentResult { StatusCode = (int?)(HttpStatusCode)metadata.StatusCode, Content = content };

        }
        /// <summary>
        /// Endpoint that processes all requests via wildcard
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpGet( "/{*path}" )]
        public async Task<ActionResult> GetRequest( string path ) {
            _log.LogTrace( this.HttpContext.Request.GetDisplayUrl() );
            try {

                // rewrite URL. If we find that we don't serve the remote host, bail bout
                string urlNew = RewriteUrl();
                if ( string.IsNullOrEmpty( urlNew ) ) { 
                    return new NotFoundResult(); // TODO: security best practice would be to not respond at all
                }
                
                // if caching is enabled
                if ( _cacheTtl > 0 ) {
                    // if caller is passing Cache-Control: no-cahse in header or ?no-cache as query string parameter, we do not use cached content 
                    bool headerNoCache = CheckRequestkHeaderNoCache();
                    bool queryNoCache = this.Request.Query.ContainsKey( "no-cache" );
                    // Check if we have URL in cache. If so, return it
                    if (!headerNoCache && !queryNoCache && GetRemoteContentFromCache( urlNew, out CacheMetadata md, out string content )) {
                        return ReturnContentFromCache( urlNew, md, content );
                    }
                }

                // Get content from remote server

                _httpClient = _httpClientFactory.CreateClient();
                // relay accepted request headers (Some headers result in SSL/cert validation errors as we are a man-in-the-middle)
                StringBuilder sb = new StringBuilder();
                foreach (var h in this.Request.Headers) {
                    if ( _acceptedRequestHeaders.Contains(h.Key) ) {
                        _httpClient.DefaultRequestHeaders.Add( h.Key, h.Value.First() );
                        sb.Append( $"{h.Key}: {h.Value.First()}\n" );
                    }
                }
                _log.LogTrace( $"Fetching {urlNew}\n{sb.ToString()}" );

                HttpResponseMessage res = _httpClient.GetAsync( urlNew ).Result;
                string response = res.Content.ReadAsStringAsync().Result;

                // relay response headers we got from the remote server to the caller
                Dictionary<string, string> headers = new Dictionary<string, string>(); ;
                foreach ( var hdr in res.Content.Headers ) { 
                    this.Response.Headers.Append( hdr.Key, hdr.Value.First() );
                    headers.Add( hdr.Key, hdr.Value.First() );
                }                
                foreach( var rh in res.Headers ) {
                    this.Response.Headers.Append( rh.Key, rh.Value.First() );
                    headers.Add( rh.Key, rh.Value.First() );
                }

                // Check to see if we should cache it (we only cache when GET was a success)
                if ( res.IsSuccessStatusCode && _cacheTtl > 0 && IsCachable( urlNew, headers )) {
                    CacheMetadata metadata = new CacheMetadata() {
                        url = urlNew,
                        StatusCode = (int)res.StatusCode,
                        Headers = headers,
                        cachedDate = DateTime.UtcNow.ToString(),
                        cacheHits = 0,
                        expiry = DateTimeOffset.Now.AddSeconds( _cacheTtl )
                    };
                    _log.LogTrace( $"Caching content for {urlNew}\nExpiry: {metadata.expiry}" );
                    CacheRemoteContent( metadata, response );
                }
                _httpClient.Dispose();

                return new ContentResult { StatusCode = (int?)res.StatusCode, Content = response };
            } catch (Exception ex) {
                string errorMessage = ex.Message;
                if ( ex.InnerException != null && ex.InnerException.InnerException != null ) {
                    errorMessage += $" {ex.InnerException.InnerException.Message}";
                }
                _log.LogError( errorMessage );
                return BadRequest( new { error = "400", error_description = "Technical error: " + errorMessage } );
            }
        }
    } // cls
} // ns
