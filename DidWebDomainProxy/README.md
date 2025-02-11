# DID Domain Proxy for Microsoft Entra Verified ID

If you are hosting your `did.json` DID Document file on Azure Storage, as described in the public docs [here](https://learn.microsoft.com/en-us/entra/verified-id/how-to-dnsbind#linked-domain-made-easy-for-developers), you can use this sample as a proxy to get a real domain infront of it.

## Why should I use this app?

If you have control over your DNS (or have a friendly admin who has and can help you), but you can't or won't deploy the did.json and did-configuration.json files to a webserver 
running on that hostname, you can use the Azure Storage to host these files and use this app as a proxy.

[Azure Front Door](https://learn.microsoft.com/en-us/azure/storage/blobs/storage-custom-domain-name?tabs=azure-portal#map-a-custom-domain-with-https-enabled) is a more robust, powerful and professional solution of achieving the same thing, but perhaps you find it too much overhead to deploy it for your POC/demo/etc environment.

## How it works?

You configure your hostname mapping to [map a domain name](https://learn.microsoft.com/en-us/azure/app-service/app-service-web-tutorial-custom-domain) to your [Azure Storage static website](https://learn.microsoft.com/en-us/azure/storage/blobs/storage-blob-static-website) hostname. Then you deploy the app to Azure App Services and add your custom domain to it (same domain you have in your mapping).
The app will then act as a proxy and get the did.json file from Azure Storage and serve the contents to the caller.

```JSON
  "AppSettings": {
    ...
    "domainMap-https://did.woodgrovedemo.com": "https://didcustomerplayground.z13.web.core.windows.net",
    ...
  }

``` 

When a caller makes a HTTP GET request to `https://did.woodgrovedemo.com/.well-known/did.json`, the app will forward the request to `https://didcustomerplayground.z13.web.core.windows.net/.well-known/did.json`, 
cache the response and return the contents to the caller.

### HTTP Methods

Only HTTP GET and HEAD are supported. All paths are forwarded as is and there is no URL rewrite (other than changing the hostname).

HTTP POST, PUT, PATCH and DELETE are not supported as they modify content on the remote webserver 
and that is beyond the scope of this simple app.

### Headers

HTTP Headers received from the caller are passed on to the forwarded request ***if*** they are configured to do so. 
Please note that if you forward all headers to the remote server, you may get into SSL/certificate validation errors.

```JSON
  "AppSettings": {
    ...
    "acceptedRequestHeaders": "User-Agent;Accept-Encoding;Accept-Language"
    ...
  }

```

Headers received from remote webserver are all returned to the caller.

### Cookies

This app does not support proxying cookies in either direction.

### Caching

The app will cache content successfully retrieved from the remote webserver for the amount of time configured in appsettings.json. The default is one hour.
If you set this value to 0 (or negative value), no caching is done at all.
Caching is only done for configured Content-Types. So the below configuration will not cache `text/plain`, `application\xml`, etc.

```JSON
  "AppSettings": {
    ...
    "CacheExpiresInSeconds": 3600,
    "cacheRule-Content-Type": "application/json;image/",
    ...
  }

```

The cache will store content plus headers received from the remote webserver and will return them from cache exactly as they were received.

The app will add extra header `x-dwdp-cached` if the content is served from cache.

Requests with header Cache-Control and value `no-cache` will not use any cached response and instead retrieve content from remote webserver. 
Requests with header Cache-Control and value `no-store` will not cache response from remote webserver but may use content from cache. 
Using both `no-cache` and `no-store` means don't use cache at all.

If you need retrieve the content from the remote webserver and update the cache, run the following powershell command

```Powershell
Invoke-RestMethod -Method "GET" -Uri $url -headers @{'Cache-Control'='no-cache'}
```

The app will return a 304 Not Modified if:

- The remote webserver returned headers `ETag` and `Last-Modified` in the previous cached response
- The caller sends request headers `If-Modified-Since` and `If-None-Match` headers
- The `If-None-Match` in the request header matches the cached ETag value and the `Last-Modified` value is less than or equal to `If-Modified-Since`

You need to restart the Azure App Service if you want to flush the entire cache and not invalidate URLs one-by-one. 

If a HEAD request returns an ETag header value that is different than the cached ETag value for a GET request with the same URL, 
then the cached content for the GET request is removed - and vice versa.

## Deploy to Azure App Services

Either clone the repo and do it yourself, or click on the below button to deploy it using the `ARM template` in this repo.

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fcljung%2Fdid-samples%2Fmain%2FDidWebDomainProxy%2FARMTemplate%2Ftemplate.json)

There is no need to do an app registration in Entra ID, etc, as this app runs without any ENtra permissions. 
All you need to do is to make sure your config is correct.

When updating the configuration in Azure App Services, remember that the environment variable name must be `AppSettings__domainMap-https://did.woodgrovedemo.com`, etc, with two underscores.

## Setting up a domain in Azure App Services

Use the `Custom Domain` configuration section and add your custom domain as documented [here](https://learn.microsoft.com/en-us/azure/app-service/app-service-web-tutorial-custom-domain).

For the DID Document to work in Microsoft Entra Verified ID, the domain name must match what you specified as `linked domain`. 
Ie, if your Verified ID domain is `did.woodgrovedemo.com`, that is the custom domain you must add, and that is the mapping you must have in your app's configuration.

The app can handle multiple domains. You just add another domain in the Azure Portal and update your apps's configuration.
