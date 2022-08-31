<?php
session_start();
$_SESSION['state'] = bin2hex(random_bytes(5));

require_once "inc/config.php";
require_once "Controller/Api/Routes.php";

/**
 * redirect to verifier page (sample doesn't implement isuance)
 */
header("Location: verifier.html");
exit();
