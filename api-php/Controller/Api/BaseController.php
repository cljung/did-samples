<?php
class BaseController
{
    /**
     * Create a guid
     */
    function create_guid() { // Create GUID (Globally Unique Identifier)
        $guid = '';
        $namespace = rand(11111, 99999);
        $uid = uniqid('', true);
        $data = $namespace;
        $data .= $_SERVER['REQUEST_TIME'];
        $data .= $_SERVER['HTTP_USER_AGENT'];
        $data .= $_SERVER['REMOTE_ADDR'];
        $data .= $_SERVER['REMOTE_PORT'];
        $hash = strtoupper(hash('ripemd128', $uid . $guid . md5($data)));
        $guid = substr($hash, 0, 8) . '-' . substr($hash, 8, 4) . '-' . substr($hash, 12, 4) . '-' . substr($hash, 16, 4) . '-' . substr($hash, 20, 12);
        return $guid;
    }

    /**
     * Log message to console (terminal, not browser console)
     */
    function console_log($message) {
        if ( is_array($message)) {
            $message = implode( ", ", $message ); 
        }
        $message = date("H:i:s") . " - $message - ".PHP_EOL;
        $STDERR = fopen("php://stderr", "w");
                  fwrite($STDERR, $message);
                  fclose($STDERR);
    }

    /**
     * Make an HTTP POST call
     */
    function httpPost( $url, $headers, $data ) {
        // use key 'http' even if you send the request to https://...
        $options = [
            'http' => [
                'header'  => $headers,
                'method'  => 'POST',
                'content' => $data
            ]
        ];
        $context  = stream_context_create($options);
        $response = file_get_contents($url, false, $context);
        if ($response === false) {
            $response = $http_response_header[0];
        }
        return $response;
    }

    /**
     * Put data into cache (cache == file)
     */
    function putCache( $key, $value ) {        
        file_put_contents( $key . '.json', json_encode($value, JSON_UNESCAPED_SLASHES) );
    }
    /**
     * Get data from cache (cache == file)
     */
    function getCache( $key ) {     
        $filename = $key . '.json';
        if ( file_exists( $filename ) ) {
            return json_decode( file_get_contents( $filename ), true );
        } else {
            return "";
        }
    }
    /**
     * Delete data from cache (cache == file)
     */
    function deleteCache( $key ) {
        unlink( $key . '.json');
    }
    /**
     * Make a POST to token endpoint with client credentials to get an access_token
     */
    function acquireAccessToken() {
        $options = [
            'http' => [
                'header'  => "Content-type: application/x-www-form-urlencoded\r\n",
                'method'  => 'POST',
                'content' => http_build_query( [
                    'grant_type' => 'client_credentials',
                    'client_id' => CLIENT_ID,
                    'client_secret' => CLIENT_SECRET,
                    'scope' => SCOPES
                ] )
            ]
        ];
        $context  = stream_context_create($options);
        $response = file_get_contents( TOKEN_ENDPOINT, false, $context);        
        $resp = json_decode($response);        
        if(isset($resp->access_token)) {
            return $resp->access_token;
        } else {
            return "";
        } 
    }
    /**
     * __call magic method.
     */
    public function __call($name, $arguments)
    {
        $this->sendOutput('', array('HTTP/1.1 404 Not Found'));
    }
 
    /**
     * Send API output.
     *
     * @param mixed  $data
     * @param string $httpHeader
     */
    protected function sendOutput($data, $httpHeaders=array())
    {
        header_remove('Set-Cookie');
 
        if (is_array($httpHeaders) && count($httpHeaders)) {
            foreach ($httpHeaders as $httpHeader) {
                header($httpHeader);
            }
        }
 
        echo $data;
        exit;
    }
}