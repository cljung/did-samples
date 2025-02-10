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
using System.Net.Http;

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
        private Stopwatch _sw;

        public ProxyController(IConfiguration configuration, IMemoryCache memoryCache, ILogger<ProxyController> log, IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _cache = memoryCache;
            _log = log;
            _httpClientFactory = httpClientFactory;
            _cacheTtl = _configuration.GetValue<int>( "AppSettings:CacheExpiresInSeconds", 300 );
            _contentTypesToCache = _configuration.GetValue<string>( "AppSettings:cacheRule-Content-Type", "application/json;image/" ).Split(";");
            _acceptedRequestHeaders = _configuration.GetValue<string>( "AppSettings:acceptedRequestHeaders", "Accept-Encoding;Accept-Language" ).Split( ";" );
            _sw = new Stopwatch();
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
            string host = GetRequestHostName();
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
        private bool TryGetRequestHeaderValue( string key, out string value ) {
            value = null;
            if (this.Request.Headers.ContainsKey( key )) {
                value = this.Request.Headers[key].ToString();
                return true;
            }
            return false;
        }

        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // cache related methods

        private bool IsCacheControlNoCache() {
            if (TryGetRequestHeaderValue( "Cache-Control", out string buf )) {
                return buf.Contains( "no-cache" );
            }
            return false;
        }
        private bool IsCacheControlNoStore() {
            if (TryGetRequestHeaderValue( "Cache-Control", out string buf )) {
                return buf.Contains( "no-store" );
            }
            return false;
        }
        private bool IsCachable( string url, Dictionary<string,string> headers ) {
            if ( _cacheTtl <= 0 || IsCacheControlNoStore() ) {
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
        private void CacheRemoteContent( CacheMetadata metadata, string content ) {
            _log.LogTrace( $"Caching content for {metadata.url}\nExpiry: {metadata.expiry}" );
            _cache.Set( $"{this.Request.Method}@{metadata.url}", JsonConvert.SerializeObject( metadata ), metadata.expiry );
            if (!string.IsNullOrEmpty( content )) {
                _cache.Set( $"@{metadata.url}", content, metadata.expiry );
            }
        }
        private bool GetRemoteContentFromCache( string url, out CacheMetadata metadata, out string content ) {
            metadata = null;
            content = null;
            // HEAD only needs the metadata. GET needs metadata + content
            if (_cache.TryGetValue( $"{this.Request.Method}@{url}", out string md )) {
                if (!string.IsNullOrWhiteSpace( md ) ) {
                    metadata = JsonConvert.DeserializeObject<CacheMetadata>( md );
                }
                if ( this.Request.Method == "GET" ) {
                    if (_cache.TryGetValue( $"@{url}", out content ) && !string.IsNullOrWhiteSpace( content )) {
                        return true;
                    } else {
                        return false;
                    }
                }
                return true; // HEAD
            }
            return false;
        }
        private void RemoveCacheIfNewETag( string method, string url, Dictionary<string, string> headers ) {
            // we evaluate cached GET when method is HEAD and vice versa
            string cacheKey = method == "GET" ? $"HEAD@{url}" : $"GET@{url}";
            if (headers.ContainsKey( "ETag" ) && _cache.TryGetValue( cacheKey, out string md )) {
                CacheMetadata metadata = JsonConvert.DeserializeObject<CacheMetadata>( md );
                if (metadata.Headers.ContainsKey( "ETag" ) && metadata.Headers["ETag"] != headers["ETag"]) {
                    _log.LogTrace( $"ETag changed ({headers["ETag"]} != {metadata.Headers["ETag"]}), removing cache: {cacheKey}" );
                    _cache.Remove( cacheKey );
                    if (method == "HEAD") {
                        _cache.Remove( $"@{url}" ); // remove the GET response body also
                    }
                }
            }
        }
        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // return related methods
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
        private ActionResult FinalReturn( ActionResult result ) {
            _sw.Stop();
            _log.LogTrace( $"Response time: {_sw.ElapsedMilliseconds} ms" );
            return result;
        }

        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // Headers related methods

        private void SetRequestHeaders( HttpRequestMessage httpRequestMessage ) {
            foreach (var h in this.Request.Headers) {
                if (_acceptedRequestHeaders.Contains( h.Key )) {
                    httpRequestMessage.Headers.Add( h.Key, h.Value.First() );
                }
            }
            // set the X-Forwarded-* headers
            httpRequestMessage.Headers.Add( "X-Forwarded-Host", this.Request.Host.ToString() );
            httpRequestMessage.Headers.Add( "X-Forwarded-Proto", this.Request.Scheme );
            string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            httpRequestMessage.Headers.Add( "X-Forwarded-For", ipAddress );
        }

        private Dictionary<string,string> SetResponseHeaders( HttpResponseMessage httpResponseMessage ) {
            Dictionary<string, string> headers = new Dictionary<string, string>(); ;
            foreach (var hdr in httpResponseMessage.Content.Headers) {
                this.Response.Headers.Append( hdr.Key, hdr.Value.First() );
                headers.Add( hdr.Key, hdr.Value.First() );
            }
            foreach (var rh in httpResponseMessage.Headers) {
                this.Response.Headers.Append( rh.Key, rh.Value.First() );
                headers.Add( rh.Key, rh.Value.First() );
            }
            return headers;
        }

        /// <summary>
        /// Endpoint that processes all GET and HEAD requests via wildcard
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpGet( "/{*path}" )]
        [HttpHead( "/{*path}" )]
        public async Task<ActionResult> GetRequest( string path ) {
            _log.LogTrace( this.HttpContext.Request.GetDisplayUrl() );
            try {
                _sw.Start();

                // rewrite URL. If we find that we don't serve the remote host, bail bout
                string urlNew = RewriteUrl();
                if ( string.IsNullOrEmpty( urlNew ) ) { 
                    this.Response.HttpContext.Abort();
                    this.Response.HttpContext.Connection.RequestClose();
                    return new NotFoundResult(); // TODO: security best practice would be to not respond at all
                }
                
                // if caching is enabled
                if ( _cacheTtl > 0 ) {
                    // if caller is passing Cache-Control: no-cache in header or ?no-cache as query string parameter, we do not use cached content 
                    // Check if we have URL in cache. If so, return it
                    if (!IsCacheControlNoCache() && GetRemoteContentFromCache( urlNew, out CacheMetadata md, out string content )) {
                        return FinalReturn( ReturnContentFromCache( urlNew, md, content ) );
                    }
                }

                // Get content from remote server
                HttpRequestMessage httpRequestMessage = new HttpRequestMessage( new HttpMethod( this.Request.Method ), new Uri( urlNew ) );

                // relay accepted request headers (Some headers result in SSL/cert validation errors as we are a man-in-the-middle)
                SetRequestHeaders( httpRequestMessage );
                StringBuilder sb = new StringBuilder();
                foreach( var h in httpRequestMessage.Headers ) {
                    sb.Append( $"{h.Key}: {h.Value.First()}\n" );
                }
                _log.LogTrace( $"{this.Request.Method} {urlNew}\n{sb.ToString()}" );

                HttpResponseMessage httpResponseMessage = null;
                string response = null;
                using (_httpClient = _httpClientFactory.CreateClient()) {
                    httpResponseMessage = _httpClient.SendAsync( httpRequestMessage ).Result;
                    response = httpResponseMessage.Content.ReadAsStringAsync().Result;
                }

                // relay response headers we got from the remote server to the caller
                Dictionary<string, string> headers = SetResponseHeaders( httpResponseMessage );

                // Check to see if we should cache it (we only cache when request was a success)
                if (httpResponseMessage.IsSuccessStatusCode && _cacheTtl > 0 && IsCachable( urlNew, headers )) {
                    CacheMetadata metadata = new CacheMetadata() {
                        method = this.Request.Method,
                        url = urlNew,
                        StatusCode = (int)httpResponseMessage.StatusCode,
                        Headers = headers,
                        cachedDate = DateTime.UtcNow.ToString(),
                        cacheHits = 0,
                        expiry = DateTimeOffset.Now.AddSeconds( _cacheTtl )
                    };
                    CacheRemoteContent( metadata, response );
                }
                // if HEAD ETag != GET ETag (and vice versa), then invalidate GET cache
                RemoveCacheIfNewETag( this.Request.Method, urlNew, headers );

                return FinalReturn( new ContentResult { StatusCode = (int?)httpResponseMessage.StatusCode, Content = response } );
            } catch (Exception ex) {
                string errorMessage = ex.Message;
                if ( ex.InnerException != null && ex.InnerException.InnerException != null ) {
                    errorMessage += $" {ex.InnerException.InnerException.Message}";
                }
                _log.LogError( errorMessage );
                return FinalReturn( BadRequest( new { error = "400", error_description = "Technical error: " + errorMessage } ) );
            }
        }
    } // cls
} // ns
